using System; 
using System.Text; 
using System.Threading; 
using System.Threading.Channels; 
using System.Threading.Tasks; 
using System.Windows.Forms;
using System.Collections.Generic;
using Kvaser.CanLib;

/*
 can读取器的设计思路：
        把底层 CAN 读取（由 ICanAdapter 提供的 Channel<CanFrame>）和 WinForms UI 连接起来，
        使用一个后台任务批量消费通道数据并在 UI 线程上一次性追加文本，减少跨线程更新频次。
 */


namespace CanHostcomputer
{ 
    public partial class Form1 : Form 
    { 
        private ICanAdapter? canAdapter; // 可选的 CAN 适配器接口实例字段
        private CancellationTokenSource? uiCts; // UI 消费循环使用的取消令牌源字段
        private Task? uiTask; // 后台的 UI 消费任务字段

        // 每次批处理最大数量与批处理等待（ms）
        private const int BatchMax = 200; // 批处理时一次最多读取的帧数量常量
        private const int BatchWaitMs = 50; // 当队列为空时等待的毫秒数常量

        private Dictionary<int, long> lastTimestamps = new Dictionary<int, long>(); // 新增此字段

        public bool IsCanRunning => uiCts != null && !uiCts.IsCancellationRequested; // 只读属性：表示 CAN 是否正在运行

        private void InitCanIntegration() // 初始化 CAN 方法（创建适配器并设置日志回调）
        {
            // 优先使用 UI 下拉选择适配器，其次使用环境变量 CAN_ADAPTER，最后回退到默认 kvaser
            string adapterName;
            try
            {
                adapterName = comboBoxAdapter?.SelectedItem?.ToString()
                              ?? Environment.GetEnvironmentVariable("CAN_ADAPTER")
                              ?? "kvaser";
            }
            catch
            {
                adapterName = "kvaser";
            }

            // 读取波特率选择（默认 500k）
            int bitrate = Canlib.canBITRATE_500K;
            try
            {
                // 优先读取 UI 下拉选择，否则读取环境变量 CAN_BAUD（可选）
                var sel = comboBoxBaud?.SelectedItem?.ToString() ?? Environment.GetEnvironmentVariable("CAN_BAUD");
                if (!string.IsNullOrEmpty(sel) && sel.Contains("250"))
                {
                    bitrate = Canlib.canBITRATE_250K;
                }
                else
                {
                    bitrate = Canlib.canBITRATE_500K;
                }
            }
            catch { bitrate = Canlib.canBITRATE_500K; }

            // 创建适配器并传入 logger 回调，同时把选择的波特率传入工厂
            canAdapter = CanAdapterFactory.Create(adapterName, channelIndex: 0, capacity: 5000, logger: (msg) =>
            {
                try
                {
                    if (IsHandleCreated)
                        BeginInvoke((Action)(() => textBox1.AppendText(msg + "\r\n")));
                }
                catch { }
            }, bitrate: bitrate);
        }

        // 启动 CAN（后台 reader + UI 消费者）
        public async Task StartCanAsync() // 异步方法：启动 CAN 读取和 UI 消费
        { 
            if (IsCanRunning) return; // 如果已经在运行则直接返回

            if (canAdapter == null) InitCanIntegration(); // 如果适配器为 null 则初始化
            uiCts = new CancellationTokenSource(); // 创建用于 UI 消费循环的取消令牌源

            await canAdapter!.StartAsync(uiCts.Token).ConfigureAwait(false); // 启动适配器的后台读取任务，并传入取消令牌

            // 启动 UI 消费者任务（读取 ChannelReader 并按批更新 TextBox）
            uiTask = Task.Run(() => UiConsumeLoop(canAdapter!.Frames, uiCts.Token), uiCts.Token); // 在线程池中启动 UI 消费循环任务
        } 

        // 停止 CAN（停止 UI 消费 + 关闭 adapter）
        public async Task StopCanAsync() // 异步方法：停止 CAN 和清理资源
        { 
            if (!IsCanRunning && canAdapter == null) return; // 如果既不运行且没有适配器则直接返回

            try
            { // 尝试取消和等待任务完成
                uiCts?.Cancel(); // 取消 UI 消费循环
                if (uiTask != null) await Task.WhenAny(uiTask, Task.Delay(500)).ConfigureAwait(false); // 等待 uiTask 最多 500ms
                if (canAdapter != null) await canAdapter.StopAsync().ConfigureAwait(false); // 停止适配器
            }
            finally
            { // 无论如何都进行清理工作
                uiTask = null; // 清空任务引用
                uiCts?.Dispose(); // 释放取消令牌源
                uiCts = null; // 清空取消令牌源引用
                // dispose adapter so next Start creates a fresh one
                try
                { // 如果适配器实现 IDisposable 则释放它
                    if (canAdapter is IDisposable d)
                    {
                        d.Dispose(); // 调用 Dispose 释放资源
                    }
                }
                catch { } // 忽略释放时的异常
                canAdapter = null; // 清空适配器引用，使下次 Start 创建新的实例
            }
        } 

        private async Task UiConsumeLoop(ChannelReader<CanFrame> reader, CancellationToken ct) // UI 消费循环：从 ChannelReader 批量读取并更新 UI
        { 
            while (!ct.IsCancellationRequested) // 持续运行直到取消请求
            { 
                var sb = new StringBuilder(); // 为本次批量构建字符串缓冲
                int count = 0; // 记录本次批量读取的数量

                // 尝试批量读取
                while (count < BatchMax && reader.TryRead(out var frame)) // 尝试尽可能多地从通道非阻塞读取
                {       
                    // 计算同一 ID 的时间差（单位：毫秒）
                    string deltaStr = "-";
                    if (lastTimestamps.TryGetValue(frame.Id, out var prevTs))
                    {
                        var deltaMs = frame.Timestamp - prevTs; // Timestamp 单位为 ms
                        deltaStr = $"{deltaMs} ms";
                    }
                    lastTimestamps[frame.Id] = frame.Timestamp;

                    // 每条消息一行：CanFrame.ToString() + 时间差
                    sb.AppendLine($"{frame.ToString()} Δ:{deltaStr}");
                    count++;
                } 

                // 如果没有立即获取到数据，等待新数据到达
                if (count == 0) // 如果本次没有读取到任何帧
                { 
                    if (!await reader.WaitToReadAsync(ct).ConfigureAwait(false)) break; // 等待新数据，若返回 false 表示通道已完成，则退出循环
                    continue; // 继续下一轮循环以读取刚到达的数据
                } 

                var text = sb.ToString(); // 将缓冲转换为字符串
                if (!string.IsNullOrEmpty(text)) // 如果有要写入 UI 的文本
                { 
                    if (!IsHandleCreated) continue; // 如果 UI 句柄未创建则跳过这次写入
                    try
                    { // 在 UI 线程中批量追加文本
                        // 在 UI 线程一次性追加批量文本，减少跨线程更新次数
                        BeginInvoke((Action)(() =>
                        {
                            RxtextBox1.AppendText(text); // 将批量文本追加到 RxtextBox1
                        })); 
                    }
                    catch (ObjectDisposedException) { break; } // 如果控件已被销毁则退出循环
                }  

                if (reader.Count == 0) // 如果通道当前没有剩余项
                { 
                    await Task.Delay(BatchWaitMs, ct).ConfigureAwait(false); // 等待短暂时间以批量累积数据
                } 
            } 
        } 
    } 
} 