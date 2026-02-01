using System;
using Kvaser.CanLib;

namespace CanHostcomputer
{
    public static class CanAdapterFactory
    {
        /// <summary>
        /// Create an ICanAdapter by name. Known names:
        ///  - "kvaser" (default): KvaserCanAdapter
        ///  - "loopback": LoopbackCanAdapter (test/demo)
        /// </summary>
        public static ICanAdapter Create(string? name, int channelIndex = 0, int capacity = 2000, Action<string>? logger = null, int bitrate = Canlib.canBITRATE_500K)
        {
            var key = (name ?? "kvaser").Trim().ToLowerInvariant();

            // 可选：通过环境变量传入 native DLL 路径，例如 CAN_DLL_PATH=C:\path\canlib.dll
            var nativePath = Environment.GetEnvironmentVariable("CAN_DLL_PATH"); // 这个CAN_DLL_PATH路径设置在哪？
            return key switch
            {
                "loopback" => new LoopbackCanAdapter(capacity: capacity, logger: logger),
                _ => new KvaserCanAdapter(channelIndex: channelIndex, capacity: capacity, logger: logger, nativeLibraryPath: nativePath, bitrate: bitrate),
            };
        }
    }
}
