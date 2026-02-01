using System;
using System.Text;

namespace CanHostcomputer
{
    public sealed class CanFrame
    {
        public int Id { get; init; } // 报文ID
        public byte[] Data { get; init; } = Array.Empty<byte>(); // 数据字节数组
        public int Dlc { get; init; } // 数据长度
        public int Flags { get; init; } //扩展帧、远程帧
        public long Timestamp { get; init; } // 时间戳

        // 将数据按两位16进制（大写）空格分隔，只输出 dlc 字节 （显示用）
        public string DataHex()
        {
            var len = Math.Clamp(Dlc, 0, Math.Min(8, Data?.Length ?? 0));
            if (len == 0) return string.Empty;
            var sb = new StringBuilder(len * 3);
            for (int i = 0; i < len; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(Data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        // 单行格式：timestamp ID:0x### DLC:# data...
        public override string ToString()
        {
            return $"{Timestamp} ID:0x{Id:X3} DLC:{Dlc} {DataHex()} Time:{Timestamp}";
        }
    }
}