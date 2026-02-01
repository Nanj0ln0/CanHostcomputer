using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

/*
 设计思路：
     把底层 CAN 驱动与上层（UI / 业务）彻底解耦，
     提供统一的启动/停止、发送、读取报文，便于替换。
 */

namespace CanHostcomputer
{
    public interface ICanAdapter
    {
        ChannelReader<CanFrame> Frames { get; } // CAN 帧的通道读取器（只读）
        Task StartAsync(CancellationToken ct); // 启动后台读取任务
        Task StopAsync();   // 停止后台读取任务
        ValueTask<bool> SendAsync(CanFrame frame, CancellationToken ct); // 发送 CAN 帧，返回是否成功

        // 新增：获取底层驱动的一些信息（版本、通道数等），异步以便适配器内部可做 I/O 或延迟初始化
        Task<DriverInfo> GetDriverInfoAsync();
    }
}