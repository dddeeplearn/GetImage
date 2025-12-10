using System;
using System.IO;
using System.Net.Sockets;
using System.Net.Sockets;
using System.Text;
using System.Threading;
namespace GetImage
{
    public partial class Form1 : Form
    {
        private TcpClient _tcpClient = null;
        private NetworkStream _networkStream = null;
        private Thread _receiveThread = null;
        private bool _isReceiving = false;
        private string save_path="D://ImgSave";
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
        public Form1()
        {
            InitializeComponent();
            server_ip.Text="10.26.145.50";
            server_port.Text="7768";
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void connect_button_Click(object sender, EventArgs e)
        {
            if (_tcpClient == null || !_tcpClient.Connected)
            {
                // 连接操作
                string serverIP = server_ip.Text.Trim();
                int port = int.Parse(server_port.Text.Trim());

                try
                {
                    _tcpClient = new TcpClient();
                    _tcpClient.Connect(serverIP, port);
                    _networkStream = _tcpClient.GetStream();

                    _isReceiving = true;
                    _receiveThread = new Thread(new ThreadStart(ReceiveImageData));
                    _receiveThread.IsBackground = true;
                    _receiveThread.Start();

                    connect_button.Text = "断开连接";
                    SafeUpdateUI($"已连接到开发板 {serverIP}:{port}");
                }
                catch (Exception ex)
                {
                    SafeUpdateUI($"连接失败: {ex.Message}");
                    if (_tcpClient != null) _tcpClient.Close();
                    _tcpClient = null;
                }
            }
            else
            {
                // 断开连接操作
                _isReceiving = false;

                if (_receiveThread != null && _receiveThread.IsAlive)
                    _receiveThread.Join(1000); // 等待接收线程结束

                _networkStream?.Close();
                _tcpClient?.Close();

                connect_button.Text = "连接";
                SafeUpdateUI("已断开与开发板的连接");
            }
        }
        private void ReceiveImageData()
        {
            byte[] readBuffer = new byte[4096]; // 每次从网络读取的缓冲区

            while (_isReceiving && _tcpClient != null && _tcpClient.Connected)
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
                        SafeUpdateUI("开发板断开连接");
                        _isReceiving = false;
                        break;
                    }
                }
                catch (IOException ioEx)
                {
                    SafeUpdateUI($"网络错误: {ioEx.Message}");
                    _isReceiving = false;
                    break;
                }
                catch (Exception ex)
                {
                    SafeUpdateUI($"接收错误: {ex.Message}");
                    _isReceiving = false;
                    break;
                }
            }

            // 清理资源
            if (_tcpClient != null)
            {
                _networkStream?.Close();
                _tcpClient.Close();

                // 更新UI（回到主线程）
                this.Invoke(new Action(() => { connect_button.Text = "连接"; }));
            }
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
                if (files.Length >= 5000)
                {
                    // 通过UI日志告知用户正在进行清理操作
                    SafeUpdateUI($"文件夹中文件数量达到 {files.Length} 个，超过5000个上限，正在清空文件夹...");

                    // 步骤 3: 如果超限，遍历并删除所有文件
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
