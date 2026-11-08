using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RyzenTuner
{
    // ------------------------------------------------------------------ 原生互操作
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved1;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetDllDirectory(string path);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string name);

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_SYSKEYDOWN = 0x0104;
    }

    internal static class Rz
    {
        private const string Dll = "libryzenadj.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr init_ryzenadj();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int refresh_table(IntPtr ry);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int set_stapm_limit(IntPtr ry, uint value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int set_fast_limit(IntPtr ry, uint value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int set_slow_limit(IntPtr ry, uint value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int set_tctl_temp(IntPtr ry, uint value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int set_power_saving(IntPtr ry);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int set_max_performance(IntPtr ry);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern float get_fast_limit(IntPtr ry);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern float get_slow_limit(IntPtr ry);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern float get_stapm_limit(IntPtr ry);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern float get_tctl_temp(IntPtr ry);
    }

    // ------------------------------------------------------------------ 配置
    internal sealed class Settings
    {
        public bool AutoSwitch { get; set; }
        public string Mode { get; set; }          // auto | fixed | manual
        public string FixedSource { get; set; }   // battery | typec | dcin
        public int ManualW { get; set; }
        public string SourceOverride { get; set; } // auto | battery | typec | dcin
        public string AcSource { get; set; }       // typec | dcin
        public string ThemeName { get; set; }      // onex | midnight | light | custom
        public int ThemeBg { get; set; }
        public int ThemeCard { get; set; }
        public int ThemeAccent { get; set; }
        public int ThemeText { get; set; }
        public int ThemeDim { get; set; }
        public string DllDir { get; set; }         // 运行库目录，空=exe 同目录

        public int BatteryFast { get; set; }
        public int BatterySlow { get; set; }
        public int BatteryTctl { get; set; }
        public bool BatteryPs { get; set; }
        public bool BatteryMp { get; set; }

        public int TypecFast { get; set; }
        public int TypecSlow { get; set; }
        public int TypecTctl { get; set; }
        public bool TypecPs { get; set; }
        public bool TypecMp { get; set; }

        public int DcinFast { get; set; }
        public int DcinSlow { get; set; }
        public int DcinTctl { get; set; }
        public bool DcinPs { get; set; }
        public bool DcinMp { get; set; }

        public string TurboKind { get; set; }     // none | key | hid
        public int TurboVk { get; set; }
        public int TurboScan { get; set; }
        public int TurboMods { get; set; }
        public int TurboPage { get; set; }
        public int TurboUsage { get; set; }
        public int TurboPadMask { get; set; }
        public string TurboName { get; set; }

        public bool RgbEnabled { get; set; }
        public string RgbMode { get; set; }
        public int RgbR { get; set; }
        public int RgbG { get; set; }
        public int RgbB { get; set; }
        public int RgbSpeed { get; set; }
        public int RgbBrightness { get; set; }
        public int RgbEffect { get; set; }

        public bool CloseToTray { get; set; }
        public bool StartMinimized { get; set; }

        public Settings()
        {
            AutoSwitch = true;
            Mode = "auto";
            FixedSource = "dcin";
            ManualW = 45;
            SourceOverride = "auto";
            AcSource = "dcin";
            ThemeName = "onex";
            ThemeBg = 0x0E0E10;
            ThemeCard = 0x17171A;
            ThemeAccent = 0xE23B2E;
            ThemeText = 0xF2F2F2;
            ThemeDim = 0x9A9A9A;
            DllDir = "";

            BatteryFast = 25; BatterySlow = 20; BatteryTctl = 90; BatteryPs = true; BatteryMp = false;
            TypecFast = 65; TypecSlow = 50; TypecTctl = 95; TypecPs = false; TypecMp = false;
            DcinFast = 85; DcinSlow = 65; DcinTctl = 95; DcinPs = false; DcinMp = true;

            TurboKind = "key";
            TurboVk = 164;       // VK_LMENU
            TurboScan = 56;      // Alt 扫描码
            TurboMods = 9;       // Ctrl + Win
            TurboName = "Ctrl+Win+LMenu";
            TurboPage = 0; TurboUsage = 0; TurboPadMask = 0;
            RgbEnabled = false;
            RgbMode = "static";
            RgbR = 255; RgbG = 0; RgbB = 0;
            RgbSpeed = 5; RgbBrightness = 4; RgbEffect = 3;

            CloseToTray = true;
            StartMinimized = false;
        }

        public void SetProfile(string name, int fast, int slow, int tctl, bool ps, bool mp)
        {
            if (name == "battery") { BatteryFast = fast; BatterySlow = slow; BatteryTctl = tctl; BatteryPs = ps; BatteryMp = mp; }
            else if (name == "typec") { TypecFast = fast; TypecSlow = slow; TypecTctl = tctl; TypecPs = ps; TypecMp = mp; }
            else { DcinFast = fast; DcinSlow = slow; DcinTctl = tctl; DcinPs = ps; DcinMp = mp; }
        }

        public int FastOf(string name)
        {
            if (name == "battery") return BatteryFast;
            if (name == "typec") return TypecFast;
            return DcinFast;
        }
    }

    internal static class Store
    {
        public static string Dir = Path.GetDirectoryName(Application.ExecutablePath);
        public static string PathOverride = null;
        public static string FilePath
        {
            get { return PathOverride != null ? PathOverride : Path.Combine(Dir, "settings.ini"); }
        }

        public static Settings Load()
        {
            var s = new Settings();
            if (!File.Exists(FilePath)) return s;
            try
            {
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    Apply(s, k, v);
                }
            }
            catch { }
            Migrate(s);
            return s;
        }

        private static void Migrate(Settings s)
        {
            // 旧版本默认值被清零，这里恢复 TURBO 默认绑定（用户显式「清除」时 TurboKind 为 none，不会命中）
            if (string.IsNullOrEmpty(s.TurboKind)) s.TurboKind = "key";
            if (s.TurboKind == "key" && s.TurboVk == 0 && s.TurboScan == 0)
            {
                s.TurboVk = 164;     // VK_LMENU
                s.TurboScan = 56;    // Alt 扫描码
                s.TurboMods = 9;     // Ctrl + Win
                s.TurboName = "Ctrl+Win+LMenu";
            }
        }

        private static void Apply(Settings s, string k, string v)
        {
            bool bv; int iv;
            if (!int.TryParse(v, out iv)) iv = 0;
            if (!bool.TryParse(v, out bv)) bv = false;

            switch (k)
            {
                case "AutoSwitch": s.AutoSwitch = bv; break;
                case "Mode": s.Mode = v; break;
                case "FixedSource": s.FixedSource = v; break;
                case "ManualW": s.ManualW = iv; break;
                case "SourceOverride": s.SourceOverride = v; break;
                case "AcSource": s.AcSource = v; break;
                case "ThemeName": s.ThemeName = v; break;
                case "ThemeBg": s.ThemeBg = iv; break;
                case "ThemeCard": s.ThemeCard = iv; break;
                case "ThemeAccent": s.ThemeAccent = iv; break;
                case "ThemeText": s.ThemeText = iv; break;
                case "ThemeDim": s.ThemeDim = iv; break;
                case "DllDir": s.DllDir = v; break;
                case "BatteryFast": s.BatteryFast = iv; break;
                case "BatterySlow": s.BatterySlow = iv; break;
                case "BatteryTctl": s.BatteryTctl = iv; break;
                case "BatteryPs": s.BatteryPs = bv; break;
                case "BatteryMp": s.BatteryMp = bv; break;
                case "TypecFast": s.TypecFast = iv; break;
                case "TypecSlow": s.TypecSlow = iv; break;
                case "TypecTctl": s.TypecTctl = iv; break;
                case "TypecPs": s.TypecPs = bv; break;
                case "TypecMp": s.TypecMp = bv; break;
                case "DcinFast": s.DcinFast = iv; break;
                case "DcinSlow": s.DcinSlow = iv; break;
                case "DcinTctl": s.DcinTctl = iv; break;
                case "DcinPs": s.DcinPs = bv; break;
                case "DcinMp": s.DcinMp = bv; break;
                case "TurboKind": s.TurboKind = v; break;
                case "TurboVk": s.TurboVk = iv; break;
                case "TurboScan": s.TurboScan = iv; break;
                case "TurboMods": s.TurboMods = iv; break;
                case "TurboPage": s.TurboPage = iv; break;
                case "TurboUsage": s.TurboUsage = iv; break;
                case "TurboPadMask": s.TurboPadMask = iv; break;
                case "TurboName": s.TurboName = v; break;
                case "RgbEnabled": s.RgbEnabled = bv; break;
                case "RgbMode": s.RgbMode = v; break;
                case "RgbR": s.RgbR = iv; break;
                case "RgbG": s.RgbG = iv; break;
                case "RgbB": s.RgbB = iv; break;
                case "RgbSpeed": s.RgbSpeed = iv; break;
                case "RgbBrightness": s.RgbBrightness = iv; break;
                case "RgbEffect": s.RgbEffect = iv; break;
                case "CloseToTray": s.CloseToTray = bv; break;
                case "StartMinimized": s.StartMinimized = bv; break;
            }
        }

        public static void Save(Settings s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# RyzenTuner 配置（可直接编辑，程序退出时写回）");
            sb.AppendLine("AutoSwitch=" + s.AutoSwitch);
            sb.AppendLine("Mode=" + s.Mode);
            sb.AppendLine("FixedSource=" + s.FixedSource);
            sb.AppendLine("ManualW=" + s.ManualW);
            sb.AppendLine("SourceOverride=" + s.SourceOverride);
            sb.AppendLine("AcSource=" + s.AcSource);
            sb.AppendLine("ThemeName=" + s.ThemeName);
            sb.AppendLine("ThemeBg=" + s.ThemeBg);
            sb.AppendLine("ThemeCard=" + s.ThemeCard);
            sb.AppendLine("ThemeAccent=" + s.ThemeAccent);
            sb.AppendLine("ThemeText=" + s.ThemeText);
            sb.AppendLine("ThemeDim=" + s.ThemeDim);
            sb.AppendLine("DllDir=" + s.DllDir);
            sb.AppendLine("BatteryFast=" + s.BatteryFast);
            sb.AppendLine("BatterySlow=" + s.BatterySlow);
            sb.AppendLine("BatteryTctl=" + s.BatteryTctl);
            sb.AppendLine("BatteryPs=" + s.BatteryPs);
            sb.AppendLine("BatteryMp=" + s.BatteryMp);
            sb.AppendLine("TypecFast=" + s.TypecFast);
            sb.AppendLine("TypecSlow=" + s.TypecSlow);
            sb.AppendLine("TypecTctl=" + s.TypecTctl);
            sb.AppendLine("TypecPs=" + s.TypecPs);
            sb.AppendLine("TypecMp=" + s.TypecMp);
            sb.AppendLine("DcinFast=" + s.DcinFast);
            sb.AppendLine("DcinSlow=" + s.DcinSlow);
            sb.AppendLine("DcinTctl=" + s.DcinTctl);
            sb.AppendLine("DcinPs=" + s.DcinPs);
            sb.AppendLine("DcinMp=" + s.DcinMp);
            sb.AppendLine("TurboKind=" + s.TurboKind);
            sb.AppendLine("TurboVk=" + s.TurboVk);
            sb.AppendLine("TurboScan=" + s.TurboScan);
            sb.AppendLine("TurboMods=" + s.TurboMods);
            sb.AppendLine("TurboPage=" + s.TurboPage);
            sb.AppendLine("TurboUsage=" + s.TurboUsage);
            sb.AppendLine("TurboPadMask=" + s.TurboPadMask);
            sb.AppendLine("TurboName=" + s.TurboName);
            sb.AppendLine("RgbEnabled=" + s.RgbEnabled);
            sb.AppendLine("RgbMode=" + s.RgbMode);
            sb.AppendLine("RgbR=" + s.RgbR);
            sb.AppendLine("RgbG=" + s.RgbG);
            sb.AppendLine("RgbB=" + s.RgbB);
            sb.AppendLine("RgbSpeed=" + s.RgbSpeed);
            sb.AppendLine("RgbBrightness=" + s.RgbBrightness);
            sb.AppendLine("RgbEffect=" + s.RgbEffect);
            sb.AppendLine("CloseToTray=" + s.CloseToTray);
            sb.AppendLine("StartMinimized=" + s.StartMinimized);
            try { File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8); } catch { }
        }
    }

    // ------------------------------------------------------------------ 引擎
    internal sealed class Engine
    {
        private IntPtr _ry = IntPtr.Zero;
        public bool Ready { get; private set; }
        public string Error { get; private set; }
        public string LastAction { get; private set; }

        public Engine()
        {
            Ready = false;
            Error = "";
            LastAction = "";
        }

        public void Init(string dir)
        {
            Ready = false;
            Error = "";
            if (string.IsNullOrEmpty(dir)) dir = Store.Dir;

            string dllPath = Path.Combine(dir, "libryzenadj.dll");
            if (!File.Exists(dllPath))
            {
                Error = "未找到 libryzenadj.dll。请把它和 WinRing0x64.sys 放到：" + dir;
                return;
            }

            try { Native.SetDllDirectory(dir); } catch { }
            try
            {
                string oldPath = Environment.GetEnvironmentVariable("PATH");
                Environment.SetEnvironmentVariable("PATH", dir + ";" + (oldPath == null ? "" : oldPath));
            }
            catch { }

            // 显式按完整路径预加载：避免依赖 PATH / 当前目录的搜索顺序
            IntPtr mod = Native.LoadLibraryEx(dllPath, IntPtr.Zero, 0x00000008);
            if (mod == IntPtr.Zero)
            {
                int code = Marshal.GetLastWin32Error();
                Error = string.Format("无法加载 libryzenadj.dll（Win32 {0}：{1}）\r\n路径：{2}", code, LoadErrorText(code), dllPath);
                return;
            }

            IntPtr ry;
            try
            {
                ry = Rz.init_ryzenadj();
            }
            catch (Exception ex)
            {
                Error = "libryzenadj 初始化调用失败：" + ex.Message;
                return;
            }
            if (ry == IntPtr.Zero)
            {
                Error = "初始化失败：需要管理员权限，或 WinRing0 驱动未就位（驱动应在 " + dir + "）";
                return;
            }
            _ry = ry;
            Ready = true;
            LastAction = "";
        }

        private static string LoadErrorText(int code)
        {
            switch (code)
            {
                case 126: return "找不到依赖模块，通常是缺少 VC++ 运行库，或被安全软件拦截";
                case 127: return "找不到指定的过程";
                case 193: return "不是有效的 Win32 程序（位数不匹配）";
                case 5: return "拒绝访问，可能被杀毒软件拦截";
                case 2: return "找不到文件";
                default: return "系统错误码";
            }
        }

        public bool Apply(int fastW, int slowW, int tctl, bool ps, bool mp)
        {
            if (!Ready) { LastAction = Error; return false; }
            try
            {
                int r1 = Rz.set_stapm_limit(_ry, (uint)(slowW * 1000));
                int r2 = Rz.set_fast_limit(_ry, (uint)(fastW * 1000));
                int r3 = Rz.set_slow_limit(_ry, (uint)(slowW * 1000));
                int r4 = Rz.set_tctl_temp(_ry, (uint)tctl);
                int r5 = 0;
                if (ps) r5 = Rz.set_power_saving(_ry);
                else if (mp) r5 = Rz.set_max_performance(_ry);
                int bad = 0;
                if (r1 != 0) bad = r1;
                if (r2 != 0) bad = r2;
                if (r3 != 0) bad = r3;
                if (r4 != 0) bad = r4;
                if (r5 != 0) bad = r5;
                if (bad != 0)
                {
                    LastAction = "应用失败，SMU 返回 " + bad + "（" + CodeText(bad) + "）";
                    return false;
                }
                LastAction = "已应用 " + fastW + "W / " + slowW + "W / " + tctl + "℃";
                return true;
            }
            catch (Exception ex)
            {
                LastAction = "应用异常：" + ex.Message;
                return false;
            }
        }

        public static string CodeText(int code)
        {
            switch (code)
            {
                case -1: return "平台不支持";
                case -2: return "参数不支持";
                case -3: return "SMU 不支持";
                case -4: return "SMU 拒绝";
                case -5: return "表读取失败";
                default: return "错误码";
            }
        }

        public string ReadLimits()
        {
            if (!Ready) return "";
            try
            {
                int rc = Rz.refresh_table(_ry);
                if (rc != 0) return "读取不可用（" + CodeText(rc) + "）";
                float f = Rz.get_fast_limit(_ry);
                float s = Rz.get_slow_limit(_ry);
                float t = Rz.get_tctl_temp(_ry);
                if (f > 1000) f = f / 1000f;
                if (s > 1000) s = s / 1000f;
                return string.Format("FAST {0:0.#}W · SLOW {1:0.#}W · 温度墙 {2:0}℃", f, s, t);
            }
            catch (Exception ex)
            {
                return "读取异常：" + ex.Message;
            }
        }

        public static bool OnAc
        {
            get
            {
                Native.SYSTEM_POWER_STATUS s;
                if (!Native.GetSystemPowerStatus(out s)) return false;
                return s.ACLineStatus == 1;
            }
        }

        public static int BatteryPercent
        {
            get
            {
                Native.SYSTEM_POWER_STATUS s;
                if (!Native.GetSystemPowerStatus(out s)) return -1;
                if (s.BatteryLifePercent == 255) return -1;
                return s.BatteryLifePercent;
            }
        }
    }

    // ------------------------------------------------------------------ EC 供电识别
    internal static class PowerSource
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string path);
        [DllImport("inpoutx64.dll")]
        private static extern bool IsInpOutDriverOpen();
        [DllImport("inpoutx64.dll")]
        private static extern byte Inp32(int port);
        [DllImport("inpoutx64.dll")]
        private static extern void Out32(int port, int value);

        private const int PortData = 0x62;
        private const int PortStatus = 0x66;

        private static int _driverState = -1;   // -1 未检测, 0 不可用, 1 可用
        private static string _cached;
        private static DateTime _cacheTime = DateTime.MinValue;

        private static bool EnsureDriver()
        {
            if (_driverState >= 0) return _driverState == 1;
            try
            {
                SetDllDirectory(System.IO.Path.GetDirectoryName(Application.ExecutablePath));
                _driverState = IsInpOutDriverOpen() ? 1 : 0;
            }
            catch { _driverState = 0; }
            return _driverState == 1;
        }

        private static bool WaitClear(byte mask, int ms)
        {
            for (int i = 0; i < ms * 20; i++)
            {
                if ((Inp32(PortStatus) & mask) == 0) return true;
                System.Threading.Thread.Sleep(0);
            }
            return false;
        }

        private static bool WaitSet(byte mask, int ms)
        {
            for (int i = 0; i < ms * 20; i++)
            {
                if ((Inp32(PortStatus) & mask) != 0) return true;
                System.Threading.Thread.Sleep(0);
            }
            return false;
        }

        private static int ReadRegister(byte addr)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (!WaitClear(0x02, 30)) continue;
                Out32(PortStatus, 0x80);
                if (!WaitClear(0x02, 30)) continue;
                Out32(PortData, addr);
                if (!WaitSet(0x01, 30)) continue;
                return Inp32(PortData);
            }
            return -1;
        }

        /// <summary>返回 battery / typec / dcin；无法判断时返回 null。</summary>
        public static string Detect()
        {
            if ((DateTime.Now - _cacheTime).TotalSeconds < 2) return _cached;
            _cacheTime = DateTime.Now;
            _cached = null;
            try
            {
                if (!EnsureDriver()) return null;
                int v = ReadRegister(0xFE);
                if (v < 0) return null;
                if (v == 1) _cached = "battery";
                else if (v == 3) _cached = "typec";
                else if (v == 5) _cached = "dcin";
            }
            catch { _cached = null; }
            return _cached;
        }

        public static string Describe()
        {
            if (!EnsureDriver()) return "EC 驱动不可用";
            int v = ReadRegister(0xFE);
            return v < 0 ? "EC 读取超时" : ("EC 0xFE = 0x" + v.ToString("X2"));
        }
    }

    // ------------------------------------------------------------------ 全局按键
    internal sealed class KeyHook : IDisposable
    {
        private Native.LowLevelKeyboardProc _proc;
        private IntPtr _hook = IntPtr.Zero;
        public bool Learning { get; set; }
        public static int CurrentMods;
        public event Action<int, int, string> KeyPressed; // vk, scan, name

        public void Start()
        {
            _proc = HookCallback;
            IntPtr hMod = Native.GetModuleHandle(null);
            _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, hMod, 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                {
                    Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    CurrentMods = Mods();
                    Action<int, int, string> h = KeyPressed;
                    if (h != null)
                    {
                        try { h((int)k.vkCode, (int)k.scanCode, KeyName((int)k.vkCode)); }
                        catch { }
                    }
                }
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static int Mods()
        {
            int m = 0;
            if ((GetAsyncKeyState(0x11) & 0x8000) != 0) m |= 1;
            if ((GetAsyncKeyState(0x12) & 0x8000) != 0) m |= 2;
            if ((GetAsyncKeyState(0x10) & 0x8000) != 0) m |= 4;
            if (((GetAsyncKeyState(0x5B) | GetAsyncKeyState(0x5C)) & 0x8000) != 0) m |= 8;
            return m;
        }

        /// <summary>计算修饰键状态，但排除按键本身（例如单独按 Ctrl 时 mods 应为 0）。</summary>
        public static int ModsFor(int vk)
        {
            int m = CurrentMods;
            if (vk == 0x11) m &= ~1;
            else if (vk == 0x12) m &= ~2;
            else if (vk == 0x10) m &= ~4;
            else if (vk == 0x5B || vk == 0x5C) m &= ~8;
            return m;
        }

        public static string ModsName(int mods)
        {
            string s = "";
            if ((mods & 1) != 0) s += "Ctrl+";
            if ((mods & 2) != 0) s += "Alt+";
            if ((mods & 4) != 0) s += "Shift+";
            if ((mods & 8) != 0) s += "Win+";
            return s;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vk);

        public static string KeyName(int vk)
        {
            Keys k = (Keys)vk;
            try
            {
                if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
                if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
                if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
                string s = k.ToString();
                if (s.Length <= 12 && !s.StartsWith("Oem")) return s;
            }
            catch { }
            return "VK 0x" + vk.ToString("X2");
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    // ------------------------------------------------------------------ 自启
    internal static class Autostart
    {
        public const string TaskName = "RyzenTuner";

        public static bool IsEnabled()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", "/query /tn " + TaskName);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(5000);
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        public static void Enable(string exePath)
        {
            string user = Environment.UserDomainName + "\\" + Environment.UserName;
            string xml = string.Format(
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                "<Task version=\"1.3\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Description>RyzenTuner 登录自启</Description></RegistrationInfo>" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{0}</UserId><Delay>PT20S</Delay></LogonTrigger></Triggers>" +
                "<Principals><Principal id=\"Author\"><UserId>{0}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" +
                "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
                "<AllowHardTerminate>true</AllowHardTerminate>" +
                "<StartWhenAvailable>false</StartWhenAvailable>" +
                "<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>" +
                "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled>" +
                "<Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun>" +
                "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>{1}</Command><Arguments>--tray</Arguments></Exec></Actions></Task>",
                EscapeXml(user), EscapeXml(exePath));

            string tmp = Path.Combine(Path.GetTempPath(), "ryzentuner-task.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try
            {
                RunSchTasks("/create /tn " + TaskName + " /xml \"" + tmp + "\" /f");
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        public static void Disable()
        {
            RunSchTasks("/delete /tn " + TaskName + " /f");
        }

        private static void RunSchTasks(string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(10000);
            }
        }

        private static string EscapeXml(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
