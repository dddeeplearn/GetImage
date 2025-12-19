using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets;
using System.Text;
using System.Threading;
namespace GetImage
{
    public partial class GetImg : Form
    {
        private TcpClient _tcpClient = null;
        private NetworkStream _networkStream = null;
        private Thread _receiveThread = null;
        private bool _isReceiving = false;
        private string save_path="D://ImgSave";
        private System.Windows.Forms.Timer _reconnectTimer;
        // 标记是否是用户主动断开连接
        private bool _isUserDisconnect = false;
        // 协议解析相关字段
        private enum ReceiveState
        {
            ReadingFilenameLength,
            ReadingFilename,
            ReadingImageSize,
            ReadingImageData
        }
        //private const int HEADER_SIZE = 4; // 包头大小（4字节，表示图像长度）
        //private MemoryStream _imageBuffer = new MemoryStream(); // 累积图像数据的缓冲区
        //private int _expectedImageSize = 0; // 当前期望接收的图像大小
        //private int _receivedImageBytes = 0; // 当前已接收的图像字节数
        //private bool _readingHeader = true; // 当前是否正在读取包头
        private ReceiveState _currentState = ReceiveState.ReadingFilenameLength;
        private MemoryStream _buffer = new MemoryStream();
        private int _expectedDataSize = 0;
        private string _currentFilename = "";
        private delegate void SafeUpdateUIDelegate(string message, byte[] imageData = null);
        private TcpListener _imageServer = null;
        private Thread _serverListenThread = null;
        private volatile bool _isServerRunning = false;
        private const int BACKUP_PORT = 7772; // 定义备份端口
        public GetImg()
        {
            InitializeComponent();
            server_ip.Text="192.168.2.101";
            server_port.Text="7768";
            _reconnectTimer = new System.Windows.Forms.Timer();
            _reconnectTimer.Interval = 60000; // 1分钟
            _reconnectTimer.Tick += ReconnectTimer_Tick;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            SafeUpdateUI("程序启动，开始自动连接...");
            AttemptConnect();
            StartImageBackupServer();
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopImageBackupServer();
        }

