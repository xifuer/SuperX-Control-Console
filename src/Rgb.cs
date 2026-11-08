using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace RyzenTuner
{
    /// <summary>
    /// OneXPlayer GEN1 RGB 控制（VID_1A2C & PID_B001, usage page 0xFF01）。
    /// 协议来自 Linux 内核 hid-oxp 驱动：64 字节输出报告，FID 0x07 / 消息 0xFF，
    /// 命令 0xFD 写状态、0xFE 单色、0xFC 读状态；MCU 每条命令后需要约 200ms。
    /// </summary>
    internal static class RgbProtocol
    {
        public const int PacketSize = 65;      // 1 字节报告 ID + 64 字节数据
        public const byte ReportId = 0x00;
        public const byte MessageId = 0xFF;
        public const byte Fid = 0x07;

        public const byte CmdGet = 0xFC;
        public const byte CmdSet = 0xFD;
        public const byte EffectMono = 0xFE;

        public const int EffectAurora = 1;
        public const int EffectBirthday = 2;
        public const int EffectFlowing = 3;
        public const int EffectChromaPop = 4;
        public const int EffectNeon = 5;
        public const int EffectChromaBreathing = 6;
        public const int EffectDreamy = 7;
        public const int EffectWarm = 8;
        public const int EffectCyberpunk = 9;
        public const int EffectSea = 10;
        public const int EffectSunset = 11;
        public const int EffectColorful = 12;
        public const int EffectMonster = 13;
        public const int EffectGreen = 14;
        public const int EffectBlue = 15;
        public const int EffectYellow = 16;
        public const int EffectTeal = 17;
        public const int EffectPurple = 18;
        public const int EffectFoggy = 19;

        public static readonly string[] EffectNames = new string[]
        {
            "极光", "生日蛋糕", "流彩", "色彩弹跳", "霓虹", "彩色呼吸", "梦幻",
            "暖阳", "赛博朋克", "海浪", "日落余晖", "缤纷", "怪兽觉醒",
            "绿色呼吸", "蓝色呼吸", "黄色呼吸", "青色呼吸", "紫色呼吸", "迷雾"
        };

        public static string EffectName(int index)
        {
            if (index < 1 || index > EffectNames.Length) return "未知";
            return EffectNames[index - 1];
        }
    }

    internal sealed class RgbDevice : IDisposable
    {
        private IntPtr _handle = new IntPtr(-1);
        private IntPtr _readHandle = new IntPtr(-1);
        private readonly object _lock = new object();
        private DateTime _lastSend = DateTime.MinValue;

        public bool IsOpen { get { return _handle != new IntPtr(-1) && _handle != IntPtr.Zero; } }
        public string Path { get; private set; }
        public string LastError { get; private set; }

        // ---------------------------------------------------------------- 打开设备
        public static string FindPath(string vidPid, string mi)
        {
            try
            {
                uint count = 0;
                uint elem = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
                if (GetRawInputDeviceList(IntPtr.Zero, ref count, elem) != 0 || count == 0) return null;
                IntPtr buf = Marshal.AllocHGlobal((int)(count * elem));
                try
                {
                    uint got = count;
                    if (GetRawInputDeviceList(buf, ref got, elem) == uint.MaxValue) return null;
                    for (int i = 0; i < got; i++)
                    {
                        IntPtr el = (IntPtr)((long)buf + i * elem);
                        IntPtr dev = Marshal.ReadIntPtr(el, 0);
                        if (Marshal.ReadInt32(el, 8) != 2) continue;
                        string name = DeviceName(dev);
                        if (string.IsNullOrEmpty(name)) continue;
                        string up = name.ToUpperInvariant();
                        if (up.Contains(vidPid.ToUpperInvariant()) && up.Contains(mi.ToUpperInvariant()))
                            return name;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { }
            return null;
        }

        public bool OpenPath(string path)
        {
            Close();
            LastError = "";
            IntPtr h = CreateFile(path, 0x40000000 | 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h == new IntPtr(-1) || h == IntPtr.Zero)
            {
                LastError = "打开失败，Win32 " + Marshal.GetLastWin32Error();
                return false;
            }
            _handle = h;
            Path = path;
            return true;
        }

        /// <summary>发送已构造好的完整报告（长度必须等于设备输出报告长度）。</summary>
        public bool SendPacket(byte[] packet)
        {
            lock (_lock)
            {
                int wait = 220 - (int)(DateTime.Now - _lastSend).TotalMilliseconds;
                if (wait > 0) Thread.Sleep(wait);
                uint written = 0;
                bool ok = WriteFile(_handle, packet, (uint)packet.Length, out written, IntPtr.Zero);
                if (!ok)
                    ok = HidD_SetOutputReport(_handle, packet, (uint)packet.Length);
                _lastSend = DateTime.Now;
                if (!ok) LastError = "写入失败，Win32 " + Marshal.GetLastWin32Error();
                return ok;
            }
        }

        public bool Open()
        {
            Close();
            LastError = "";

            string path = FindDevicePath();
            if (path == null)
            {
                LastError = "没有找到 RGB 设备（VID_1A2C/PID_B001, page 0xFF01）";
                return false;
            }

            IntPtr h = CreateFile(path, 0x40000000 | 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h == new IntPtr(-1) || h == IntPtr.Zero)
            {
                LastError = "打开 RGB 设备失败，Win32 " + Marshal.GetLastWin32Error();
                return false;
            }
            _handle = h;

            IntPtr rh = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
            _readHandle = (rh == new IntPtr(-1)) ? IntPtr.Zero : rh;

            Path = path;
            return true;
        }

        public void Close()
        {
            if (IsOpen)
            {
                try { CloseHandle(_handle); } catch { }
            }
            if (_readHandle != IntPtr.Zero && _readHandle != new IntPtr(-1))
            {
                try { CloseHandle(_readHandle); } catch { }
            }
            _readHandle = IntPtr.Zero;
            _handle = new IntPtr(-1);
        }

        public void Dispose() { Close(); }

        // ---------------------------------------------------------------- 枚举路径
        private static string FindDevicePath()
        {
            try
            {
                uint count = 0;
                uint elem = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
                if (GetRawInputDeviceList(IntPtr.Zero, ref count, elem) != 0 || count == 0) return null;

                IntPtr buf = Marshal.AllocHGlobal((int)(count * elem));
                try
                {
                    uint got = count;
                    if (GetRawInputDeviceList(buf, ref got, elem) == uint.MaxValue) return null;
                    for (int i = 0; i < got; i++)
                    {
                        IntPtr el = (IntPtr)((long)buf + i * elem);
                        IntPtr dev = Marshal.ReadIntPtr(el, 0);
                        int type = Marshal.ReadInt32(el, 8);
                        if (type != 2) continue;

                        string name = DeviceName(dev);
                        if (string.IsNullOrEmpty(name)) continue;
                        string up = name.ToUpperInvariant();
                        if (up.Contains("VID_1A2C&PID_B001") && up.Contains("MI_00"))
                            return name;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { }
            return null;
        }

        private static string DeviceName(IntPtr dev)
        {
            uint size = 0;
            const uint RIDI_DEVICENAME = 0x20000007;
            if (GetRawInputDeviceInfo(dev, RIDI_DEVICENAME, IntPtr.Zero, ref size) != 0) return null;
            if (size == 0) return null;
            IntPtr buf = Marshal.AllocHGlobal((int)size * 2 + 2);
            try
            {
                if (GetRawInputDeviceInfo(dev, RIDI_DEVICENAME, buf, ref size) == uint.MaxValue) return null;
                return Marshal.PtrToStringUni(buf);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // ---------------------------------------------------------------- 发送
        private bool Send(byte[] payload)
        {
            if (!IsOpen) return false;

            lock (_lock)
            {
                // MCU 每条命令后需要约 200ms
                int wait = 220 - (int)(DateTime.Now - _lastSend).TotalMilliseconds;
                if (wait > 0) Thread.Sleep(wait);

                byte[] buf = new byte[RgbProtocol.PacketSize];
                buf[0] = RgbProtocol.ReportId;   // 0x00
                buf[1] = RgbProtocol.Fid;        // 0x07
                buf[2] = RgbProtocol.MessageId;  // 0xFF
                int n = Math.Min(payload.Length, buf.Length - 3);
                Array.Copy(payload, 0, buf, 3, n);

                // 厂商实测：64 字节，走中断 OUT 端点（WriteFile）
                uint written = 0;
                bool ok = WriteFile(_handle, buf, (uint)buf.Length, out written, IntPtr.Zero);
                if (!ok || written != buf.Length)
                {
                    int e1 = Marshal.GetLastWin32Error();
                    LastError = "写入失败：WriteFile=" + e1 + " (写 " + written + ")";
                    return false;
                }
                LastError = "";
                _lastSend = DateTime.Now;
                try { TrayApp.Log("RGB >> " + BitConverter.ToString(buf, 0, 10) + " ... len=" + buf.Length + " ok=" + ok); } catch { }
                return ok;
            }
        }

        public bool SetStatus(bool enabled, int speed, int brightness)
        {
            byte[] p = new byte[4];
            p[0] = RgbProtocol.CmdSet;
            p[1] = (byte)(enabled ? 1 : 0);
            p[2] = (byte)Math.Max(0, Math.Min(9, speed));
            p[3] = (byte)Math.Max(0, Math.Min(3, brightness));
            return Send(p);
        }

        public bool SetEffect(int effect)
        {
            byte[] p = new byte[1];
            p[0] = (byte)effect;
            return Send(p);
        }

        public bool SetMonoColor(byte r, byte g, byte b)
        {
            // 载荷：0xFE + 18 组 RGB = 55 字节
            byte[] p = new byte[55];
            p[0] = RgbProtocol.EffectMono;
            for (int i = 0; i < 18; i++)
            {
                p[3 * i + 1] = r;
                p[3 * i + 2] = g;
                p[3 * i + 3] = b;
            }
            return Send(p);
        }

        /// <summary>读取当前状态（发送 0xFC 并解析回复）。失败返回 null。</summary>
        public RgbState GetStatus()
        {
            if (!IsOpen) return null;
            bool sent = Send(new byte[] { RgbProtocol.CmdGet });
            if (!sent) return null;

            Thread.Sleep(60);
            byte[] reply = ReadReply(600);
            if (reply == null) return null;
            return RgbState.Parse(reply);
        }

        private byte[] ReadReply(int timeoutMs)
        {
            byte[] buf = new byte[RgbProtocol.PacketSize];
            IntPtr ev = CreateEvent(IntPtr.Zero, true, false, null);
            if (ev == IntPtr.Zero) return null;

            NativeOverlapped ov = new NativeOverlapped();
            ov.EventHandle = ev;
            try
            {
                uint read = 0;
                if (_readHandle == IntPtr.Zero) return null;
                if (!ReadFile(_readHandle, buf, (uint)buf.Length, out read, ref ov))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != 997) return null; // ERROR_IO_PENDING
                    if (WaitForSingleObject(ev, timeoutMs) != 0) return null;
                    if (!GetOverlappedResult(_readHandle, ref ov, out read, false)) return null;
                }
                if (read < 11) return null;
                return buf;
            }
            catch { return null; }
            finally { CloseHandle(ev); }
        }

        // ---------------------------------------------------------------- P/Invoke
        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICELIST { public IntPtr hDevice; public int dwType; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeOverlapped
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr EventHandle;
        }

        [DllImport("user32.dll")]
        private static extern uint GetRawInputDeviceList(IntPtr list, ref uint count, uint size);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetOutputReport(IntPtr h, byte[] buffer, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr h, byte[] buf, uint count, out uint written, IntPtr ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr h, byte[] buf, uint count, out uint read, ref NativeOverlapped ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(IntPtr h, ref NativeOverlapped ov, out uint transferred, bool wait);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr sec, bool manual, bool initial, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr h, int ms);
    }

    internal sealed class RgbState
    {
        public int Effect;
        public bool Enabled;
        public int Speed;
        public int Brightness;
        public int Red;
        public int Green;
        public int Blue;
        public byte[] Raw;

        public static RgbState Parse(byte[] buf)
        {
            // 报告布局（含报告 ID）：[0]=ID [1]=0x0F [2..3] 保留
            // [4]=effect [5]=enabled [6]=speed [7]=brightness [8..10]=rgb
            if (buf == null || buf.Length < 11) return null;
            if (buf[1] != 0x0F) return null;
            RgbState s = new RgbState();
            s.Raw = buf;
            s.Effect = buf[4];
            s.Enabled = buf[5] != 0;
            s.Speed = buf[6];
            s.Brightness = buf[7];
            s.Red = buf[8];
            s.Green = buf[9];
            s.Blue = buf[10];
            return s;
        }

        public override string ToString()
        {
            return string.Format("effect={0} enabled={1} speed={2} bright={3} rgb=({4},{5},{6})",
                Effect, Enabled, Speed, Brightness, Red, Green, Blue);
        }
    }
}
