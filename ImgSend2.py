import socket
import struct
import time
import os
import glob
import threading
import configparser


class ImageServer:
    def __init__(self, host='0.0.0.0', port=8888):
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
            # 假设配置文件格式：
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
        while self.running:
            try:
                # 读取detectsign值
                detectsign = self.read_detectsign()
                print(f"检查detectsign值: {detectsign} (时间: {time.strftime('%Y-%m-%d %H:%M:%S')})")

                if detectsign == 1 and self.client_socket:
                    print("detectsign=1, 开始发送图像...")
                    sent_count = self.send_all_images()

                    # 发送完成标记
                    if sent_count > 0:
                        print(f"图像发送完成，共发送 {sent_count} 张图像")

                        # 发送结束标记
                        self.client_socket.send(struct.pack('!I', 0xFFFFFFFF))

                    # 重置标志位（可选）
                    # 这里可以添加代码将detectsign重置为0

                # 等待10秒
                time.sleep(10)

            except Exception as e:
                print(f"检查或发送过程中出错: {e}")
                if not self.running:
                    break

                # 如果连接断开，尝试重新连接
                try:
                    self.client_socket.close()
                except:
                    pass

                # 等待客户端重新连接
                print("等待客户端重新连接...")
                self.client_socket, self.client_address = self.server_socket.accept()
                print(f"重新连接: {self.client_address}")

    def start_server(self):
        """启动服务器"""
        self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server_socket.bind((self.host, self.port))
        self.server_socket.listen(1)

        print(f"服务器启动在 {self.host}:{self.port}")
        print("等待客户端连接...")

        # 接受客户端连接
        self.client_socket, self.client_address = self.server_socket.accept()
        print(f"客户端已连接: {self.client_address}")

        # 启动检查线程
        self.running = True
        check_thread = threading.Thread(target=self.check_and_send)
        check_thread.daemon = True
        check_thread.start()

        # 保持主线程运行
        try:
            while self.running:
                time.sleep(1)
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