namespace ImgBackClient
{
    partial class ImgBack
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            textBox1 = new TextBox();
            getimg_button = new Button();
            connect_button = new Button();
            label1 = new Label();
            server_port = new TextBox();
            ip = new Label();
            server_ip = new TextBox();
            SuspendLayout();
            // 
            // textBox1
            // 
            textBox1.Location = new Point(32, 139);
            textBox1.Multiline = true;
            textBox1.Name = "textBox1";
            textBox1.ScrollBars = ScrollBars.Vertical;
            textBox1.Size = new Size(558, 299);
            textBox1.TabIndex = 13;
            // 
            // getimg_button
            // 
            getimg_button.Location = new Point(32, 89);
            getimg_button.Name = "getimg_button";
            getimg_button.Size = new Size(112, 34);
            getimg_button.TabIndex = 12;
            getimg_button.Text = "获取图像";
            getimg_button.UseVisualStyleBackColor = true;
            // 
            // connect_button
            // 
            connect_button.Location = new Point(474, 21);
            connect_button.Name = "connect_button";
            connect_button.Size = new Size(112, 34);
            connect_button.TabIndex = 11;
            connect_button.Text = "连接";
            connect_button.UseVisualStyleBackColor = true;
            connect_button.Click += connect_button_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(240, 24);
            label1.Name = "label1";
            label1.Size = new Size(47, 24);
            label1.TabIndex = 10;
            label1.Text = "port";
            // 
            // server_port
            // 
            server_port.Location = new Point(293, 21);
            server_port.Name = "server_port";
            server_port.Size = new Size(150, 30);
            server_port.TabIndex = 9;
            // 
            // ip
            // 
            ip.AutoSize = true;
            ip.Location = new Point(32, 21);
            ip.Name = "ip";
            ip.Size = new Size(26, 24);
            ip.TabIndex = 8;
            ip.Text = "IP";
            // 
            // server_ip
            // 
            server_ip.Location = new Point(64, 18);
            server_ip.Name = "server_ip";
            server_ip.Size = new Size(150, 30);
            server_ip.TabIndex = 7;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(11F, 24F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(611, 450);
            Controls.Add(textBox1);
            Controls.Add(getimg_button);
            Controls.Add(connect_button);
            Controls.Add(label1);
            Controls.Add(server_port);
            Controls.Add(ip);
            Controls.Add(server_ip);
            Name = "Form1";
            Text = "Form1";
            Load += Form1_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox textBox1;
        private Button getimg_button;
        private Button connect_button;
        private Label label1;
        private TextBox server_port;
        private Label ip;
        private TextBox server_ip;
    }
}
