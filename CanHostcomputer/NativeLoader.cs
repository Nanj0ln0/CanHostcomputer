using System;
using System.Runtime.InteropServices;  // 加载NativeLibrary类

namespace CanHostcomputer
{
    /*
     * 本地DLL加载器，提取DLL中的函数指针
     * 外部不可继承
     */
    internal sealed class NativeLoader : IDisposable   
    {
        // 用于保存NativeLibrary.Load返回的模块句柄(表示已加载的DLL)，句柄指针
        private readonly IntPtr module;

        //构造函数，传入DLL路径并加载·
        public NativeLoader(string libraryPath)
        {
            // 路径检测
            if (string.IsNullOrEmpty(libraryPath)) 
                  throw new ArgumentNullException(nameof(libraryPath));
            module = NativeLibrary.Load(libraryPath);
        }

        // 获取指定导出函数的委托
        public TDelegate GetFunction<TDelegate>(string exportName) where TDelegate : Delegate
        {
            // 获取导出的符号的地址，并返回一个指示方法调用是否成功的值 
            if (!NativeLibrary.TryGetExport(module, exportName, out var ptr) || ptr == IntPtr.Zero)
                throw new EntryPointNotFoundException($"{exportName} not found in module");
            // 给出函数的指针，并转换为委托类型返回
            return Marshal.GetDelegateForFunctionPointer<TDelegate>(ptr);
        }

        //释放已加载的DLL模块
        public void Dispose()
        {
            try { NativeLibrary.Free(module); } catch { }
        }
    }
}