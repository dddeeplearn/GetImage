#原tv7
#功能包含：
#重启
#新转换角度
import json
import os.path
import socket
import struct
import time
import threading
import serial
import spidev
import cv2
import numpy as np
import pickle
from numpy.ma.core import nonzero
from skimage.morphology import skeletonize
import shutil
from datetime import datetime
from pathlib import Path
import subprocess
import configparser
#########Variable Define###########
detect_sign = 0
heart = '0'
data_receive = ''
angle_diff = 0.0
corrected_angle = 0
delay_cap = 1
motor_open_sign = 0
motor_off_sign = 1
conn_active=0
filt_shape = (5, 5)
sigma = 0.5
kernel = np.ones((5, 5), np.uint8)
cap=None
thread_sign=1
i_img=0
base_path = "/home/C/myCapture/ALLImgSave"
target_folder = "/home/C/myCapture/ImgSave"
#########Variable Define###########
########angle detect########
def get_stable_angle(vx, vy):
    # 1. 计算原始角度 (-180, 180]
    raw_angle = np.degrees(np.arctan2(vy, vx))
    raw_angle=-raw_angle
    # 2. 转换到 [0, 360)
    if raw_angle < 0:
        raw_angle += 360
    # 3. 规范化到 [0, 180) 以消除反向向量的影响
    normalized_angle = raw_angle % 180
    return normalized_angle
def angle_out(framecap,dkernel):
    frame_array = cv2.cvtColor(framecap, cv2.COLOR_BGR2GRAY)
    # frame_array = np.array(gray_frame)
    # print('frame_array',frame_array.shape)
    edges = cv2.Canny(frame_array, 150, 205)
    dilated = cv2.dilate(edges, dkernel, iterations=1)
    binary = skeletonize(dilated > 0)
    skeleton = skeletonize(binary)
    c1 = skeleton.astype(np.uint8) * 255
    # contours, _ = cv2.findContours(c1, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    edge_pos = nonzero(c1)
    line_pos = cv2.hconcat([edge_pos[0], edge_pos[1]])
    if line_pos is not None:
        [vx, vy, _, _] = cv2.fitLine(line_pos, cv2.DIST_L1, 0, 0.01, 0.01)
    else:
        vx = None
        vy = None
    if vx != 0 and (vx is not None):
        # angleor =90*vy/abs(vy)-np.arctan(vy / vx) * 180 / np.pi
        # alarmcode = '1'
        angleor=get_stable_angle(vx, vy)
        alarmcode = '1'
    elif vx is None:
        angleor = 0.0
        alarmcode = '2'
    else:
        angleor = 0.0
        alarmcode = '1'
    return angleor,alarmcode
#######angle detect#########

#######delay cap#########
def delay_detect(delay_sign):
    if delay_sign==1:
        time.sleep(1)
        delay_sign=0
        print("相机打开...")
    return delay_sign
#######delay cap#########

########motor control########
ser=serial.Serial(port='/dev/ttyS1',baudrate=115200,timeout=3)
ser.flushInput()
def motor_open(open_sign):
    open_code=b'\x3E\xA3\x01\x08\xEA\xD0\x07\x00\x00\x00\x00\x00\x00\xD7'
    if ser.is_open and open_sign==1:
        ser.write(open_code)
        open_sign=0
    return open_sign
def motor_off(off_sign):
    off_code=b'\x3E\xA3\x01\x08\xEA\xEC\x2C\x00\x00\x00\x00\x00\x00\x18'
    if ser.is_open and off_sign==1:
        ser.write(off_code)
        off_sign=0
    return off_sign
########motor control########

########Light control########
spi=spidev.SpiDev()
spi.open(0,0)
spi.max_speed_hz=3200000

def light_control(color_parma,light_mode):
    data_send=[]
    if light_mode==1:
        data_bite=[]
        for i in range(1,9):
            for color_num in color_parma:
                for bit in range(8):
                    if color_num & (1 << (7 - bit)):
                        data_bite.append(0b110)
                    else:
                        data_bite.append(0b100)
            data_send.extend(data_bite)
    spi.xfer2(data_send)
    time.sleep(0.00005)
########Light control########

#######heart beat#########
def heart_beat():
    global heart
    while True:
        heart='0'
        time.sleep(1)
        heart='1'
        time.sleep(1)
#######heart beat#########

#######file save/read#########
def data_save(anglediff):
    global angle_diff
    data={'angle_diff':anglediff}
    full_path=os.path.join('/home','C','myCapture','parameters','CalibrateData.json')
    if not os.path.exists(os.path.dirname(full_path)):
        os.makedirs(os.path.dirname(full_path))
    with open(full_path,'w') as f:
        json.dump(data,f)

def data_load():
    global angle_diff
    try:
        full_path = os.path.join('/home', 'C', 'myCapture', 'parameters', 'CalibrateData.json')
        with open(full_path,'r') as f:
            data=json.load(f)
            angle_diff=data['angle_diff']

    except Exception as e:
        print('读取错误:',e)
        angle_diff = 0.0
