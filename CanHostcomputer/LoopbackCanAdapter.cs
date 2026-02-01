using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CanHostcomputer
{
    // 简单回环适配器：把发送的帧回写到接收通道，便于测试 UI 和上层逻辑
    public class LoopbackCanAdapter : ICanAdapter, IDisposable
    {
        private Channel<CanFrame> channel;
        private CancellationTokenSource? cts;
        private bool disposed;
        private readonly Action<string>? logger;

        public LoopbackCanAdapter(int capacity = 2000, Action<string>? logger = null)
        {
            this.logger = logger;
            channel = Channel.CreateBounded<CanFrame>(new BoundedChannelOptions(capacity)
            {
                SingleReader = false,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest
            });
        }

        public ChannelReader<CanFrame> Frames => channel.Reader;

        public Task StartAsync(CancellationToken ct)
        {
            if (disposed) throw new ObjectDisposedException(nameof(LoopbackCanAdapter));
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            logger?.Invoke("LoopbackCanAdapter: started");
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            cts?.Cancel();
            try { channel.Writer.TryComplete(); } catch { }
            logger?.Invoke("LoopbackCanAdapter: stopped");
            return Task.CompletedTask;
        }

        public ValueTask<bool> SendAsync(CanFrame frame, CancellationToken ct)
        {
            if (disposed) return new ValueTask<bool>(false);
            // echo the frame back to reader
            var copy = new CanFrame
            {
                Id = frame.Id,
                Data = frame.Data != null ? (byte[])frame.Data.Clone() : Array.Empty<byte>(),
                Dlc = frame.Dlc,
                Flags = frame.Flags,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            var ok = channel.Writer.TryWrite(copy);
            if (!ok) logger?.Invoke("LoopbackCanAdapter: drop on send");
            return new ValueTask<bool>(ok);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { cts?.Cancel(); } catch { }
            try { channel.Writer.TryComplete(); } catch { }
            cts?.Dispose();
        }

        // 新增：返回伪造的驱动信息，供 UI 显示
        public Task<DriverInfo> GetDriverInfoAsync()
        {
            var info = new DriverInfo
            {
                VersionRaw = 0,
                VersionMajor = 0,
                VersionMinor = 0,
                NumberOfChannels = 1
            };
            return Task.FromResult(info);
        }
    }
}
