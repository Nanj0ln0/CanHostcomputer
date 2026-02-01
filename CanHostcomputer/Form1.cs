using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace CanHostcomputer
{
    public partial class Form1 : Form
    {
        private int i = 0;

        const int slot = 0;

        // can 通道号
        const int channel = 0;

        public Form1()
        {
            InitializeComponent();

            // 绑定窗口关闭事件，确保停止后台任务并释放资源
            this.FormClosing += Form1_FormClosing;
        }

        // 把启动逻辑改为 async，启动完成后在 UI 线程更新按钮状态为“已连接”
        private async void button1_Click(object sender, EventArgs e)
        {
            // 初始化 UI 日志区
            textBox1.Clear();
            textBox1.Text = "DEMO VS2025\r\n";

            // 启动新的 CAN 架构（后台 adapter + UI 批量消费）
            try
            {
                await StartCanAsync();

                // 获取并显示驱动信息（不用直接调用 Canlib.*）
                try
                {
                    var info = await canAdapter!.GetDriverInfoAsync();
                    textBox1.Text += $"Found CANlib version {info.VersionMajor}.{info.VersionMinor}\r\n";
                    textBox1.Text += $"Found {info.NumberOfChannels} channels\r\n";
                    textBox1.Text += "----------------------------------------\r\n";
                }
                catch (Exception ex)
                {
                    textBox1.Text += "GetDriverInfoAsync failed: " + ex.Message + "\r\n";
                }

                // 启动成功：更新按钮状态
                button1.Enabled = false;
                button1.Text = "已连接";
                button3.Enabled = true;

                // 连接成功后不允许修改波特率（防止运行时更改）
                try
                {
                    comboBoxBaud.Enabled = false;
                }
                catch { /* 忽略窗体已关闭或控件不存在情况 */ }
            }
            catch (Exception ex)
            {
                textBox1.AppendText("StartCanAsync failed: " + ex.Message + "\r\n");
                return;
            }
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            byte[] Frame = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

            try
            {
                // 优先通过 adapter 发送，如果 adapter 不可用则记录错误
                if (canAdapter != null)
                {
                    var frame = new CanFrame { Id = 0x123, Data = Frame, Dlc = 8, Flags = 0, Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                    var ok = await canAdapter.SendAsync(frame, default);
                    textBox1.Text += string.Format("发送数据结果: " + ok + "\r\n");
                }
                else
                {
                    textBox1.Text += "canAdapter not initialized\r\n";
                }
            }
            catch (Exception ex)
            {
                textBox1.Text += "发送异常: " + ex.Message + "\r\n";
            }
        }

        // 把断开逻辑改为 async：等待 StopCanAsync 完成后恢复按钮状态为“连接”
        private async void button3_Click(object sender, EventArgs e)
        {
            try
            {
                await StopCanAsync();
            }
            catch (Exception ex)
            {
                textBox1.AppendText("StopCanAsync failed: " + ex.Message + "\r\n");
            }

            textBox1.Text += string.Format("关闭总线\r\n");

            // 恢复 UI 按钮到未连接状态
            try
            {
                button1.Enabled = true;
                button1.Text = "连接";
                button3.Enabled = false;

                // 断开后允许修改波特率
                try
                {
                    comboBoxBaud.Enabled = true;
                }
                catch { /* 忽略窗体已关闭情况 */ }
            }
            catch { /* 忽略窗体已关闭情况 */ }
        }

    
        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try
            {
                // 在关闭窗体时同步等待 Stop，以尽量确保后台任务结束
                StopCanAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // 忽略关闭期间的错误，保证退出
            }
        }
    }
}