#######file save/read#########

#######data reveive#########

def handle_receive(client_conn):
    global data_receive
    global conn_active
    revsign=1
    try:
        while True:
            if conn_active==1:
                #client_conn.settimeout(5)
                data=client_conn.recv(1024)
                if data:
                    data_receive=data.decode('utf-8')
                    print(f"客户端信息：{data_receive}")
                    revsign=1
                if revsign:
                    print("等待接收客户端信息...")
                    revsign=0
            elif client_conn:
                client_conn.close()
                break
    except Exception as e:
        print(f'接收数据失败:{e}')

#######data reveive#########
def handle_delete(file_path):
    # global base_path
    # base_path = "/home/C/myCapture/ALLImgSave"
    try:
        shutil.rmtree(file_path)
        os.makedirs(file_path, exist_ok=True)
        #print(f"✅ 快速清理完成: {base_path}")
    except Exception as e:
        print(f"❌ {file_path}清理失败: {e}")
    if not os.path.exists(file_path):
        os.makedirs(file_path)
# def handle_fileprocess():
#     global base_path
#     base_path = "/home/C/myCapture/ALLImgSave"
#     try:
#         while True:
#             with os.scandir(base_path) as entries:
#
#     except Exception as e:
#         print(f"清理失败: {e}")

def move_file(source_folder,target_folder,file_count=5):
    # global base_path
    # base_path = "/home/C/myCapture/ALLImgSave"
    Path(source_folder).mkdir(parents=True, exist_ok=True)
    try:
        files=[os.path.join(source_folder,filename)
              for filename in os.listdir(source_folder)]
        if not files:
            print(f"源文件夹{source_folder}无文件")
            return
        files.sort(key=os.path.getmtime,reverse=True)
        lastfile=files[:min(file_count,len(files))]
        for file_path in lastfile:
            shutil.move(file_path,target_folder)
    except Exception as e:
        print(f"移动失败")
def config_write(section,option,value,config_path):
    config = configparser.ConfigParser()
    try:
        if os.path.exists(config_path):
            config.read(config_path)
        if not config.has_section(section):
            config.add_section(section)
        config.set(section, option, value)
        with open(config_path, 'w') as configfile:
            config.write(configfile)
    except Exception as e:
        print(f"配置写入失败：{e}")
    
