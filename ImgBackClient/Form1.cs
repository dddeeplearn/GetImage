using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImgBackClient
{
    public partial class Form1 : Form
    {
        private readonly string _backupSavePath = "D:\\ImageBackup";
        private const int SERVER_PORT = 7772;
        public Form1()
        {
            InitializeComponent();
            server_port.Text = SERVER_PORT.ToString();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private async void connect_button_Click(object sender, EventArgs e)
        {
            string serverIp = server_ip.Text.Trim();
            if (string.IsNullOrEmpty(serverIp))
            {
                MessageBox.Show("请输入服务器IP地址。");
                return;
            }

            connect_button.Enabled = false;
            Log($"开始连接服务器 {serverIp}:{SERVER_PORT}...");

            try
            {
                // 使用Task.Run在后台线程执行备份，避免UI卡死
                await Task.Run(() => StartBackupProcess(serverIp));
                Log("备份任务成功完成。");
            }
            catch (Exception ex)
            {
                Log($"备份过程中发生错误: {ex.Message}");
            }
            finally
            {
                connect_button.Enabled = true;
                Application.Exit();
            }
        }
        private void StartBackupProcess(string serverIp)
        {
            // 确保备份目录存在
            Directory.CreateDirectory(_backupSavePath);

            using (var client = new TcpClient())
            {
                // 设置5秒连接超时
                if (!client.ConnectAsync(serverIp, SERVER_PORT).Wait(5000))
                {
                    throw new Exception("连接服务器超时。");
                }

                Log("已连接服务器，开始接收图片...");
                using (var stream = client.GetStream())
                {
                    int filesReceived = 0;
                    while (true)
                    {
                        // 协议: 4字节文件名长度 + 文件名 + 8字节文件大小 + 文件内容
                        byte[] lenBuffer = ReadExactly(stream, 4);
                        int fileNameLength = BitConverter.ToInt32(lenBuffer, 0);

                        // 检查结束信号
                        if (fileNameLength == 0)
                        {
                            break; // 传输结束
                        }

                        byte[] fileNameBytes = ReadExactly(stream, fileNameLength);
                        string fileName = Encoding.UTF8.GetString(fileNameBytes);

                        byte[] sizeBuffer = ReadExactly(stream, 8);
                        long fileSize = BitConverter.ToInt64(sizeBuffer, 0);

                        Log($"正在接收: {fileName}, 大小: {fileSize} 字节");

                        byte[] fileData = ReadExactly(stream, (int)fileSize);

                        string savePath = Path.Combine(_backupSavePath, fileName);
                        File.WriteAllBytes(savePath, fileData);
                        filesReceived++;
                    }
                    Log($"共接收并保存了 {filesReceived} 张图片。");
                }
            }
        }
        private byte[] ReadExactly(NetworkStream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read == 0) throw new EndOfStreamException("连接在读取数据时意外关闭。");
                offset += read;
            }
            return buffer;
        }

        // 线程安全的日志记录方法
        private void Log(string message)
        {
            if (textBox1.InvokeRequired)
            {
                textBox1.Invoke(new Action(() => Log(message)));
            }
            else
            {
                textBox1.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            }
        }
    }
}
