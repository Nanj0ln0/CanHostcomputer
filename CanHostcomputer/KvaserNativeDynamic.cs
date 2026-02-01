using System;
using System.Runtime.InteropServices;

namespace CanHostcomputer
{
    // 注意：这里默认使用 CallingConvention.Cdecl；若目标库使用不同调用约定，请调整 UnmanagedFunctionPointer 特性。
    internal sealed class KvaserNativeDynamic : IDisposable
    {
        private readonly NativeLoader loader;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void canInitializeLibrary_delegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int canGetVersionEx_delegate(int arg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int canGetNumberOfChannels_delegate(out int noc);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int canOpenChannel_delegate(int channel, int flags);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int canSetBusParams_delegate(int handle, int bitrate, int tseg1, int tseg2, int sjw, int noSamp);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int canBusOn_delegate(int handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int canBusOff_delegate(int handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int canClose_delegate(int handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int canWrite_delegate(int handle, int id, byte[] data, int dlc, int flags);

        // canReadWait: 使用 IntPtr buffer，然后复制回托管数组
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int canReadWait_delegate(int handle, out int id, IntPtr data, out int dlc, out int flags, out long timestamp, int timeout);

        private readonly canInitializeLibrary_delegate native_canInitializeLibrary;
        private readonly canGetVersionEx_delegate native_canGetVersionEx;
        private readonly canGetNumberOfChannels_delegate native_canGetNumberOfChannels;
        private readonly canOpenChannel_delegate native_canOpenChannel;
        private readonly canSetBusParams_delegate native_canSetBusParams;
        private readonly canBusOn_delegate native_canBusOn;
        private readonly canBusOff_delegate native_canBusOff;
        private readonly canClose_delegate native_canClose;
        private readonly canWrite_delegate native_canWrite;
        private readonly canReadWait_delegate native_canReadWait;

        // 构造函数，加载指定路径的本地库
        public KvaserNativeDynamic(string libraryPath)
        {
            loader = new NativeLoader(libraryPath);
            native_canInitializeLibrary = loader.GetFunction<canInitializeLibrary_delegate>("canInitializeLibrary");
            native_canGetVersionEx = loader.GetFunction<canGetVersionEx_delegate>("canGetVersionEx");
            native_canGetNumberOfChannels = loader.GetFunction<canGetNumberOfChannels_delegate>("canGetNumberOfChannels");
            native_canOpenChannel = loader.GetFunction<canOpenChannel_delegate>("canOpenChannel");
            native_canSetBusParams = loader.GetFunction<canSetBusParams_delegate>("canSetBusParams");
            native_canBusOn = loader.GetFunction<canBusOn_delegate>("canBusOn");
            native_canBusOff = loader.GetFunction<canBusOff_delegate>("canBusOff");
            native_canClose = loader.GetFunction<canClose_delegate>("canClose");
            native_canWrite = loader.GetFunction<canWrite_delegate>("canWrite");
            native_canReadWait = loader.GetFunction<canReadWait_delegate>("canReadWait");
        }

        public void canInitializeLibrary() => native_canInitializeLibrary();

        public int canGetVersionEx(int v) => native_canGetVersionEx(v);

        public int canGetNumberOfChannels(out int noc) => native_canGetNumberOfChannels(out noc);

        public int canOpenChannel(int channel, int flags) => native_canOpenChannel(channel, flags);

        public int canSetBusParams(int handle, int bitrate, int tseg1, int tseg2, int sjw, int noSamp) =>
            native_canSetBusParams(handle, bitrate, tseg1, tseg2, sjw, noSamp);

        public int canBusOn(int handle) => native_canBusOn(handle);

        public int canBusOff(int handle) => native_canBusOff(handle);

        public int canClose(int handle) => native_canClose(handle);

        public int canWrite(int handle, int id, byte[] data, int dlc, int flags) => native_canWrite(handle, id, data, dlc, flags);

        public int canReadWait(int handle, out int id, byte[] data, out int dlc, out int flags, out long timestamp, int timeout)
        {
            var buf = Marshal.AllocHGlobal(8);
            try
            {
                int res = native_canReadWait(handle, out id, buf, out dlc, out flags, out timestamp, timeout);
                if (res == 0) // assume 0 means canOK in native; 上层会比较返回值
                {
                    Marshal.Copy(buf, data, 0, 8);
                }
                else
                {
                    // 若无消息或错误，则 data 未改写
                }
                return res;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        public void Dispose()
        {
            try { loader.Dispose(); } catch { }
        }
    }
}