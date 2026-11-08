using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TurboProbe
{
    internal static class Program
    {
        private static string _logPath;
        private static readonly object _lock = new object();
        private static HiddenWindow _window;
        private static IntPtr _hook = IntPtr.Zero;

        public static void W(string s)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s;
            lock (_lock)
            {
                Console.WriteLine(line);
                try { File.AppendAllText(_logPath, line + "\r\n", Encoding.UTF8); } catch { }
            }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            string logArg = null;
            for (int i = 0; i < args.Length; i++) { if (!args[i].StartsWith("--")) { logArg = args[i]; break; } }
            _logPath = logArg != null ? logArg : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "turbo-probe.log");
            try { File.WriteAllText(_logPath, "", Encoding.UTF8); } catch { }

            W("=== TURBO 键探测 ===");
            W("按下机身 TURBO 键；观察下面哪个通道有反应。结束后直接关窗口。");
            W("日志：" + _logPath);
            W("");

            _window = new HiddenWindow();
            _window.CreateHandle(new CreateParams { Caption = "TurboProbe", X = -4000, Y = -4000, Width = 1, Height = 1, Style = unchecked((int)0x80000000) });

            ListDevices();
            RegisterAllHid();

            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, HookProc, GetModuleHandle(null), 0);
            W("[键盘钩子] " + (_hook != IntPtr.Zero ? "已安装" : "安装失败"));
            W("");

            if (Array.IndexOf(args, "--nohid") < 0) StartHidReaders();

            if (Array.IndexOf(args, "--ec") >= 0)
            {
                Thread ec = new Thread(StartEcScan);
                ec.IsBackground = true;
                ec.Start();
            }

            if (Array.IndexOf(args, "--smu") >= 0)
            {
                Thread smu = new Thread(SmuLoop);
                smu.IsBackground = true;
                smu.Start();
            }

            Thread dbg = new Thread(delegate()
            {
                int last = -1;
                while (true)
                {
                    Thread.Sleep(5000);
                    int now = _rawCount;
                    if (now != last) { W("[统计] 5 秒内 WM_INPUT 累计 " + now + " 条"); last = now; }
                }
            });
            dbg.IsBackground = true;
            dbg.Start();

            Thread t = new Thread(XInputLoop);
            t.IsBackground = true;
            t.Start();

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        // ---------------------------------------------------------------- 设备枚举
        private static void ListDevices()
        {
            List<DeviceRef> devs = Enumerate();
            if (devs.Count == 0) { W("设备枚举失败"); return; }

            W("--- 输入设备列表 (" + devs.Count + ") ---");
            foreach (DeviceRef d in devs)
            {
                string name = DeviceName(d.Handle);
                if (d.Type == RIM_TYPEHID)
                {
                    RID_DEVICE_INFO info = GetInfo(d.Handle);
                    W(string.Format("HID  [{0}] page=0x{1:X4} usage=0x{2:X4} vid=0x{3:X4} pid=0x{4:X4}",
                        Short(name), info.hid.usUsagePage, info.hid.usUsage, info.hid.dwVendorId, info.hid.dwProductId));
                }
                else
                {
                    string kind = d.Type == 1 ? "KBD  " : (d.Type == 3 ? "OTHER" : "MOUSE");
                    W(kind + " [" + Short(name) + "]");
                }
            }
            W("");
        }

        private sealed class DeviceRef { public IntPtr Handle; public int Type; }

        private static List<DeviceRef> Enumerate()
        {
            List<DeviceRef> result = new List<DeviceRef>();
            uint count = 0;
            uint elem = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
            if (GetRawInputDeviceList(IntPtr.Zero, ref count, elem) != 0 || count == 0) return result;

            IntPtr buf = Marshal.AllocHGlobal((int)(count * elem));
            try
            {
                uint got = count;
                if (GetRawInputDeviceList(buf, ref got, elem) == uint.MaxValue) return result;
                for (int i = 0; i < got; i++)
                {
                    IntPtr el = (IntPtr)((long)buf + i * elem);
                    DeviceRef d = new DeviceRef();
                    d.Handle = Marshal.ReadIntPtr(el, 0);
                    d.Type = Marshal.ReadInt32(el, 8);
                    result.Add(d);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return result;
        }

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            int i = s.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
            if (i > 0) return s.Substring(i);
            return s.Length > 70 ? s.Substring(0, 70) : s;
        }

        private static void RegisterAllHid()
        {
            List<DeviceRef> devs = Enumerate();
            if (devs.Count == 0) return;

            HashSet<int> seen = new HashSet<int>();
            List<RAWINPUTDEVICE> reg = new List<RAWINPUTDEVICE>();

            for (int i = 0; i < devs.Count; i++)
            {
                if (devs[i].Type != RIM_TYPEHID) continue;
                RID_DEVICE_INFO info = GetInfo(devs[i].Handle);
                int page = info.hid.usUsagePage;
                int usage = info.hid.usUsage;
                if (page == 0 || usage == 0) continue;                                  // 无效集合
                if (page == 0x01 && (usage == 1 || usage == 2 || usage == 6)) continue; // 鼠标/键盘走钩子

                int key = (page << 16) | usage;
                if (!seen.Add(key)) continue;

                RAWINPUTDEVICE d = new RAWINPUTDEVICE();
                d.usUsagePage = (ushort)page;
                d.usUsage = (ushort)usage;
                d.dwFlags = RIDEV_INPUTSINK;
                d.hwndTarget = _window.Handle;
                reg.Add(d);
            }

            // 兜底：无论枚举结果如何，都注册消费类与手柄集合
            int[][] fallback = new int[][] { new int[] { 0x0C, 0x01 }, new int[] { 0x01, 0x04 }, new int[] { 0x01, 0x05 }, new int[] { 0x01, 0x06 } };
            foreach (int[] fb in fallback)
            {
                if (seen.Contains((fb[0] << 16) | fb[1])) continue;
                seen.Add((fb[0] << 16) | fb[1]);
                RAWINPUTDEVICE d2 = new RAWINPUTDEVICE();
                d2.usUsagePage = (ushort)fb[0];
                d2.usUsage = (ushort)fb[1];
                d2.dwFlags = RIDEV_INPUTSINK;
                d2.hwndTarget = _window.Handle;
                reg.Add(d2);
            }
            bool ok = RegisterRawInputDevices(reg.ToArray(), (uint)reg.Count, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            W("[RawInput] 注册 " + reg.Count + " 个集合：" + (ok ? "成功" : "失败"));
        }

        private static string DeviceName(IntPtr dev)
        {
            uint size = 0;
            const uint RIDI_DEVICENAME = 0x20000007;
            if (GetRawInputDeviceInfo(dev, RIDI_DEVICENAME, IntPtr.Zero, ref size) != 0) return "";
            if (size == 0) return "";
            IntPtr buf = Marshal.AllocHGlobal((int)size * 2 + 2);
            try
            {
                if (GetRawInputDeviceInfo(dev, RIDI_DEVICENAME, buf, ref size) == uint.MaxValue) return "";
                return Marshal.PtrToStringUni(buf);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private static RID_DEVICE_INFO GetInfo(IntPtr dev)
        {
            RID_DEVICE_INFO info = new RID_DEVICE_INFO();
            info.cbSize = Marshal.SizeOf(typeof(RID_DEVICE_INFO));
            uint size = (uint)info.cbSize;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                Marshal.StructureToPtr(info, buf, false);
                GetRawInputDeviceInfo(dev, 0x2000000B, buf, ref size);
                return (RID_DEVICE_INFO)Marshal.PtrToStructure(buf, typeof(RID_DEVICE_INFO));
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // ---------------------------------------------------------------- 原始输入
        private static int _rawCount;
        private static readonly HashSet<long> _unknown = new HashSet<long>();

        public static void OnRawInput(IntPtr lParam)
        {
            _rawCount++;
            uint size = 0;
            uint header = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            if (GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, header) != 0) return;
            if (size == 0 || size > 8192) return;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(lParam, RID_INPUT, buf, ref size, header) == uint.MaxValue) return;
                RAWINPUTHEADER hdr = (RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(RAWINPUTHEADER));
                if (hdr.dwType == 1)
                {
                    RAWKEYBOARD kb = (RAWKEYBOARD)Marshal.PtrToStructure((IntPtr)((long)buf + header), typeof(RAWKEYBOARD));
                    if (kb.VKey != 0) W(string.Format("[RAW-KBD] vk=0x{0:X2} scan=0x{1:X2}", kb.VKey, kb.MakeCode));
                    return;
                }
                if (hdr.dwType != RIM_TYPEHID) return;

                RAWHID hid = (RAWHID)Marshal.PtrToStructure((IntPtr)((long)buf + header), typeof(RAWHID));
                int total = hid.dwSizeHid * hid.dwCount;
                if (total <= 0 || total > 256) return;

                int show = Math.Min(total, 16);
                byte[] data = new byte[show];
                Marshal.Copy((IntPtr)((long)buf + header + Marshal.SizeOf(typeof(RAWHID))), data, 0, show);

                string hex = BitConverter.ToString(data);
                string dev = Short(DeviceName(hdr.hDevice));
                if (_lastDevice == dev && _lastHex == hex) return;
                _lastDevice = dev; _lastHex = hex;
                W("[HID] " + dev + "  " + hex);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private static string _lastDevice = "";
        private static string _lastHex = "";

        // ---------------------------------------------------------------- 键盘
        private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == 0x0100 || msg == 0x0104)
                {
                    KBDLLHOOKSTRUCT k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    W(string.Format("[KEY] vk=0x{0:X2} scan=0x{1:X2} ({2})", k.vkCode, k.scanCode, Name((int)k.vkCode)));
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static string Name(int vk)
        {
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            Keys k = (Keys)vk;
            string s = k.ToString();
            return s.Length > 16 ? ("VK " + vk) : s;
        }

        // ------------------------------------------------ EC 只读扫描
        private const ushort EC_DATA = 0x62;
        private const ushort EC_SC = 0x66;

        private static void StartEcScan()
        {
            string dll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinRing0x64.dll");
            if (!File.Exists(dll)) { W("[EC] 找不到 WinRing0x64.dll，跳过"); return; }
            IntPtr mod = LoadLibraryEx(dll, IntPtr.Zero, 8);
            if (mod == IntPtr.Zero) { W("[EC] 加载 WinRing0x64.dll 失败：" + Marshal.GetLastWin32Error()); return; }

            bool admin;
            try
            {
                System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent();
                admin = new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { admin = false; }
            if (!admin) { W("[EC] 需要管理员权限，已跳过 EC 扫描"); return; }

            W("[EC] 只读模式：不会写入任何 EC 寄存器");
            bool ok = InitializeOls();
            uint status = GetDllStatus();
            if (!ok || status != 0)
            {
                W("[EC] 驱动初始化失败（status=" + status + "），需要管理员权限");
                return;
            }

            // 先测 8 个寄存器的响应时间，判断 EC 是否被系统占用
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            int quickBad = 0;
            for (int i = 0; i < 8; i++) { if (EcRead((byte)i) < 0) quickBad++; }
            sw.Stop();
            W("[EC] 8 个寄存器耗时 " + sw.ElapsedMilliseconds + " ms，超时 " + quickBad + "/8");
            if (sw.ElapsedMilliseconds > 1500)
            {
                W("[EC] EC 响应过慢（通常是被 ACPI 驱动独占），放弃扫描");
                return;
            }

            byte[] prev = new byte[256];
            int bad = EcReadAll(prev);
            if (bad >= 256) { W("[EC] 256 个寄存器全部超时，该 EC 不响应标准 0x80 读命令"); return; }
            W("[EC] 基线已建立（超时 " + bad + "/256），开始监听变化");

            int failures = 0;
            while (true)
            {
                Thread.Sleep(200);
                byte[] cur = new byte[256];
                if (EcReadAll(cur) >= 256)
                {
                    failures++;
                    W("[EC] 扫描超时（" + failures + "/5）");
                    if (failures >= 5) { W("[EC] EC 无响应，已停止扫描（不影响其他通道）"); return; }
                    continue;
                }
                failures = 0;
                for (int i = 0; i < 256; i++)
                {
                    if (cur[i] != prev[i])
                        W(string.Format("[EC] 0x{0:X2}: 0x{1:X2} -> 0x{2:X2}", i, prev[i], cur[i]));
                }
                Array.Copy(cur, prev, 256);
            }
        }

        private static int EcReadAll(byte[] buf)
        {
            int bad = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                int v = EcRead((byte)i);
                if (v < 0) { bad++; buf[i] = 0xFE; }
                else buf[i] = (byte)v;
            }
            return bad;
        }

        private static int EcRead(byte addr)
        {
            if (!WaitIbf()) return -1;
            WriteIoPortByte(EC_SC, 0x80);
            if (!WaitIbf()) return -1;
            WriteIoPortByte(EC_DATA, addr);
            if (!WaitObf()) return -1;
            return ReadIoPortByte(EC_DATA);
        }

        private static bool WaitIbf()
        {
            for (int i = 0; i < 200; i++)
            {
                if ((ReadIoPortByte(EC_SC) & 0x02) == 0) return true;
                Thread.Sleep(0);
            }
            return false;
        }

        private static bool WaitObf()
        {
            for (int i = 0; i < 200; i++)
            {
                if ((ReadIoPortByte(EC_SC) & 0x01) != 0) return true;
                Thread.Sleep(0);
            }
            return false;
        }

        private static void StartHidReaders()
        {
            List<DeviceRef> devs = Enumerate();
            foreach (DeviceRef d in devs)
            {
                if (d.Type != RIM_TYPEHID) continue;
                RID_DEVICE_INFO info = GetInfo(d.Handle);
                int page = info.hid.usUsagePage;
                int usage = info.hid.usUsage;
                if (page == 0 || usage == 0) continue;
                if (page != 0x0C && page < 0xFF00) continue;

                string path = DeviceName(d.Handle);
                if (string.IsNullOrEmpty(path)) continue;

                IntPtr h = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == (IntPtr)(-1))
                {
                    W("[HID-READ] 打开失败 page=0x" + page.ToString("X4") + " err=" + Marshal.GetLastWin32Error());
                    continue;
                }

                string tag = string.Format("[HID-READ] page=0x{0:X4} usage=0x{1:X4}", page, usage);
                W(tag + " 已打开，开始读取");
                IntPtr handle = h;
                Thread th = new Thread(delegate()
                {
                    byte[] buf = new byte[128];
                    while (true)
                    {
                        uint read = 0;
                        bool ok = ReadFile(handle, buf, (uint)buf.Length, out read, IntPtr.Zero);
                        if (!ok) { W(tag + " 读取结束 err=" + Marshal.GetLastWin32Error()); return; }
                        if (read == 0) continue;
                        string hex = BitConverter.ToString(buf, 0, (int)Math.Min(read, 16));
                        W(tag + "  " + hex);
                    }
                });
                th.IsBackground = true;
                th.Start();
            }
        }

        private static void SmuLoop()
        {
            string dll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libryzenadj.dll");
            if (!File.Exists(dll)) { W("[SMU] 找不到 libryzenadj.dll，跳过限制采样"); return; }
            IntPtr mod = LoadLibraryEx(dll, IntPtr.Zero, 8);
            if (mod == IntPtr.Zero) { W("[SMU] 加载 dll 失败：" + Marshal.GetLastWin32Error()); return; }

            IntPtr ry = init_ryzenadj();
            if (ry == IntPtr.Zero) { W("[SMU] init 失败（需要管理员权限），跳过限制采样"); return; }

            W("[SMU] 已连接，每 500ms 采样一次功耗限制");
            float lastF = -1f, lastS = -1f;
            while (true)
            {
                try
                {
                    int rc = refresh_table(ry);
                    if (rc == 0)
                    {
                        float f = get_fast_limit(ry);
                        float s = get_slow_limit(ry);
                        if (f > 1000) f = f / 1000f;
                        if (s > 1000) s = s / 1000f;
                        if (Math.Abs(f - lastF) > 0.4f || Math.Abs(s - lastS) > 0.4f)
                        {
                            W(string.Format("[SMU] FAST={0:0.#}W  SLOW={1:0.#}W", f, s));
                            lastF = f; lastS = s;
                        }
                    }
                }
                catch { }
                Thread.Sleep(500);
            }
        }

        // ---------------------------------------------------------------- XInput
        private static void XInputLoop()
        {
            uint[] last = new uint[4];
            bool[] seen = new bool[4];
            while (true)
            {
                for (uint i = 0; i < 4; i++)
                {
                    XINPUT_STATE st;
                    uint rc = TryGetState(i, out st);
                    if (rc != 0) continue;
                    if (!seen[i]) { seen[i] = true; W("[XInput] 手柄 " + i + " 已连接"); }
                    if (st.Gamepad.wButtons != last[i])
                    {
                        last[i] = st.Gamepad.wButtons;
                        W(string.Format("[XInput] 手柄 {0} 按钮位 0x{1:X4}", i, st.Gamepad.wButtons));
                    }
                }
                Thread.Sleep(40);
            }
        }

        private static uint TryGetState(uint index, out XINPUT_STATE state)
        {
            try { return XInputGetState14(index, out state); }
            catch
            {
                try { return XInputGetState13(index, out state); }
                catch { state = new XINPUT_STATE(); return 1; }
            }
        }

        // ---------------------------------------------------------------- P/Invoke
        private const int WH_KEYBOARD_LL = 13;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const int RIM_TYPEHID = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICELIST { public IntPtr hDevice; public int dwType; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTHEADER { public int dwType; public int dwSize; public IntPtr hDevice; public IntPtr wParam; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWHID { public int dwSizeHid; public int dwCount; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWKEYBOARD { public ushort MakeCode; public ushort Flags; public ushort Reserved; public ushort VKey; public uint Message; public uint ExtraInformation; }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RID_DEVICE_INFO_HID { public int dwVendorId; public int dwProductId; public int dwVersionNumber; public ushort usUsagePage; public ushort usUsage; public int _pad1; public int _pad2; }

        [StructLayout(LayoutKind.Explicit)]
        public struct RID_DEVICE_INFO
        {
            [FieldOffset(0)] public int cbSize;
            [FieldOffset(4)] public int dwType;
            [FieldOffset(8)] public RID_DEVICE_INFO_HID hid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint num, uint size);

        [DllImport("user32.dll")]
        public static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

        [DllImport("user32.dll")]
        public static extern uint GetRawInputDeviceList(IntPtr list, ref uint count, uint size);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc proc, IntPtr module, uint thread);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG msg);

        [DllImport("WinRing0x64.dll")]
        private static extern bool InitializeOls();

        [DllImport("WinRing0x64.dll")]
        private static extern uint GetDllStatus();

        [DllImport("WinRing0x64.dll")]
        private static extern byte ReadIoPortByte(ushort port);

        [DllImport("WinRing0x64.dll")]
        private static extern void WriteIoPortByte(ushort port, byte value);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr file, byte[] buffer, uint count, out uint read, IntPtr overlapped);

        [DllImport("libryzenadj.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr init_ryzenadj();

        [DllImport("libryzenadj.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int refresh_table(IntPtr ry);

        [DllImport("libryzenadj.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern float get_fast_limit(IntPtr ry);

        [DllImport("libryzenadj.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern float get_slow_limit(IntPtr ry);

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState14(uint index, out XINPUT_STATE state);

        [DllImport("xinput1_3.dll")]
        private static extern uint XInputGetState13(uint index, out XINPUT_STATE state);
    }

    internal sealed class HiddenWindow : NativeWindow
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x00FF) Program.OnRawInput(m.LParam);
            base.WndProc(ref m);
        }
    }
}
