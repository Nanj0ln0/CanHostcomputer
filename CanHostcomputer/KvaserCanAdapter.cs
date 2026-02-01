using Kvaser.CanLib;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace CanHostcomputer
{   
    /*
    *负责和 Kvaser CAN 驱动交互（支持静态 P/Invoke 的 Canlib 或运行时动态加载的原生 DLL）。
    *在后台创建一个阻塞读取循环（ReaderLoop），将收到的报文写入有界 Channel，供上层通过 ChannelReader 非阻塞读取。
    *支持 StartAsync / StopAsync / SendAsync，并实现 IDisposable 以便在 Dispose 时停止并释放资源。
    *设计目标：
    *将底层驱动与上层 UI/业务解耦（通过 ICanAdapter 接口）。
    *支持运行时按路径加载原生 DLL（可通过 KvaserNativeDynamic）或回退到静态的 Canlib。
    */
    public class KvaserCanAdapter : ICanAdapter, IDisposable
    {
        // Channel：用于在后台读取任务（写）和 UI 消费任务（读）之间传递 CanFrame。
        // 使用有界通道以限制内存、并在满时丢弃最旧项
        private Channel<CanFrame>? channel;

        // CancellationTokenSource：用于取消 readerTask（以及 StartAsync/StopAsync 协调）
        private CancellationTokenSource? cts;

        // 后台读取任务引用（ReaderLoop）
        private Task? readerTask;

        // running 标志：指示 readerLoop 是否在运行（使用 volatile 以避免线程缓存问题）
        private volatile bool running;

        // 可选日志回调：外部可传入一个 Action<string>，通常用于将日志封送回 UI 线程显示
        private readonly Action<string>? logger;

        // 要打开的通道索引（CAN 通道号）
        private readonly int chanIndex;

        // 打开的通道句柄（-1 表示尚未打开）
        private int chanhandle = -1;

        // Channel 容量
        private readonly int capacity;

        // 可选的本地 DLL 路径（用于 KvaserNativeDynamic）
        private readonly string? nativeLibraryPath;

        // 动态加载的 native 封装（如果存在）
        private readonly KvaserNativeDynamic? native;

        // 新增：保存所选择的波特率（使用 Canlib 常量）
        private readonly int bitrate;

        // public 属性，暴露当前句柄与运行状态
        public int ChannelHandle => chanhandle;
        public bool IsRunning => running;

        // Channel 的 Reader，由 ICanAdapter 接口暴露
        public ChannelReader<CanFrame> Frames => (channel ?? throw new InvalidOperationException("Adapter not initialized")).Reader;

        /*
        * 构造函数
        * channelIndex: 目标 CAN 端口索引
        * capacity: Channel 容量
        * logger: 日志回调
        * nativeLibraryPath: 可选本地 DLL 路径
        * bitrate: 波特率（使用 Canlib 定义的常量，如 Canlib.canBITRATE_250K / canBITRATE_500K）
        */
        public KvaserCanAdapter(int channelIndex = 0, int capacity = 2000, Action<string>? logger = null, string? nativeLibraryPath = null, int bitrate = Canlib.canBITRATE_500K)
        {
            chanIndex = channelIndex;
            this.capacity = capacity;
            this.logger = logger;
            this.nativeLibraryPath = nativeLibraryPath;
            this.bitrate = bitrate; // 保存选择的波特率

            // 如果指定了本地库路径，尝试构造动态绑定封装（加载失败时记录并回退到静态 Canlib）
            if (!string.IsNullOrEmpty(nativeLibraryPath))
            {
                try
                {
                    native = new KvaserNativeDynamic(nativeLibraryPath);
                    logger?.Invoke($"KvaserCanAdapter: using native library {nativeLibraryPath}");
                }
                catch (Exception ex)
                {
                    logger?.Invoke($"KvaserCanAdapter: failed to load native library {nativeLibraryPath}: {ex.Message}");
                    native = null;
                }
            }

            EnsureChannel();
        }

        /// <summary>
        /// EnsureChannel
        /// - 在未初始化或前次 channel 已完成时重建一个新的有界通道实例。
        /// - 这样可以安全地在 Stop 后再次 Start。
        /// </summary>
        private void EnsureChannel()
        {
            // 如果 channel 为 null 或已被完成，重建一个新的有界通道
            if (channel == null || channel.Reader.Completion.IsCompleted)
            {
                channel = Channel.CreateBounded<CanFrame>(new BoundedChannelOptions(capacity)
                {
                    SingleWriter = true,   // 只有 readerLoop 写入
                    SingleReader = false,  // UI 可能并发读取
                    FullMode = BoundedChannelFullMode.DropOldest // 队满时丢弃最早的帧
                });
            }
        }

        /// <summary>
        /// StartAsync
        /// - 启动底层驱动（初始化库、打开通道、设置波特率并上总线）并启动后台的 ReaderLoop。
        /// - externalCt 用于外部取消（例如 UI 取消、Stop 请求等），内部创建 linked CTS 以便集中控制。
        /// - 对动态与静态路径均做支持：优先使用 native（动态），否则使用静态 Canlib。
        /// </summary>
        public async Task StartAsync(CancellationToken externalCt)
        {
            // 如果已有 readerTask 正在运行，则不重复启动
            if (readerTask != null && !readerTask.IsCompleted) return;

            // 每次 Start 都创建一个新的 channel 实例，避免写入到旧的已完成 channel
            channel = Channel.CreateBounded<CanFrame>(new BoundedChannelOptions(capacity)
            {
                SingleWriter = true,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

            // 创建 linked CTS，外部取消会传递到内部任务
            cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            var ct = cts.Token;

            // 初始化并打开通道（如果尚未打开）
            // 打开并设置波特率，使用构造时保存的 this.bitrate（而非硬编码）
            if (native != null)
            {
                // 使用动态加载的本地封装
                native.canInitializeLibrary();
                if (chanhandle < 0)
                {
                    chanhandle = native.canOpenChannel(chanIndex, Canlib.canOPEN_ACCEPT_VIRTUAL);
                    logger?.Invoke($"KvaserCanAdapter(native): canOpenChannel returned {chanhandle}");
                    var setRes = native.canSetBusParams(chanhandle, this.bitrate, 0, 0, 0, 0);
                    logger?.Invoke($"KvaserCanAdapter(native): canSetBusParams returned {setRes}");
                    var onRes = native.canBusOn(chanhandle);
                    logger?.Invoke($"KvaserCanAdapter(native): canBusOn returned {onRes}");
                    // small delay to allow driver to settle
                    try { Task.Delay(200, ct).Wait(ct); } catch { }
                }
            }
            else
            {
                // 回退到静态 Canlib（原有实现）
                Canlib.canInitializeLibrary();
                if (chanhandle < 0)
                {
                    chanhandle = Canlib.canOpenChannel(chanIndex, Canlib.canOPEN_ACCEPT_VIRTUAL);
                    logger?.Invoke($"KvaserCanAdapter: canOpenChannel returned {chanhandle}");
                    var setRes = Canlib.canSetBusParams(chanhandle, this.bitrate, 0, 0, 0, 0);
                    logger?.Invoke($"KvaserCanAdapter: canSetBusParams returned {setRes}");
                    var onRes = Canlib.canBusOn(chanhandle);
                    logger?.Invoke($"KvaserCanAdapter: canBusOn returned {onRes}");
                    try { Task.Delay(200, ct).Wait(ct); } catch { }
                }
            }

            // 启动后台读取循环（在线程池）
            // 启动 reader loop
            readerTask = Task.Run(() => ReaderLoop(ct), ct);

            // 等待 reader loop 指示已运行（短超时）
            try
            {
                await WaitForRunningAsync(500).ConfigureAwait(false);
            }
            catch
            {
                // ignore; reader may still start shortly
            }
            logger?.Invoke("KvaserCanAdapter: readerTask started");
            await Task.CompletedTask;
        }

        /// <summary>
        /// StopAsync
        /// - 取消 reader 循环并关闭通道、释放句柄
        /// </summary>
        public async Task StopAsync()
        {
            try
            {
                // 请求取消后台任务
                cts?.Cancel();
                if (readerTask != null)
                {
                    // 等待 readerTask 在最多 2s 内退出
                    var finished = await Task.WhenAny(readerTask, Task.Delay(2000)).ConfigureAwait(false);
                    if (finished != readerTask)
                    {
                        logger?.Invoke("KvaserCanAdapter: readerTask did not exit within timeout");
                    }
                    readerTask = null;
                }
            }
            finally
            {
                // 关闭底层通道句柄（保持与现有 Form1 行为一致）
                if (chanhandle >= 0)
                {
                    try
                    {
                        if (native != null)
                        {
                            var offRes = native.canBusOff(chanhandle);
                            logger?.Invoke($"KvaserCanAdapter(native): canBusOff returned {offRes}");
                            var closeRes = native.canClose(chanhandle);
                            logger?.Invoke($"KvaserCanAdapter(native): canClose returned {closeRes}");
                        }
                        else
                        {
                            var offRes = Canlib.canBusOff(chanhandle);
                            logger?.Invoke($"KvaserCanAdapter: canBusOff returned {offRes}");
                            var closeRes = Canlib.canClose(chanhandle);
                            logger?.Invoke($"KvaserCanAdapter: canClose returned {closeRes}");
                        }
                    }
                    catch { /* 忽略底层关闭错误以保证 Stop 完成 */ }
                    chanhandle = -1;
                }

                // small delay to let driver finish closing
                try { Task.Delay(50).Wait(); } catch { }

                cts?.Dispose();
                cts = null;
            }
        }

        /// <summary>
        /// SendAsync
        /// - 将 CanFrame 转换为驱动需要的 8 字节缓冲并调用本地 canWrite。
        /// - 返回 bool 表示驱动是否接受（res >= 0 为成功）。
        /// - 该方法为 ValueTask&lt;bool&gt;，以便在常见同步完成路径时避免分配开销。
        /// 注意：对复杂的异步驱动写入，可能需要改写为真正的异步实现。
        /// </summary>
        public ValueTask<bool> SendAsync(CanFrame frame, CancellationToken ct)
        {
            if (chanhandle < 0) return new ValueTask<bool>(false);

            // 按 Kvaser API 要求准备 8 字节数据缓冲（清零并复制实际数据）
            var data = new byte[8];
            Array.Clear(data, 0, data.Length);
            if (frame.Data != null) Array.Copy(frame.Data, data, Math.Min(frame.Data.Length, 8));

            int res;
            if (native != null)
            {
                res = native.canWrite(chanhandle, frame.Id, data, frame.Dlc, frame.Flags);
                logger?.Invoke($"KvaserCanAdapter(native): canWrite returned {res}");
            }
            else
            {
                res = (int)Canlib.canWrite(chanhandle, frame.Id, data, frame.Dlc, frame.Flags);
                logger?.Invoke($"KvaserCanAdapter: canWrite returned {res}");
            }
            return new ValueTask<bool>(res >= 0);
        }

        /// <summary>
        /// ReaderLoop
        /// - 持续调用 canReadWait（带 timeout），在成功时构造 CanFrame 并 TryWrite 到 channel 的 writer。
        /// - 采用非阻塞写入（TryWrite），当写入失败（通道满或已关闭）会记录并丢弃帧，避免阻塞底层读取线程。
        /// - 在异常或取消时会退出并 TryComplete writer。
        /// 注意：底层 canReadWait 的返回值语义依赖于具体驱动；这里与 Canlib.canStatus 进行了对比（整型比较）。
        /// </summary>
        private void ReaderLoop(CancellationToken ct)
        {
            var buffer = new byte[8];
            var writer = channel?.Writer;
            try
            {
                running = true;

                // 等待 writer 可用的一小段时间（Start/Stop 切换时的防护）
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (writer == null && sw.ElapsedMilliseconds < 200)
                {
                    Thread.Sleep(5);
                    writer = channel?.Writer;
                }

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        int id, dlc, flags;
                        long timestamp;

                        int status;
                        if (native != null)
                        {
                            // 动态本地实现返回 int 状态
                            status = native.canReadWait(chanhandle, out id, buffer, out dlc, out flags, out timestamp, 100);
                        }
                        else
                        {
                            // 静态 Canlib 返回 canStatus 枚举，转换为 int 进行比较
                            status = (int)Canlib.canReadWait(chanhandle, out id, buffer, out dlc, out flags, out timestamp, 100);
                        }

                        if (status == (int)Canlib.canStatus.canOK)
                        {
                            // 成功读取到帧：复制数据并写入 channel（非阻塞）
                            var dataCopy = new byte[8];
                            Array.Copy(buffer, dataCopy, 8);

                            var frame = new CanFrame
                            {
                                Id = id,
                                Data = dataCopy,
                                Dlc = dlc,
                                Flags = flags,
                                Timestamp = timestamp
                            };
                            var wrote = writer?.TryWrite(frame) ?? false;
                            if (!wrote) logger?.Invoke("KvaserCanAdapter: dropped frame - writer full or closed");
                        }
                        else if (status == (int)Canlib.canStatus.canERR_NOMSG)
                        {
                            // 没有消息，立即继续循环（短超时即可避免 busy spin）
                            continue;
                        }
                        else
                        {
                            // 其它错误：记录并短暂等待
                            logger?.Invoke($"canReadWait error: {status}");
                            Task.Delay(50, ct).Wait(ct);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        // 记录异常并短暂退避
                        logger?.Invoke("ReaderLoop exception: " + ex);
                        try { Task.Delay(100, ct).Wait(ct); } catch { break; }
                    }
                }
            }
            finally
            {
                // 确保 writer 被 TryComplete，以通知所有消费者通道结束
                try
                {
                    writer?.TryComplete();
                }
                catch { /* ignore */ }
                running = false;
                logger?.Invoke("KvaserCanAdapter: readerTask exiting");
            }
        }

        /// <summary>
        /// Dispose
        /// - 同步地停止适配器并释放 native（如果有）。
        /// - 注意：Dispose 会同步等待 StopAsync 完成（使用 GetAwaiter().GetResult()），确保资源在返回前已释放。
        /// </summary>
        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
            try { native?.Dispose(); } catch { }
        }

        /// <summary>
        /// WaitForReadyAsync
        /// - 等待 reader loop 启动并且底层句柄打开（chanhandle >= 0），带超时。
        /// - 返回 bool 表示是否在 timeout 内达到就绪状态。
        /// </summary>
        public async Task<bool> WaitForReadyAsync(int timeoutMs = 1000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (running && chanhandle >= 0) return true;
                await Task.Delay(10).ConfigureAwait(false);
            }
            return running && chanhandle >= 0;
        }

        /// <summary>
        /// WaitForRunningAsync
        /// - 非异步阻塞等待 reader loop 设置 running 标志（用于短等待）
        /// </summary>
        private Task WaitForRunningAsync(int timeoutMs)
        {
            return Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!running && sw.ElapsedMilliseconds < timeoutMs)
                {
                    Thread.Sleep(5);
                }
            });
        }

        /// <summary>
        /// GetDriverInfoAsync
        /// - 返回 DriverInfo（版本原始值、主/次版本、通道数），优先使用动态加载的 native，否则使用静态 Canlib。
        /// - 该方法是异步的，以便实现可以在内部执行可能的 I/O 或延迟初始化。
        /// </summary>
        public async Task<DriverInfo> GetDriverInfoAsync()
        {
            try
            {
                int raw = 0;
                int channels = 0;

                if (native != null)
                {
                    // 使用动态加载路径
                    raw = native.canGetVersionEx(Canlib.canVERSION_CANLIB32_PRODVER32);
                    native.canGetNumberOfChannels(out channels);
                }
                else
                {
                    // 回退到静态 Canlib
                    raw = Canlib.canGetVersionEx(Canlib.canVERSION_CANLIB32_PRODVER32);
                    Canlib.canGetNumberOfChannels(out channels);
                }

                var major = (raw & 0xFF0000) >> 16;
                var minor = (raw & 0xFF00) >> 8;

                var info = new DriverInfo
                {
                    VersionRaw = raw,
                    VersionMajor = major,
                    VersionMinor = minor,
                    NumberOfChannels = channels
                };
                return await Task.FromResult(info);
            }
            catch (Exception ex)
            {
                logger?.Invoke("GetDriverInfoAsync error: " + ex.Message);
                return new DriverInfo { VersionRaw = 0, VersionMajor = 0, VersionMinor = 0, NumberOfChannels = 0 };
            }
        }
    }
}