        private void StartImageBackupServer()
        {
            if (_isServerRunning) return;

            _isServerRunning = true;
            _serverListenThread = new Thread(new ThreadStart(ListenForBackupClients));
            _serverListenThread.IsBackground = true;
            _serverListenThread.Start();
            SafeUpdateUI($"图片备份服务器已在端口 {BACKUP_PORT} 启动。");
        }
        private void StopImageBackupServer()
        {
            if (!_isServerRunning) return;

            _isServerRunning = false;
            _imageServer?.Stop(); 
            _serverListenThread?.Join(500); 
        }
        private void ListenForBackupClients()
        {
            try
            {
                _imageServer = new TcpListener(IPAddress.Any, BACKUP_PORT);
                _imageServer.Start();

                while (_isServerRunning)
                {
                    try
                    {
                        // 阻塞，直到有备份客户端连接进来
                        TcpClient backupClient = _imageServer.AcceptTcpClient();
                        SafeUpdateUI($"备份客户端已连接: {((IPEndPoint)backupClient.Client.RemoteEndPoint).Address}");

                        // 为每个连接的客户端创建一个新线程来处理文件发送，避免阻塞主监听线程
                        Thread clientHandlerThread = new Thread(() => HandleBackupRequest(backupClient));
                        clientHandlerThread.IsBackground = true;
                        clientHandlerThread.Start();
                    }
                    catch (SocketException)
                    {
                        // 当调用_imageServer.Stop()时会触发此异常，是正常关闭流程的一部分
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                SafeUpdateUI($"备份服务器启动失败: {ex.Message}");
            }
        }
        private void HandleBackupRequest(TcpClient client)
        {
            try
            {
                using (var stream = client.GetStream())
                {
                    if (!Directory.Exists(save_path))
                    {
                        SafeUpdateUI("图片存储文件夹不存在，无法执行备份。");
                        return;
                    }

                    string[] imageFiles = Directory.GetFiles(save_path);
                    if (imageFiles.Length == 0)
                    {
                        SafeUpdateUI("文件夹为空，无需备份。");
                        stream.Write(BitConverter.GetBytes(0), 0, 4); // 发送结束信号
                        return;
                    }
                    SafeUpdateUI($"开始向备份客户端发送 {imageFiles.Length} 张图片...");

                    // 遍历并发送每一张图片
                    foreach (string filePath in imageFiles)
                    {
                        byte[] fileData = File.ReadAllBytes(filePath);
                        string fileName = Path.GetFileName(filePath);
                        byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);

                        // 协议: 4字节文件名长度 + 文件名 + 8字节文件大小 + 文件内容
                        stream.Write(BitConverter.GetBytes(fileNameBytes.Length), 0, 4);
                        stream.Write(fileNameBytes, 0, fileNameBytes.Length);
                        stream.Write(BitConverter.GetBytes((long)fileData.Length), 0, 8);
                        stream.Write(fileData, 0, fileData.Length);
                    }

                    // 发送结束信号 (一个长度为0的文件名)
                    stream.Write(BitConverter.GetBytes(0), 0, 4);
                    SafeUpdateUI("所有图片发送完毕。");
                    SafeUpdateUI("开始清理已备份的图片...");
                    int deletedCount = 0;
                    foreach (string filePathToDelete in imageFiles)
                    {
                        try
                        {
                            File.Delete(filePathToDelete);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            // 如果某个文件删除失败，记录日志并继续尝试删除下一个
                            SafeUpdateUI($"删除文件 {Path.GetFileName(filePathToDelete)} 失败: {ex.Message}");
                        }
                    }
                    SafeUpdateUI($"清理完成，共删除了 {deletedCount} / {imageFiles.Length} 个文件。");
                }
            }
            catch (Exception ex)
            {
                SafeUpdateUI($"向备份客户端发送图片时出错: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }
        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void connect_button_Click(object sender, EventArgs e)
        {
            if (_tcpClient != null && _tcpClient.Connected)
            {
                // 用户主动断开连接
                SafeUpdateUI("用户手动断开连接。");
                _isUserDisconnect = true; // 标记为用户主动断开
                _reconnectTimer.Stop(); // 停止自动重连
                CleanupConnection();
            }
            else
            {
                // 用户手动连接
                SafeUpdateUI("正在手动连接...");
                _isUserDisconnect = false; // 清除用户主动断开标记
                _reconnectTimer.Stop(); // 先停止计时器，立即尝试连接
                AttemptConnect();
            }
        }
        private void AttemptConnect()
        {
            if (_tcpClient != null && _tcpClient.Connected) return; // 如果已经连接，则不执行任何操作

            string serverIP = server_ip.Text.Trim();
            int port = int.Parse(server_port.Text.Trim());

            try
            {
                CleanupConnection(); // 连接前先确保清理了旧的资源

                _tcpClient = new TcpClient();
                // 可以设置一个连接超时，避免UI假死
                var result = _tcpClient.BeginConnect(serverIP, port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5));

                if (!success)
                {
                    throw new Exception("连接超时。");
                }

                try
                {
                    // C# 设置Keep-Alive需要一个字节数组作为参数
                    // 结构: [on/off (1/0)], [keepalivetime (ms)], [keepaliveinterval (ms)]
                    // C#中的单位是毫秒
                    byte[] keepAliveValues = new byte[12];
                    // 1. 开启Keep-Alive
                    BitConverter.GetBytes(1).CopyTo(keepAliveValues, 0);
                    // 2. 60秒内无数据，则发送心跳
                    BitConverter.GetBytes(60000).CopyTo(keepAliveValues, 4);
                    // 3. 心跳包每10秒发送一次
                    BitConverter.GetBytes(10000).CopyTo(keepAliveValues, 8);

                    // 调用底层IOControl来设置
                    _tcpClient.Client.IOControl(IOControlCode.KeepAliveValues, keepAliveValues, null);
                    SafeUpdateUI("客户端 TCP Keep-Alive 已启用。");
                }
                catch (Exception ex)
                {
                    SafeUpdateUI($"警告: 设置客户端TCP Keep-Alive失败: {ex.Message}");
                }

                _tcpClient.EndConnect(result);
                _networkStream = _tcpClient.GetStream();

                _isReceiving = true;
                _receiveThread = new Thread(new ThreadStart(ReceiveImageData));
                _receiveThread.IsBackground = true;
                _receiveThread.Start();

                SafeUpdateUI($"已成功连接到 {serverIP}:{port}");
            }
            catch (Exception ex)
            {
                SafeUpdateUI($"连接失败: {ex.Message}");
                HandleDisconnection(); // 连接失败，处理断开逻辑
            }
        }

        private void ReceiveImageData()
        {
            byte[] readBuffer = new byte[4096]; // 每次从网络读取的缓冲区

            while (_isReceiving)
            {
                try
                {
                    // 从网络流读取数据（阻塞调用）
                    int bytesRead = _networkStream.Read(readBuffer, 0, readBuffer.Length);

                    if (bytesRead > 0)
                    {
                        // 处理接收到的数据
                        ProcessReceivedData(readBuffer, bytesRead);
                    }
                    else
                    {
                        // 连接已关闭
                        _isReceiving = false;
                        SafeUpdateUI("开发板断开连接");
                        this.Invoke(new Action(HandleDisconnection));
                        break;
                    }
                }
                catch (IOException ioEx)
                {
                    _isReceiving = false;
                    SafeUpdateUI("网络连接丢失。");
                    this.Invoke(new Action(HandleDisconnection));
                }
                catch (Exception ex)
                {
                    SafeUpdateUI($"接收错误: {ex.Message}");
                    _isReceiving = false;
                    this.Invoke(new Action(HandleDisconnection));
                    break;
                }
            }

            // 清理资源
            //if (_tcpClient != null)
            //{
            //    _networkStream?.Close();
            //    _tcpClient.Close();

            //    // 更新UI（回到主线程）
            //    this.Invoke(new Action(() => { connect_button.Text = "连接"; }));
            //}
        }
        private void HandleDisconnection()
        {
            CleanupConnection();
            if (!_isUserDisconnect) // 如果不是用户主动断开，则启动自动重连
            {
                SafeUpdateUI("将在1分钟后尝试自动重连...");
                _reconnectTimer.Start();
            }
        }
        private void CleanupConnection()
        {
            _isReceiving = false;

            // 等待接收线程安全退出
            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                _receiveThread.Join(500);
            }

            _networkStream?.Close();
            _tcpClient?.Close();
            _tcpClient = null;

            // 在UI线程上更新UI状态
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => connect_button.Text = "连接"));
            }
            else
            {
                connect_button.Text = "连接";
            }
        }
        private void ReconnectTimer_Tick(object sender, EventArgs e)
        {
            _reconnectTimer.Stop(); // 先停止计时，避免在连接过程中重复触发
            SafeUpdateUI("自动重连计时结束，尝试重新连接...");
            AttemptConnect();
        }
        // 处理接收到的数据（协议解析核心）
        private void ProcessReceivedData(byte[] buffer, int bytesRead)
        {
            int bufferOffset = 0;

            while (bufferOffset < bytesRead)
            {
                // 确定当前状态需要多少字节
                int bytesNeeded;
                if (_currentState == ReceiveState.ReadingFilenameLength || _currentState == ReceiveState.ReadingImageSize)
                {
                    bytesNeeded = 4 - (int)_buffer.Length;
                }
                else
                {
                    bytesNeeded = _expectedDataSize - (int)_buffer.Length;
                }

                int bytesToCopy = Math.Min(bytesRead - bufferOffset, bytesNeeded);

                if (bytesToCopy > 0)
                {
                    _buffer.Write(buffer, bufferOffset, bytesToCopy);
                    bufferOffset += bytesToCopy;
                }

                // 检查是否已收满当前阶段所需的数据
                bool isBlockComplete = false;
                if (_currentState == ReceiveState.ReadingFilenameLength || _currentState == ReceiveState.ReadingImageSize)
                {
                    if (_buffer.Length == 4) isBlockComplete = true;
                }
                else
                {
                    if (_buffer.Length == _expectedDataSize) isBlockComplete = true;
                }

                if (isBlockComplete)
                {
                    switch (_currentState)
                    {
                        case ReceiveState.ReadingFilenameLength:
                            byte[] lengthBytes = _buffer.ToArray();
                            Array.Reverse(lengthBytes); // Python struct.pack('!I') is big-endian
                            int filenameLength = BitConverter.ToInt32(lengthBytes, 0);
                            _buffer.SetLength(0); // 清空缓冲区

                            // *** 关键修复点 ***
                            // 检查是否是批次结束标志
                            if (filenameLength == -1) // 0xFFFFFFFF
                            {
                                SafeUpdateUI("一批图像接收完成。");
                                // 保持当前状态，等待下一批次的第一个文件名长度
                                _currentState = ReceiveState.ReadingFilenameLength;
                            }
                            else
                            {
                                _expectedDataSize = filenameLength;
                                SafeUpdateUI($"接收到文件名长度: {_expectedDataSize} 字节");
                                _currentState = ReceiveState.ReadingFilename;
                            }
                            break;

                        case ReceiveState.ReadingFilename:
                            _currentFilename = Encoding.UTF8.GetString(_buffer.ToArray());
                            SafeUpdateUI($"正在接收文件: {_currentFilename}");
                            _buffer.SetLength(0);
                            _currentState = ReceiveState.ReadingImageSize;
                            break;

                        case ReceiveState.ReadingImageSize:
                            byte[] sizeBytes = _buffer.ToArray();
                            Array.Reverse(sizeBytes);
                            _expectedDataSize = BitConverter.ToInt32(sizeBytes, 0);

                            SafeUpdateUI($"开始接收图像数据，大小: {_expectedDataSize} 字节");
                            _buffer.SetLength(0);
                            _currentState = ReceiveState.ReadingImageData;
                            break;

                        case ReceiveState.ReadingImageData:
                            byte[] imageData = _buffer.ToArray();
                            SaveImageToFile(imageData, _currentFilename);
                            SafeUpdateUI($"图像 {_currentFilename} 接收并保存成功 ({imageData.Length} 字节)", null); // 预览可以在SaveImageToFile后做

                            // 重置状态，准备接收下一个文件
                            _buffer.SetLength(0);
                            _currentState = ReceiveState.ReadingFilenameLength;
                            break;
                    }
                }
            }
        }
        private void SaveImageToFile(byte[] imageData, string filename)
        {
            try
            {
                if (!Directory.Exists(save_path))
                    Directory.CreateDirectory(save_path);
                string[] files = Directory.GetFiles(save_path);
                if (files.Length >= 1000)
                {
                    // 通过UI日志告知用户正在进行清理操作
                    SafeUpdateUI($"文件夹中文件数量达到 {files.Length} 个，超过1000个上限，正在清空文件夹...");

                    //遍历并删除所有文件
                    foreach (string file in files)
                    {
                        File.Delete(file);
                    }

                    SafeUpdateUI("文件夹已清空完毕。");
                }
                string fullPath = Path.Combine(save_path, filename);
                File.WriteAllBytes(fullPath, imageData);
            }
            catch (Exception ex)
            {
                SafeUpdateUI($"保存文件 {filename} 失败: {ex.Message}");
            }
        }

        // 线程安全地更新UI（显示日志和预览图像）
        private void SafeUpdateUI(string message, byte[] imageData = null)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new SafeUpdateUIDelegate(SafeUpdateUI), message, imageData);
                return;
            }

            // 更新日志文本框
            textBox1.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");

            // 如果有图像数据，在PictureBox中预览
            //if (imageData != null && picPreview != null)
            //{
            //    try
            //    {
            //        using (MemoryStream ms = new MemoryStream(imageData))
            //        {
            //            picPreview.Image = Image.FromStream(ms);
            //        }
            //    }
            //    catch
            //    {
            //        // 如果不是有效的图像数据，忽略预览
            //    }
            //}
        }
        private void getimg_button_Click(object sender, EventArgs e)
        {

        }
    }
}
