import socket
import struct
import time
import os
import glob
import threading
import configparser


class ImageServer:
    def __init__(self, host='0.0.0.0', port=7768):
        self.host = host
        self.port = port
        self.server_socket = None
        self.client_socket = None
        self.client_address = None
        self.running = False
        self.image_dir = "/home/C/myCapture/ImgSave"
        self.config_file = "/home/C/myCapture/config.ini"

    def read_detectsign(self):
        """读取配置文件中的detectsign变量值"""
        try:
            config = configparser.ConfigParser()
            config.read(self.config_file)
            # 配置文件格式：
            # [Detection]
            # detectsign = 1
            detectsign = config.getint('Detection', 'detectsign')
            return detectsign
        except Exception as e:
            print(f"读取配置文件失败: {e}")
            return 0

    def send_image(self, image_path):
        """发送单个图像文件"""
        # try:
        with open(image_path, 'rb') as f:
            image_data = f.read()

        # 获取文件大小
        file_size = os.path.getsize(image_path)
        filename = os.path.basename(image_path)

        print(f"正在发送图像: {filename}, 大小: {file_size} 字节")

        # 发送协议：4字节文件名长度 + 文件名 + 4字节文件大小 + 文件内容
        filename_bytes = filename.encode('utf-8')

        # 发送文件名长度和文件名
        self.client_socket.send(struct.pack('!I', len(filename_bytes)))
        self.client_socket.send(filename_bytes)

        # 发送文件大小
        self.client_socket.send(struct.pack('!I', file_size))

        # 发送文件内容
        self.client_socket.send(image_data)

        return True
        # except Exception as e:
        #     print(f"发送图像 {image_path} 失败: {e}")
        #     return False

    def send_all_images(self):
        """发送指定目录下的所有图像"""
        if not os.path.exists(self.image_dir):
            print(f"图像目录不存在: {self.image_dir}")
            return 0

        # 获取所有图像文件
        image_extensions = ['*.jpg', '*.jpeg', '*.png', '*.bmp', '*.gif']
        image_files = []
        for ext in image_extensions:
            image_files.extend(glob.glob(os.path.join(self.image_dir, ext)))

        if not image_files:
            print("目录中没有找到图像文件")
            return 0

        sent_count = 0
        for image_file in image_files:
            if self.send_image(image_file):
                sent_count += 1

        return sent_count

    def check_and_send(self):
        """定期检查detectsign并发送图像"""
        while self.running and self.client_socket:
            try:
                # 读取detectsign值
                detectsign = self.read_detectsign()
                print(f"检查detectsign值: {detectsign} (时间: {time.strftime('%Y-%m-%d %H:%M:%S')})")

                if detectsign == 1:
                    print("detectsign=1, 开始发送图像...")
                    sent_count = self.send_all_images()
                    if sent_count > 0:
                        print(f"图像发送完成，共发送 {sent_count} 张图像")
                        self.client_socket.send(struct.pack('!I', 0xFFFFFFFF))
                
                time.sleep(10)

            except (BrokenPipeError, ConnectionResetError) as e:
                print(f"连接已断开: {e}")
                break # 跳出循环，让外层去处理重连
            except Exception as e:
                print(f"检查或发送过程中出错: {e}")
                break # 出现其他错误也断开

        # 清理当前客户端socket
        if self.client_socket:
            self.client_socket.close()
            self.client_socket = None
        print("当前客户端会话结束。")

    def start_server(self):
        """启动服务器"""
        self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server_socket.bind((self.host, self.port))
        self.server_socket.listen(1)

        print(f"服务器启动在 {self.host}:{self.port}")
        print("等待客户端连接...")
        self.running = True
        # 接受客户端连接
        #self.client_socket, self.client_address = self.server_socket.accept()
        #print(f"客户端已连接: {self.client_address}")

        try:
            while self.running: # 使用一个循环来处理断线重连
                print("等待客户端连接...")
                # 接受客户端连接
                self.client_socket, self.client_address = self.server_socket.accept()
                print(f"客户端已连接: {self.client_address}")

                # --- 新增代码: 设置TCP Keep-Alive ---
                try:
                    # 1. 开启Keep-Alive功能
                    self.client_socket.setsockopt(socket.SOL_SOCKET, socket.SO_KEEPALIVE, 1)
                    # 2. 设置TCP_KEEPIDLE: 60秒内无数据，则发送心跳
                    # (注意: TCP_KEEPIDLE, TCP_KEEPINTVL, TCP_KEEPCNT 在非Linux系统上可能不可用)
                    if hasattr(socket, "TCP_KEEPIDLE"):
                        self.client_socket.setsockopt(socket.IPPROTO_TCP, socket.TCP_KEEPIDLE, 60)
                    # 3. 设置TCP_KEEPINTVL: 心跳包每10秒发送一次
                    if hasattr(socket, "TCP_KEEPINTVL"):
                        self.client_socket.setsockopt(socket.IPPROTO_TCP, socket.TCP_KEEPINTVL, 10)
                    # 4. 设置TCP_KEEPCNT: 尝试3次心跳包
                    if hasattr(socket, "TCP_KEEPCNT"):
                        self.client_socket.setsockopt(socket.IPPROTO_TCP, socket.TCP_KEEPCNT, 3)
                    print("TCP Keep-Alive 已为当前连接启用。")
                except Exception as e:
                    print(f"警告: 设置TCP Keep-Alive失败 (当前系统可能不支持): {e}")
                # --- 新增代码结束 ---

                # 启动检查线程
                self.check_and_send()

        # # 保持主线程运行
        # try:
        #     while self.running:
        #         time.sleep(1)
        except KeyboardInterrupt:
            print("\n正在关闭服务器...")
        finally:
            self.stop_server()

    def stop_server(self):
        """停止服务器"""
        self.running = False
        if self.client_socket:
            self.client_socket.close()
        if self.server_socket:
            self.server_socket.close()
        print("服务器已停止")


# 主程序入口
if __name__ == "__main__":
    # 配置参数
    HOST = '0.0.0.0'  # 监听所有网络接口
    PORT = 7768  # 端口号

    server = ImageServer(HOST, PORT)
    server.start_server()