def handle_client(client_socket,client_add):
    global conn_active
    global data_receive
    global detect_sign
    global motor_open_sign
    global motor_off_sign
    global delay_cap
    global corrected_angle
    global angle_diff
    global cap
    global thread_sign
    global i_img
    global base_path
    global target_folder
    config_path='/home/C/myCapture/config.ini'
    heart_threading = threading.Thread(target=heart_beat)
    heart_threading.daemon = True
    heart_threading.start()
    data_load()
    print(f"标定角度：{angle_diff}")
    while True:
        try:
            motor_off_sign = motor_off(motor_off_sign)
            motor_open_sign = 1
            light_control((0, 0, 0), 1)
            conn = client_socket
            addr = client_add
            print(f"客户端地址：{addr}")
            print("Running...")
            conn_active=1
            receive_threading = threading.Thread(target=handle_receive, args=(conn,))
            receive_threading.daemon = True
            receive_threading.start()
            ser.flushInput()
            while True:
                if data_receive=='Start':
                    detect_sign=1
                    print(f"检测状态：{data_receive}")
                    light_control((255,255,255),1)
                elif data_receive=='Stop':
                    detect_sign=0
                    print(f"检测状态：{data_receive}")
                    light_control((0,0,0),1)
                elif data_receive=='Calibration':
                    detect_sign=2
                    print(f"检测状态：{data_receive}")
                    light_control((255, 255, 255), 1)
                elif data_receive=='BreakLine':
                    detect_sign=4
                    light_control((0, 0, 0), 1)
                elif data_receive=='Reboot':
                    detect_sign = 3
                    light_control((0, 0, 0), 1)
                data_receive=''
                if detect_sign==1:
                    motor_open_sign=motor_open(motor_open_sign)
                    motor_off_sign=1
                    delay_cap=delay_detect(delay_cap)
                    ret,frame=cap.read()
                    now = datetime.now()
                    time_str = now.strftime("%Y%m%d_%H%M%S%f")[:-3]
                    filename = os.path.join(base_path, f"{time_str}.png")
                    cv2.imwrite(filename, frame)
                    i_img = i_img + 1
                    if i_img > 100:
                        i_img = 1
                        handle_delete(base_path)
                        # heart_threading = threading.Thread(target=handle_delete,args=(base_path,))
                        # heart_threading.daemon = True
                        # heart_threading.start()
                    angle_or,Alarmcode=angle_out(frame,kernel)
                    # print(f"angle_or:{type(angle_or)}")
                    # print(f"angle_diff:{type(angle_diff)}")
                    if Alarmcode=='1':
                        corrected_angle= float(angle_or) - angle_diff
                    elif Alarmcode=='2':
                        corrected_angle=0
                elif detect_sign==0:
                    motor_off_sign=motor_off(motor_off_sign)
                    delay_cap=1
                    motor_open_sign=1
                    Alarmcode = '3'
                    corrected_angle = 0
                elif detect_sign == 2:
                    motor_open_sign=motor_open(motor_open_sign)
                    motor_off_sign = 1
                    delay_cap = delay_detect(delay_cap)
                    ret, frame = cap.read()

                    angle,AlarmCalibrate=angle_out(frame,kernel)
                    print(f"Alarm Code:{AlarmCalibrate}")
                    if AlarmCalibrate=='1':
                        angle_diff=float(angle)
                        Alarmcode ='4'
                        corrected_angle=float(angle)
                        print(f"标定成功：{Alarmcode,angle_diff}")
                    else:
                        Alarmcode ='5'
                        angle_diff=0
                        corrected_angle = 0
                        print(f"标定失败：{Alarmcode}")
                    data_save(angle_diff)
                    time.sleep(3)
                    detect_sign = 0
                    light_control((0, 0, 0), 1)
                elif detect_sign == 3:
                    motor_off_sign = motor_off(motor_off_sign)
                    motor_open_sign = 1
                    time.sleep(3)
                    os.system("reboot")
                elif detect_sign == 4:
                    motor_off_sign = motor_off(motor_off_sign)
                    motor_open_sign = 1
                    handle_delete(target_folder)
                    move_file(base_path, target_folder, file_count=5)
                    config_write('Detection','detectsign','1',config_path)
                    time.sleep(10)
                    config_write('Detection','detectsign','0',config_path)
                    detect_sign =0
                #data=pickle.dumps(frame_array)
                senddata=heart+','+Alarmcode+','+str("{:.2f}".format(corrected_angle))+'\n'
                # if data_receive=='stop':
                #     senddata=heart+','+'3'+','+'0.00'+'\n'
                sendformat = senddata.encode('utf-8')
                #arr_length=len(sendformat)

                if detect_sign==0:
                    time.sleep(1)
                    conn.sendall(sendformat)
                else:
                    conn.sendall(sendformat)
        except Exception as e:
            print(f"Error：{e}")
            detect_sign=0
            motor_off(motor_off_sign)
            light_control((0, 0, 0), 1)
            data_receive = 'Stop'
            conn_active=0
            time.sleep(0.01)
            break
    # finally:
    #     if conn:
    #         conn.close()
    #     sock.close()
    #     sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    #     sock.bind(('0.0.0.0', 7764))
    #     sock.listen(1)
def mount_share(server_ip,share_name,mount_point,username,password):
    Path(mount_point).mkdir(parents=True, exist_ok=True)
    mount_cmd = [
        'mount', '-t', 'cifs',
        f'//{server_ip}/{share_name}',
        mount_point,
        '-o', f'username={username},password={password}'
    ]
    print(f"{mount_cmd}")
    try:
        subprocess.run(mount_cmd)
    except Exception as e:
        print(f"挂载失败：{e}")
def main():
    global cap
    global thread_sign
    global motor_off_sign
    global base_path
    server_ip='10.26.145.240'
    share_name='新建文件夹'
    username='ybyf005'
    password='ybyf005'
    base_path = "/home/C/myCapture/ALLImgSave"
    send_path = "/home/C/myCapture/ImgSave"
    try:
        shutil.rmtree(base_path)
        os.makedirs(base_path, exist_ok=True)
        print(f"✅ 快速清理完成: {base_path}")
    except Exception as e:
        print(f"❌ 清理失败: {e}")
    if not os.path.exists(base_path):
        os.makedirs(base_path)
    if not os.path.exists(send_path):
        os.makedirs(send_path)
    # mount_share(server_ip,share_name,send_path,username,password)
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.bind(('0.0.0.0', 7764))
    sock.listen(5)
    print("Running...")

    cap = cv2.VideoCapture(0, cv2.CAP_V4L2)
    # fourcc=cv2.VideoWriter.fourcc('M','J','P','G')
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
    cap.set(cv2.CAP_PROP_FPS, 60)
    add_client=None
    motor_off_sign  = motor_off(1)

    light_control((0, 0, 0), 1)
    try:
        while True:
            if add_client is not None:
                print(f"连接地址{add_client}")
            else:
                print(f"TCP未连接")

            conn_client,add_client=sock.accept()
            #conn_client.settimeout(5)
            thread_sign=0
            print(f"连接地址{add_client} \n 检查位置：x")
            client_thread = threading.Thread(target=handle_client,args=(conn_client,add_client))
            client_thread.daemon=True
            client_thread.start()
    except Exception as e:
        print(f"Error:{e}")
    finally:
        sock.close()

if __name__ =='__main__':
    main()