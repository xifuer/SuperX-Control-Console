using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RyzenTuner
{
    // ------------------------------------------------------------------ 圆角卡片
    internal sealed class Card : Panel
    {
        public static bool CheapPaint;   // 动画期间用纯色填充，避免每帧重绘背景图
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 先铺父窗口背景图（若有），再叠半透明色
            Form f = FindForm();
            if (!CheapPaint && f != null && f.BackgroundImage != null)
            {
                try
                {
                    Point origin = f.PointToClient(PointToScreen(Point.Empty));
                    Graphics g = e.Graphics;
                    System.Drawing.Drawing2D.GraphicsState st = g.Save();
                    g.TranslateTransform(-origin.X, -origin.Y);
                    g.DrawImage(f.BackgroundImage, new Rectangle(0, 0, f.ClientSize.Width, f.ClientSize.Height));
                    g.Restore(st);
                }
                catch { }
            }
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(Theme.CardAlpha, Theme.Card)))
                e.Graphics.FillRectangle(sb, ClientRectangle);
            using (Pen p = new Pen(Theme.Border, 1f))
            {
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Round(r, 10))
                    e.Graphics.DrawPath(p, path);
            }
        }

        private static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath gp = new GraphicsPath();
            int d = radius * 2;
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }
    }

    internal static class Theme
    {
        public static Color Bg;
        public static Color Card;
        public static Color Border;
        public static Color Text;
        public static Color Dim;
        public static Color Accent;
        public static Color Green;
        public static Color Yellow;
        public static Color Red;
        public static Color Btn;
        public static string Name = "onex";
        public static int CardAlpha = 255;   // 面板不透明度 0..255

        public static readonly string[] PresetNames = new string[] { "壹号本红", "午夜蓝", "极简白" };

        static Theme() { Apply("onex"); }

        public static Color Mix(Color a, Color b, double k)
        {
            k = Math.Max(0, Math.Min(1, k));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * k),
                (int)(a.G + (b.G - a.G) * k),
                (int)(a.B + (b.B - a.B) * k));
        }

        // ---------------- 自定义主题存储 ----------------
        private static string ThemeFile { get { return Path.Combine(Store.Dir, "themes.ini"); } }

        public static string[] CustomNameList()
        {
            try
            {
                if (!File.Exists(ThemeFile)) return new string[0];
                List<string> names = new List<string>();
                foreach (string l in File.ReadAllLines(ThemeFile, Encoding.UTF8))
                {
                    int i = l.IndexOf('|');
                    if (i > 0) names.Add(l.Substring(0, i));
                }
                return names.ToArray();
            }
            catch { return new string[0]; }
        }

        public static bool LoadCustom(string name, out Color bg, out Color card, out Color accent, out Color text, out Color dim, out int opacity)
        {
            bg = card = accent = text = dim = Color.Black;
            opacity = 100;
            try
            {
                if (!File.Exists(ThemeFile)) return false;
                foreach (string l in File.ReadAllLines(ThemeFile, Encoding.UTF8))
                {
                    if (!l.StartsWith(name + "|", StringComparison.OrdinalIgnoreCase)) continue;
                    string[] p = l.Split('|');
                    if (p.Length < 7) return false;
                    bg = ParseHex(p[1]); card = ParseHex(p[2]); accent = ParseHex(p[3]);
                    text = ParseHex(p[4]); dim = ParseHex(p[5]);
                    int op; if (int.TryParse(p[6], out op)) opacity = op;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static Color ParseHex(string s)
        {
            try
            {
                if (s.Length != 6) return Color.Black;
                return Color.FromArgb(Convert.ToInt32(s.Substring(0, 2), 16), Convert.ToInt32(s.Substring(2, 2), 16), Convert.ToInt32(s.Substring(4, 2), 16));
            }
            catch { return Color.Black; }
        }

        public static string CustomNames()
        {
            try
            {
                if (!File.Exists(ThemeFile)) return "（无）";
                List<string> names = new List<string>();
                foreach (string l in File.ReadAllLines(ThemeFile, Encoding.UTF8))
                {
                    int i = l.IndexOf('|');
                    if (i > 0) names.Add(l.Substring(0, i));
                }
                return names.Count == 0 ? "（无）" : string.Join("、", names.ToArray());
            }
            catch { return "（无）"; }
        }

        public static void SaveCustom(string name, Color bg, Color card, Color accent, Color text, Color dim, int opacity)
        {
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                List<string> lines = new List<string>();
                if (File.Exists(ThemeFile)) lines.AddRange(File.ReadAllLines(ThemeFile, Encoding.UTF8));
                string entry = name + "|" + Hex(bg) + "|" + Hex(card) + "|" + Hex(accent) + "|" + Hex(text) + "|" + Hex(dim) + "|" + opacity;
                bool replaced = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith(name + "|", StringComparison.OrdinalIgnoreCase)) { lines[i] = entry; replaced = true; break; }
                }
                if (!replaced) lines.Add(entry);
                File.WriteAllLines(ThemeFile, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        public static void DeleteCustom(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                if (!File.Exists(ThemeFile)) return;
                List<string> keep = new List<string>();
                foreach (string l in File.ReadAllLines(ThemeFile, Encoding.UTF8))
                    if (!l.StartsWith(name + "|", StringComparison.OrdinalIgnoreCase)) keep.Add(l);
                File.WriteAllLines(ThemeFile, keep.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        private static string Hex(Color c) { return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        public static void Apply(string name)
        {
            Name = name ?? "onex";
            switch (Name)
            {
                case "midnight":
                    Bg = Color.FromArgb(10, 14, 26);
                    Card = Color.FromArgb(17, 23, 38);
                    Border = Color.FromArgb(34, 44, 66);
                    Text = Color.FromArgb(226, 234, 245);
                    Dim = Color.FromArgb(130, 146, 170);
                    Accent = Color.FromArgb(64, 156, 255);
                    Btn = Color.FromArgb(24, 33, 52);
                    break;
                case "light":
                    Bg = Color.FromArgb(244, 245, 247);
                    Card = Color.FromArgb(255, 255, 255);
                    Border = Color.FromArgb(220, 224, 230);
                    Text = Color.FromArgb(26, 30, 36);
                    Dim = Color.FromArgb(110, 118, 129);
                    Accent = Color.FromArgb(214, 48, 40);
                    Btn = Color.FromArgb(238, 240, 243);
                    break;
                case "custom":
                    return; // 颜色由 ApplyCustom 设置
                default:
                    Bg = Color.FromArgb(14, 14, 16);
                    Card = Color.FromArgb(23, 23, 26);
                    Border = Color.FromArgb(42, 42, 46);
                    Text = Color.FromArgb(242, 242, 242);
                    Dim = Color.FromArgb(154, 154, 154);
                    Accent = Color.FromArgb(226, 59, 46);
                    Btn = Color.FromArgb(32, 32, 36);
                    break;
            }
            Green = Color.FromArgb(63, 185, 80);
            Yellow = Color.FromArgb(210, 153, 34);
            Red = Color.FromArgb(248, 81, 73);
        }

        public static void ApplyCustom(Color bg, Color card, Color accent, Color text, Color dim)
        {
            Name = "custom";
            Bg = bg; Card = card; Accent = accent; Text = text; Dim = dim;
            Border = Mix(card, text, 0.22);
            Btn = Mix(card, text, 0.10);
            Green = Color.FromArgb(63, 185, 80);
            Yellow = Color.FromArgb(210, 153, 34);
            Red = Color.FromArgb(248, 81, 73);
        }

        public static void LoadFrom(Settings s)
        {
            CardAlpha = (int)Math.Round(255.0 * Math.Max(0, Math.Min(100, s.CardOpacity <= 0 ? 100 : s.CardOpacity)) / 100.0);
            if (s.ThemeName == "custom")
                ApplyCustom(ColorFromInt(s.ThemeBg), ColorFromInt(s.ThemeCard), ColorFromInt(s.ThemeAccent), ColorFromInt(s.ThemeText), ColorFromInt(s.ThemeDim));
            else
                Apply(s.ThemeName);
        }

        public static Color ColorFromInt(int v)
        {
            return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        }

        public static int ColorToInt(Color c)
        {
            return (c.R << 16) | (c.G << 8) | c.B;
        }
    }

    // ------------------------------------------------------------------ 托盘 + 逻辑
    internal sealed class TrayApp : ApplicationContext
    {
        public Settings Cfg { get; private set; }
        public Engine Eng { get; private set; }
        public KeyHook Hook { get; private set; }
        public RawInputForm Raw { get { return _raw; } }
        public LightingController Lighting { get { return _lighting; } }

        private RawInputForm _raw;
        private LightingController _lighting;
        private OxPanelWatch _bridge;
        private EventWaitHandle _turboEvent;
        private DateTime _lastTurboAt = DateTime.MinValue;
        private DateTime _lastCtrlAt = DateTime.MinValue;
        private DateTime _lastWinAt = DateTime.MinValue;
        public static string CfgAnim = "elegant";
        public static string CfgBg = "";
        private bool _gameModeActive;
        private string _gamePrevMode = "";
        public static int LastForeignPid;   // 面板弹出前的前台进程
        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private MainForm _form;
        private System.Windows.Forms.Timer _poll;
        private System.Windows.Forms.Timer _debounce;
        private System.Windows.Forms.Timer _padTimer;
        private bool _padWas;
        private EventWaitHandle _showEvent;
        private string _appliedKey = "";
        private DateTime _lastAttempt = DateTime.MinValue;
        private string _source = "battery";
        private string _status = "";
        private int _failCount;
        private float _obsFast = -1, _obsSlow = -1, _obsTctl = -1;
        private float _sentFast = -1, _sentSlow = -1, _sentTctl = -1;
        private DateTime _lastApplyAt = DateTime.MinValue;
        private DateTime _lastExternalAt = DateTime.MinValue;
        private DateTime _restoreAt = DateTime.MinValue;
        private const bool RestoreAfterExternal = false;   // 写回会与 OneXConsole 抢 SMU 邮箱，导致卡死，默认关闭

        public string EngineDir()
        {
            return string.IsNullOrEmpty(Cfg.DllDir) ? Store.Dir : Cfg.DllDir;
        }

        public void ReinitEngine()
        {
            Eng = new Engine();
            Eng.Init(EngineDir());
            _appliedKey = "";
            _failCount = 0;
            Log("重新初始化引擎: " + (Eng.Ready ? "ready" : Eng.Error));
            if (Eng.Ready) ApplyNow("reinit");
        }

        public string Source { get { return _source; } }
        public string Status { get { return _status; } }

        public TrayApp(bool startHidden)
        {
            Cfg = Store.Load();
            CfgAnim = string.IsNullOrEmpty(Cfg.AnimMode) ? "elegant" : Cfg.AnimMode;
            CfgBg = Cfg.BgImage ?? "";
            Theme.LoadFrom(Cfg);   // 启动时套用上次保存的主题
            Eng = new Engine();
            Eng.Init(EngineDir());

            Hook = new KeyHook();
            Hook.Start();
            Hook.KeyPressed += OnKeyPressed;

            _raw = new RawInputForm();
            _raw.HidInput += OnHidInput;

            _turboEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\RyzenTunerTurbo");
            Thread th = new Thread(WaitTurbo);
            th.IsBackground = true;
            th.Start();
            if (!RyzenTuner.MainForm.ShotMode) LaunchPipeHelper();

            _bridge = new OxPanelWatch();
            _bridge.Enabled = false;   // 安全模式：不监听 OneXConsole 面板，避免反复开关窗口
            _bridge.Triggered += delegate { ToggleForm(); };
            if (_bridge.Enabled) _bridge.Start();

            _lighting = new LightingController();
            if (Cfg.RgbEnabled) { try { ApplyLighting(true); } catch { } }

            BuildTray();
            BuildMenu();

            _poll = new System.Windows.Forms.Timer();
            _poll.Interval = 800;
            _poll.Tick += delegate { OnTick(); };
            _poll.Start();

            _padTimer = new System.Windows.Forms.Timer();
            _padTimer.Interval = 150;
            _padTimer.Tick += delegate { OnPadTick(); };
            _padTimer.Start();

            _debounce = new System.Windows.Forms.Timer();
            _debounce.Interval = 250;
            _debounce.Tick += delegate { _debounce.Stop(); ApplyNow("ui"); };

            try { _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\RyzenTuner.Show"); }
            catch { _showEvent = null; }
            if (_showEvent != null)
            {
                Thread t = new Thread(ShowWaiter);
                t.IsBackground = true;
                t.Start();
            }

            RefreshSource();
            RefreshMenu();
            Log("启动: engine=" + (Eng.Ready ? "ready" : Eng.Error) + " mode=" + Cfg.Mode + " source=" + _source);
            UpdateTrayText();
            if (Eng.Ready) ApplyNow("startup");

            if (!startHidden && !Cfg.StartMinimized) ShowForm();
        }

        private void ShowWaiter()
        {
            while (true)
            {
                if (_showEvent == null) return;
                _showEvent.WaitOne();
                try { ShowForm(); } catch { }
            }
        }

        // ---------------------------------------------------------- 托盘
        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = MakeIcon();
            _tray.Text = "RyzenTuner";
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowForm(); };
        }

        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();
            _menu.Font = new Font("Microsoft YaHei UI", 9f);
            _menu.ShowImageMargin = false;

            ToolStripMenuItem open = new ToolStripMenuItem("打开控制面板");
            open.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            open.Click += delegate { ShowForm(); };
            _menu.Items.Add(open);
            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem auto = new ToolStripMenuItem("自动切换（跟随供电）");
            auto.CheckOnClick = false;
            auto.Click += delegate { Cfg.Mode = "auto"; Cfg.AutoSwitch = true; Save(); RefreshMenu(); ApplyNow("tray-auto"); };
            _menu.Items.Add(auto);
            _menu.Tag = this;
            _menu.Items[2].Name = "auto";

            ToolStripMenuItem fixedItem = new ToolStripMenuItem("固定档位");
            string[] names = new string[] { "battery", "typec", "dcin" };
            string[] texts = new string[] { "电池", "USB-C", "DC-IN" };
            for (int i = 0; i < 3; i++)
            {
                string key = names[i];
                ToolStripMenuItem mi = new ToolStripMenuItem(texts[i]);
                mi.Tag = key;
                mi.Click += delegate(object s, EventArgs e)
                {
                    Cfg.Mode = "fixed";
                    Cfg.FixedSource = (string)((ToolStripMenuItem)s).Tag;
                    Save(); RefreshMenu(); ApplyNow("tray-fixed");
                };
                fixedItem.DropDownItems.Add(mi);
            }
            _menu.Items.Add(fixedItem);
            fixedItem.Name = "fixed";

            ToolStripMenuItem manual = new ToolStripMenuItem("手动功耗");
            int[] watts = new int[] { 15, 25, 35, 45, 55, 65, 75, 85, 100, 120 };
            for (int i = 0; i < watts.Length; i++)
            {
                int w = watts[i];
                ToolStripMenuItem mi = new ToolStripMenuItem(w + " W");
                mi.Tag = w;
                mi.Click += delegate(object s, EventArgs e)
                {
                    Cfg.Mode = "manual";
                    Cfg.ManualW = (int)((ToolStripMenuItem)s).Tag;
                    Save(); RefreshMenu(); ApplyNow("tray-manual");
                    if (_form != null) _form.SyncFromSettings();
                };
                manual.DropDownItems.Add(mi);
            }
            _menu.Items.Add(manual);
            manual.Name = "manual";

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem startup = new ToolStripMenuItem("开机自动启动");
            startup.CheckOnClick = false;
            startup.Click += delegate { ToggleAutostart(); RefreshMenu(); };
            startup.Name = "startup";
            _menu.Items.Add(startup);

            ToolStripMenuItem learn = new ToolStripMenuItem("设置 TURBO 按键…");
            learn.Click += delegate { ShowForm(); if (_form != null) _form.BeginLearn(true); };
            _menu.Items.Add(learn);


            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem quit = new ToolStripMenuItem("退出");
            quit.Click += delegate { ExitApp(); };
            _menu.Items.Add(quit);

            _tray.ContextMenuStrip = _menu;
        }

        private void RefreshMenu()
        {
            foreach (ToolStripItem it in _menu.Items)
            {
                if (it.Name == "auto") ((ToolStripMenuItem)it).Checked = (Cfg.Mode == "auto");
                if (it.Name == "startup") ((ToolStripMenuItem)it).Checked = Autostart.IsEnabled();
                if (it.Name == "fixed")
                {
                    ToolStripMenuItem fi = (ToolStripMenuItem)it;
                    fi.Checked = (Cfg.Mode == "fixed");
                    foreach (ToolStripItem sub in fi.DropDownItems)
                        ((ToolStripMenuItem)sub).Checked = (Cfg.Mode == "fixed" && (string)sub.Tag == Cfg.FixedSource);
                }
                if (it.Name == "manual")
                {
                    ToolStripMenuItem mi = (ToolStripMenuItem)it;
                    mi.Checked = (Cfg.Mode == "manual");
                    foreach (ToolStripItem sub in mi.DropDownItems)
                        ((ToolStripMenuItem)sub).Checked = (Cfg.Mode == "manual" && (int)sub.Tag == Cfg.ManualW);
                }
            }
        }

        private void UpdateTrayText()
        {
            string src = SourceText(_source);
            string w = "";
            if (Eng.Ready)
            {
                if (Cfg.Mode == "manual") w = " · " + Cfg.ManualW + "W";
            }
            string txt = "RyzenTuner · " + src + w;
            if (txt.Length > 62) txt = txt.Substring(0, 62);
            _tray.Text = txt;
        }

        public static string SourceText(string s)
        {
            if (s == "battery") return "电池";
            if (s == "typec") return "USB-C";
            return "DC-IN";
        }

        // ---------------------------------------------------------- 窗口
        private DateTime _lastToggle = DateTime.MinValue;

        public void ToggleForm()
        {
            if ((DateTime.Now - _lastToggle).TotalMilliseconds < 350) return;
            _lastToggle = DateTime.Now;

            if (_form != null && !_form.IsDisposed && _form.Visible)
            {
                _form.HideAnimated();
                return;
            }
            CaptureForeground();   // 抢焦点前记下当前前台进程
            ShowForm();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        private void CheckGameMode()
        {
            if (!Cfg.GameAutoPerf) return;
            string list = Cfg.CpuApps ?? "";
            if (list.Trim().Length == 0) return;

            bool running = false;
            char[] seps = new char[] { (char)13, (char)10, ',', ';' };
            foreach (string nm in list.Split(seps, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = nm.Trim();
                if (name.Length == 0) continue;
                try { if (Process.GetProcessesByName(name).Length > 0) { running = true; break; } } catch { }
            }

            if (running && !_gameModeActive)
            {
                _gamePrevMode = Cfg.PowerMode;
                if (Cfg.PowerMode != "perf")
                {
                    Cfg.PowerMode = "perf";
                    Save();
                    ApplyNow("game-auto");
                    Log("检测到列表应用运行，自动切到性能模式");
                    if (_form != null && !_form.IsDisposed && _form.Visible) _form.RefreshStatus();
                }
                _gameModeActive = true;
            }
            else if (!running && _gameModeActive)
            {
                _gameModeActive = false;
                Cfg.PowerMode = _gamePrevMode;
                Save();
                ApplyNow("game-auto-exit");
                Log("列表应用已退出，恢复原模式");
                if (_form != null && !_form.IsDisposed && _form.Visible) _form.RefreshStatus();
            }
        }

        public static void CaptureForeground()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                if (pid != 0 && (int)pid != Process.GetCurrentProcess().Id) LastForeignPid = (int)pid;
            }
            catch { }
        }

        public static bool OneXRunning()
        {
            try { return Process.GetProcessesByName("OneXConsole").Length > 0; } catch { return false; }
        }

        private void CheckExternalLimits()
        {
            // OneXConsole 在跑时它高频访问 EC/SMU，我们再去读会争用总线导致卡死
            if (OneXRunning()) return;

            float f, s, t;
            if (!Eng.TryReadLimits(out f, out s, out t)) return;

            if (_obsFast < 0)
            {
                _obsFast = f; _obsSlow = s; _obsTctl = t;
                return;
            }

            bool changed = Math.Abs(f - _obsFast) >= 1.0f || Math.Abs(s - _obsSlow) >= 1.0f || Math.Abs(t - _obsTctl) >= 1.0f;
            if (!changed) return;

            _obsFast = f; _obsSlow = s; _obsTctl = t;

            // 只有“与最后一次写入值不同”才算外部改动；这样我们自己的下发不会误触发
            bool isOurs = Math.Abs(f - _sentFast) < 0.8f && Math.Abs(s - _sentSlow) < 0.8f && Math.Abs(t - _sentTctl) < 0.8f;
            if (isOurs) return;
            if ((DateTime.Now - _lastExternalAt).TotalMilliseconds < 800) return;
            _lastExternalAt = DateTime.Now;

            Log(string.Format("检测到外部功耗变更: FAST {0:0.#}W / SLOW {1:0.#}W / {2:0}℃ → 视为 TURBO", f, s, t));
            ToggleForm();
            // 不在按键瞬间写回：连续按键会造成 SMU 写入风暴，等静止 3 秒后只恢复一次
            _restoreAt = DateTime.Now.AddSeconds(3);
        }

        private void CloseOxPanelsSoon()
        {
            // 等 OneXConsole 的快捷面板弹出来再关掉它，只保留我们的界面
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 450;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                try
                {
                    int n = OxPanelWatch.CloseQuickPanels();
                    if (n > 0) Log("已关闭 OneXConsole 快捷面板 " + n + " 个");
                }
                catch { }
            };
            t.Start();
        }

        /// <summary>以普通用户身份启动管道助手（提权进程收不到管道推送事件）。</summary>
        private void LaunchPipeHelper()
        {
            try
            {
                // 先清掉可能残留的旧实例（助手没有权限杀它，会导致连到异常实例）
                foreach (Process old in Process.GetProcessesByName("CompatLayerCT"))
                {
                    try { old.Kill(); } catch { }
                }
                Thread.Sleep(500);

                string exe = Application.ExecutablePath;
                string vbs = Path.Combine(Path.GetTempPath(), "ryzentuner_pipe.vbs");
                string body =
                    "Set sh = CreateObject(\"WScript.Shell\")\r\n" +
                    "sh.Run \"\"\"" + exe + "\"\" --pipealert\", 0, False\r\n";
                File.WriteAllText(vbs, body, Encoding.ASCII);
                Process.Start("explorer.exe", "\"" + vbs + "\"");
            }
            catch (Exception ex) { Log("管道助手启动失败: " + ex.Message); }
        }

        private void WaitTurbo()
        {
            while (true)
            {
                try
                {
                    if (!_turboEvent.WaitOne(1000)) continue;
                    if (_form != null && !_form.IsDisposed)
                        _form.BeginInvoke(new Action(TurboToggle));
                    else
                        TurboToggle();
                }
                catch { }
            }
        }

        public void TurboToggle()
        {
            if ((DateTime.Now - _lastTurboAt).TotalMilliseconds < 250) return;   // 仅防抖，避免同一次按键被两条通道重复触发
            _lastTurboAt = DateTime.Now;
            Log("唤醒触发 → " + (_form != null && !_form.IsDisposed && _form.Visible ? "关闭" : "打开"));
            ToggleForm();
        }

        public void ShowForm()
        {
            CaptureForeground();
            if (_form == null || _form.IsDisposed)
            {
                _form = new MainForm(this);
            }
            _form.ShowAnimated();
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            _form.BringToFront();
            _form.SyncFromSettings();
        }

        public void OneClickPerformance()
        {
            if (!Cfg.PerfActive)
            {
                int f, s, t; bool ps, mp;
                ResolveTarget(out f, out s, out t, out ps, out mp);

                // 记录原设置，供再次点击恢复
                Cfg.PerfActive = true;
                Cfg.PerfSrc = _source;
                Cfg.PerfMode = Cfg.Mode;
                Cfg.PerfFast = f; Cfg.PerfSlow = s; Cfg.PerfTctl = t;
                Cfg.PerfPs = ps; Cfg.PerfMp = mp;

                Cfg.Mode = "manual";
                Cfg.ManualW = 120;
                Save();

                if (Eng.Ready)
                {
                    Eng.Apply(120, 120, 95, false, false);
                    _status = Eng.LastAction;
                    _appliedKey = DesiredKey();
                    _lastApplyAt = DateTime.Now;
                    _sentFast = 120; _sentSlow = 120; _sentTctl = 95;
                }
                // 不切换 Windows 电源计划：切换会改动屏幕亮度
                Log("一键性能模式：120W / 120W / 95℃（原设置已保存）");
            }
            else
            {
                Cfg.PerfActive = false;
                Cfg.Mode = string.IsNullOrEmpty(Cfg.PerfMode) ? "auto" : Cfg.PerfMode;
                RestoreProfile(Cfg.PerfSrc, Cfg.PerfFast, Cfg.PerfSlow, Cfg.PerfTctl, Cfg.PerfPs, Cfg.PerfMp);
                Save();
                ApplyNow("perf-restore");
                Log("已恢复性能模式前的功耗设置");
            }
        }

        private void RestoreProfile(string src, int f, int s, int t, bool ps, bool mp)
        {
            if (src == "battery")
            {
                Cfg.BatteryFast = f; Cfg.BatterySlow = s; Cfg.BatteryTctl = t;
                Cfg.BatteryPs = ps; Cfg.BatteryMp = mp;
            }
            else if (src == "typec")
            {
                Cfg.TypecFast = f; Cfg.TypecSlow = s; Cfg.TypecTctl = t;
                Cfg.TypecPs = ps; Cfg.TypecMp = mp;
            }
            else
            {
                Cfg.DcinFast = f; Cfg.DcinSlow = s; Cfg.DcinTctl = t;
                Cfg.DcinPs = ps; Cfg.DcinMp = mp;
            }
        }

        private string ActiveScheme()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powercfg.exe", "/getactivescheme");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    int i = outp.IndexOf("GUID:") + 5;
                    if (i > 4 && outp.Length > i + 36) return outp.Substring(i, 36).Trim();
                }
            }
            catch { }
            return null;
        }

        private void SetScheme(string guid)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powercfg.exe", "/s " + guid);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch { }
        }

        public void RebuildUi()
        {
            MainForm old = _form;
            _form = null;
            if (old != null && !old.IsDisposed) { old.Hide(); old.Dispose(); }
            ShowForm();
        }

        public MainForm Form { get { return _form; } }
        public int MenuItemCount { get { return _menu == null ? 0 : _menu.Items.Count; } }

        public void Shutdown()
        {
            try { _tray.Visible = false; } catch { }
            try { Hook.Dispose(); } catch { }
            try { _raw.Dispose(); } catch { }
        }

        public void ExitApp()
        {
            try { Store.Save(Cfg); } catch { }
            _tray.Visible = false;
            Hook.Dispose();
            try { _raw.Dispose(); } catch { }
            try { if (_lighting != null) _lighting.Shutdown(); } catch { }
            Application.Exit();
        }

        public void Save()
        {
            Store.Save(Cfg);
        }


        public void ApplyLighting(bool enabled)
        {
            Cfg.RgbEnabled = enabled;
            Save();
            if (_lighting == null) _lighting = new LightingController();
            _lighting.Configure(enabled, Cfg.RgbMode, Cfg.RgbR, Cfg.RgbG, Cfg.RgbB, Cfg.RgbSpeed, Cfg.RgbBrightness, Cfg.RgbEffect);
            Log("灯效: " + _lighting.Status + (string.IsNullOrEmpty(_lighting.LastError) ? "" : "  (" + _lighting.LastError + ")"));
        }

        public void ToggleAutostart()
        {
            if (Autostart.IsEnabled()) Autostart.Disable();
            else Autostart.Enable(Application.ExecutablePath);
        }

        // ---------------------------------------------------------- 状态
        public void RefreshSource()
        {
            if (Cfg.SourceOverride != "auto") { _source = Cfg.SourceOverride; return; }

            // 优先用 EC 寄存器 0xFE 判断（1=电池 3=Type-C 5=DC-IN）
            string ec = PowerSource.Detect();
            if (ec != null) { _source = ec; return; }

            if (!Engine.OnAc) { _source = "battery"; return; }
            _source = (Cfg.AcSource == "typec") ? "typec" : "dcin";
        }

        public void OnTick()
        {
            // TURBO 通过键盘钩子触发，这里不再轮询 SMU（避免与 OneXConsole 争用导致卡死）
            string prev = _source;
            RefreshSource();
            if (prev != _source) _appliedKey = "";

            if (Eng.Ready)
            {
                string key = DesiredKey();
                double backoff = 5 + Math.Min(_failCount * 10, 55);
                if (key != _appliedKey && (DateTime.Now - _lastAttempt).TotalSeconds >= backoff)
                    ApplyNow("auto");
            }
            UpdateTrayText();
            if (_form != null && !_form.IsDisposed) { _form.TrackForeground(); _form.ReapplyGlobalMask(); }
            CheckGameMode();

            if (_form != null && !_form.IsDisposed && _form.Visible) _form.RefreshStatus();
        }

        public void NotifyUiChanged()
        {
            _debounce.Stop();
            _debounce.Start();
        }

        public void ApplyNow(string reason)
        {
            if (!Eng.Ready) { _status = Eng.Error; return; }
            _lastAttempt = DateTime.Now;

            int fast, slow, tctl;
            bool ps, mp;
            ResolveTarget(out fast, out slow, out tctl, out ps, out mp);

            bool ok = Eng.Apply(fast, slow, tctl, ps, mp);
            _status = Eng.LastAction;
            if (ok)
            {
                _appliedKey = DesiredKey();
                _failCount = 0;
                _lastApplyAt = DateTime.Now;
                _sentFast = fast; _sentSlow = slow; _sentTctl = tctl;
            }
            else { _failCount++; }

            UpdateTrayText();
            if (_form != null && !_form.IsDisposed) { _form.TrackForeground(); _form.ReapplyGlobalMask(); }
            CheckGameMode();

            if (_form != null && !_form.IsDisposed && _form.Visible) _form.RefreshStatus();
            Log(reason + " -> " + _status);
        }

        private void GetProfile(string src, out int fast, out int slow, out int tctl, out bool ps, out bool mp)
        {
            if (src == "battery") { fast = Cfg.BatteryFast; slow = Cfg.BatterySlow; tctl = Cfg.BatteryTctl; ps = Cfg.BatteryPs; mp = Cfg.BatteryMp; }
            else if (src == "typec") { fast = Cfg.TypecFast; slow = Cfg.TypecSlow; tctl = Cfg.TypecTctl; ps = Cfg.TypecPs; mp = Cfg.TypecMp; }
            else { fast = Cfg.DcinFast; slow = Cfg.DcinSlow; tctl = Cfg.DcinTctl; ps = Cfg.DcinPs; mp = Cfg.DcinMp; }
        }
        private void ResolveTarget(out int fast, out int slow, out int tctl, out bool ps, out bool mp)
        {
            // 三个功率模式优先级最高
            if (!string.IsNullOrEmpty(Cfg.PowerMode))
            {
                switch (Cfg.PowerMode)
                {
                    case "eco": fast = 25; slow = 15; tctl = 80; ps = true; mp = false; return;
                    case "office": fast = 55; slow = 45; tctl = 87; ps = false; mp = false; return;
                    case "perf": fast = 120; slow = 120; tctl = 95; ps = false; mp = true; return;
                }
            }
            if (Cfg.CustomSupply)
            {
                // 自定义供电：直接使用当前供电档位
                GetProfile(_source, out fast, out slow, out tctl, out ps, out mp);
                return;
            }
            if (Cfg.Mode == "manual")
            {
                fast = Cfg.ManualW; slow = Cfg.ManualW; tctl = Cfg.DcinTctl; ps = false; mp = false;
                return;
            }
            string src = (Cfg.Mode == "fixed") ? Cfg.FixedSource : _source;
            if (src == "battery") { fast = Cfg.BatteryFast; slow = Cfg.BatterySlow; tctl = Cfg.BatteryTctl; ps = Cfg.BatteryPs; mp = Cfg.BatteryMp; }
            else if (src == "typec") { fast = Cfg.TypecFast; slow = Cfg.TypecSlow; tctl = Cfg.TypecTctl; ps = Cfg.TypecPs; mp = Cfg.TypecMp; }
            else { fast = Cfg.DcinFast; slow = Cfg.DcinSlow; tctl = Cfg.DcinTctl; ps = Cfg.DcinPs; mp = Cfg.DcinMp; }
        }

        public string DesiredKey()
        {
            int fast, slow, tctl; bool ps, mp;
            ResolveTarget(out fast, out slow, out tctl, out ps, out mp);
            string src = (Cfg.Mode == "manual") ? "manual" : ((Cfg.Mode == "fixed") ? Cfg.FixedSource : _source);
            return src + "|" + fast + "|" + slow + "|" + tctl + "|" + ps + "|" + mp;
        }

        public void GetTarget(out int fast, out int slow, out int tctl, out bool ps, out bool mp)
        {
            ResolveTarget(out fast, out slow, out tctl, out ps, out mp);
        }

        // ---------------------------------------------------------- TURBO
        private void OnKeyPressed(int vk, int scan, string name)
        {
            if (Hook.Learning) return;

            // 记录修饰键事件时间，供“顺序匹配”使用
            if (vk == 0x11) _lastCtrlAt = DateTime.Now;
            if (vk == 0x5B || vk == 0x5C) _lastWinAt = DateTime.Now;
            if (scan == 0x38 || vk == 0xA4 || vk == 0x12)
                Log("按键: vk=0x" + vk.ToString("X2") + " scan=0x" + scan.ToString("X2") + " mods=" + KeyHook.CurrentMods);

            if (Cfg.TurboKind != "key") return;

            // 优先用扫描码区分：功能键常伪装成 Ctrl/Shift，VK 会与真实按键冲突
            bool hit;
            if (Cfg.TurboScan != 0) hit = (scan == Cfg.TurboScan);
            else if (Cfg.TurboVk != 0) hit = (vk == Cfg.TurboVk);
            else hit = false;

            bool seqCtrlWin = (Cfg.TurboScan == 56) &&
                              (DateTime.Now - _lastCtrlAt).TotalMilliseconds < 500 &&
                              (DateTime.Now - _lastWinAt).TotalMilliseconds < 500;
            if (hit && (KeyHook.ModsFor(vk) == Cfg.TurboMods || seqCtrlWin)) { TurboToggle(); return; }

            // 自定义唤醒键
            if (Cfg.CustomScan != 0 || Cfg.CustomVk != 0)
            {
                bool chit = (Cfg.CustomScan != 0) ? (scan == Cfg.CustomScan) : (vk == Cfg.CustomVk);
                if (chit && KeyHook.ModsFor(vk) == Cfg.CustomMods) TurboToggle();
            }
        }

        private void OnPadTick()
        {
            if (Cfg.TurboKind != "pad" || Cfg.TurboPadMask == 0) { _padWas = false; return; }
            ushort b = Pad.Buttons();
            bool hit = (b & (ushort)Cfg.TurboPadMask) == (ushort)Cfg.TurboPadMask;
            if (hit && !_padWas) ToggleForm();
            _padWas = hit;
        }

        private void OnHidInput(int page, int usage)
        {
            if (Hook.Learning) return;
            if (Cfg.TurboKind != "hid") return;
            if (page == Cfg.TurboPage && usage == Cfg.TurboUsage) ToggleForm();
        }

        // ---------------------------------------------------------- 工具
        public static string LogPath
        {
            get
            {
                string dir = Path.Combine(Store.Dir, "logs");
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "tuner.log");
            }
        }

        public static void Log(string msg)
        {
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static Icon MakeIcon()
        {
            try
            {
                string icoPath = Path.Combine(Store.Dir, "app.ico");
                if (File.Exists(icoPath)) return new Icon(icoPath, 32, 32);
            }
            catch { }

            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(Color.FromArgb(226, 59, 46), 4.5f))
                    g.DrawEllipse(p, 3, 3, 26, 26);
            }
            IntPtr h = bmp.GetHicon();
            return Icon.FromHandle(h);
        }
    }

    // ------------------------------------------------------------------ 主窗口
        // ---------------- 快照滑动窗口 ----------------
    internal sealed class SlideWindow : Form
        {
            public SlideWindow(Bitmap bmp)
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                Size = bmp.Size;
                BackgroundImage = bmp;
                BackgroundImageLayout = ImageLayout.Stretch;
                DoubleBuffered = true;
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
                    cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
                    cp.ExStyle |= 0x00080000;   // WS_EX_LAYERED（分层窗口，移动由 DWM 合成）
                    return cp;
                }
            }

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern bool SetLayeredWindowAttributes(IntPtr h, int key, byte alpha, int flags);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

            public void MoveBy(int dx)
            {
                Left += dx;   // 分层窗口下移动不会触发重绘
            }

            public void SetAlpha(byte a)
            {
                try { SetLayeredWindowAttributes(Handle, 0, a, 0x02); } catch { }
            }

            public void MakeLayered()
            {
                try { SetLayeredWindowAttributes(Handle, 0, 255, 0x02); } catch { }
            }
        }

    internal sealed class MainForm : Form
    {
        public static bool ShotMode;
        public static int ShotTab;

        private readonly TrayApp _app;
        private readonly float _sc;

        private Label _lblSource, _lblBadge, _lblBattery, _lblLimits, _lblEngine, _lblAction, _lblBinding, _lblCustom, _lblPower;
        private Label _lblFastValue, _lblSlowValue, _lblTempValue, _lblProfileTitle;
        private TrackBar _trkFast, _trkSlow, _trkTemp;
        private CheckBox _chkAuto, _chkManual, _chkPs, _chkMp, _chkStartup, _chkTray;
        private Button[] _srcBtns = new Button[4];
        private Button _btnBattery, _btnTypec, _btnDcin, _btnPerf, _btnEco, _btnOffice;
        private readonly System.Collections.Generic.List<CheckBox> _cpuChecks = new System.Collections.Generic.List<CheckBox>();
        private readonly System.Collections.Generic.List<CheckBox> _rowChecks = new System.Collections.Generic.List<CheckBox>();
        private bool _rowSyncing;
        private System.Windows.Forms.Timer _cpuApplyTimer;
        private ulong _lastGlobalMask;
        private DateTime _lastReapply = DateTime.MinValue;
        private CheckBox _chkCustomSupply;
        private Button _btnSmart, _btnTop, _btnQuit, _btnGameAuto;
        private Label _lblFooter;
        private TextBox _txtCpuApps;
        private Label _lblCpuInfo;
        private int _lastForegroundPid;
        private Button _tabPower, _tabProfile, _tabLight;
        private Panel _pagePower, _pageProfile, _pageLight, _cardHost;
        private LightingForm _lightPage;
        private string _editing = "dcin";
        private bool _syncing;
        private string _cachedLimits;
        private System.Windows.Forms.Timer _applyTimer;
        private TrackBar _trkBright, _trkVoice;
        private Label _lblBright, _lblVoice;
        private string _lastStatus;
        private bool _dialogOpen;
        private DateTime _ignoreDeactivateUntil = DateTime.MinValue;
        private DateTime _animStart = DateTime.MinValue;
        private System.Windows.Forms.Timer _animTimer;
        private Action _animDone;
        private DateTime _lastLimitsRead = DateTime.MinValue;

        public MainForm(TrayApp app)
        {
            _app = app;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _sc = g.DpiX / 96f;

            Text = "功耗控制台";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Microsoft YaHei UI", 9f);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;
            TopMost = _app.Cfg.AlwaysOnTop;
            DoubleBuffered = true;
            ShowInTaskbar = false;
            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 20;
            _animTimer.Tick += delegate { AnimTick(); };
            Icon = TrayApp.MakeIcon();
            Deactivate += delegate
            {
                if (_dialogOpen) return;
                if (!_app.Cfg.SmartClose) return;
                if (DateTime.Now < _ignoreDeactivateUntil) return;
                try
                {
                    IntPtr fg = GetForegroundWindow();
                    uint pid;
                    GetWindowThreadProcessId(fg, out pid);
                    if (pid == (uint)Process.GetCurrentProcess().Id) return;   // 焦点还在自己的窗口上
                }
                catch { }
                HideAnimated();
            };
            PlaceRight();
            Shown += delegate { PlaceRight(); };
            if (ShotMode) { StartPosition = FormStartPosition.Manual; Location = new Point(-4000, -4000); }

            BuildUi();
            SyncFromSettings();
            RefreshStatus();
            if (ShotMode) SetTab(ShotTab);
        }

        private void PlaceRight()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = S(440);
            int h = Math.Min(S(1050), wa.Height - S(8));
            ClientSize = new Size(w, Math.Max(S(700), h));
            int left = wa.Right - Width - S(2);
            if (left < wa.Left) left = wa.Left;
            Left = left;
            Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
        }

        private bool _dragging;
        private Point _dragStart;

        private void HeaderMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; }
        }

        private void HeaderMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point p = PointToScreen(e.Location);
            Location = new Point(p.X - _dragStart.X, p.Y - _dragStart.Y);
        }

        private void HeaderMouseUp(object sender, MouseEventArgs e) { _dragging = false; }

        private int S(int v) { return (int)Math.Round(v * _sc); }

        private Font F(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style);
        }

        private Card AddCard(int x, int y, int w, int h, string title)
        {
            Card c = new Card();
            c.Bounds = new Rectangle(S(x), S(y), S(w), S(h));
            if (title.Length > 0)
            {
                Label t = new Label();
                t.Text = title;
                t.ForeColor = Theme.Dim;
                t.Font = F(8f, FontStyle.Regular);
                t.AutoSize = false;
                t.Bounds = new Rectangle(S(16), S(12), S(w - 32), S(18));
                c.Controls.Add(t);
            }
            _cardHost.Controls.Add(c);
            return c;
        }

        private Label Lbl(Control parent, string text, int x, int y, int w, int h, float size, Color color, FontStyle style)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = color;
            l.Font = F(size, style);
            l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Bounds = new Rectangle(S(x), S(y), S(w), S(h));
            parent.Controls.Add(l);
            return l;
        }

        private Button Btn(Control parent, string text, int x, int y, int w, int h, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = F(9f, FontStyle.Regular);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Theme.Border;
            b.FlatAppearance.BorderSize = 1;
            b.BackColor = Theme.Btn;
            b.ForeColor = Theme.Text;
            b.Bounds = new Rectangle(S(x), S(y), S(w), S(h));
            b.Cursor = Cursors.Hand;
            b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        private CheckBox Chk(Control parent, string text, int x, int y, int w, EventHandler onChanged)
        {
            // 只有左边的小方块可点击，文字是独立标签
            CheckBox c = new CheckBox();
            c.Text = "";
            c.ForeColor = Theme.Text;
            c.Font = F(9f, FontStyle.Regular);
            c.AutoSize = false;
            c.Bounds = new Rectangle(S(x), S(y), S(22), S(22));
            c.CheckedChanged += onChanged;
            parent.Controls.Add(c);

            Label lb = new Label();
            lb.Text = text;
            lb.ForeColor = Theme.Text;
            lb.Font = F(9f, FontStyle.Regular);
            lb.AutoSize = false;
            lb.Bounds = new Rectangle(S(x + 24), S(y + 2), S(Math.Max(40, w - 24)), S(20));
            lb.TextAlign = ContentAlignment.MiddleLeft;
            parent.Controls.Add(lb);
            lb.BringToFront();
            return c;
        }

        private TrackBar Trk(Control parent, int x, int y, int w, int min, int max, int val, EventHandler onChanged)
        {
            TrackBar t = new TrackBar();
            t.Minimum = min;
            t.Maximum = max;
            t.Value = val;
            t.TickStyle = TickStyle.None;
            t.SmallChange = 1;
            t.LargeChange = 5;
            t.AutoSize = false;
            t.Bounds = new Rectangle(S(x), S(y), S(w), S(28));
            t.BackColor = Theme.Card;
            t.ValueChanged += onChanged;
            parent.Controls.Add(t);
            return t;
        }

        private bool Elegant { get { return _app.Cfg.AnimMode != "classic"; } }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr h, int idx);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr h, int idx, int val);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr h, int key, byte alpha, int flags);

        private void SetLayered(bool on)
        {
            try
            {
                int ex = GetWindowLong(Handle, -20);   // GWL_EXSTYLE
                if (on) ex |= 0x00080000; else ex &= ~0x00080000;
                SetWindowLong(Handle, -20, ex);
                if (on) SetLayeredWindowAttributes(Handle, 0, 255, 0x02);
            }
            catch { }
        }
        private Bitmap GetSplash()
        {
            if (_splash != null) return _splash;
            try
            {
                PlaceRight();
                bool wasVisible = Visible;
                Opacity = 0;
                Show();
                Application.DoEvents();
                Bitmap bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                if (!wasVisible) Hide();
                Opacity = 1;
                _splash = bmp;
                TrayApp.Log("已缓存基础设置快照");
            }
            catch { }
            return _splash;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private static void MoveNoLayout(IntPtr h, int x, int y)
        {
            try { SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); } catch { }   // NOSIZE|NOZORDER|NOACTIVATE
        }
        public void ShowAnimated()
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            if (IsDisposed) return;
            Card.CheapPaint = false;
            _ignoreDeactivateUntil = DateTime.Now.AddMilliseconds(700);
            PlaceRight();
            int target = Left;
            _homeLeft = target;

            if (!Elegant)
            {
                Opacity = 1;
                Show();
                try { Activate(); BringToFront(); } catch { }
                return;
            }

            Bitmap splash = GetSplash();
            Hide();                      // 真窗口先不显示
            if (splash == null)
            {
                Opacity = 1; Show();
                try { Activate(); BringToFront(); } catch { }
                return;
            }

            if (_splashWin == null)
            {
                _splashWin = new SlideWindow(splash);
                _splashWin.Show();
                _splashWin.Hide();
            }
            _splashWin.Left = Screen.PrimaryScreen.WorkingArea.Right;
            _splashWin.Top = Top;
            _splashWin.Show();
            _splashWin.MakeLayered();

            TrayApp.Log("滑动计时: 准备=" + sw.ElapsedMilliseconds + "ms");
            _slideTargetLeft = target;
            _slideShowing = true;
            _sliding = true;
            _animStart = DateTime.Now;
            _animTimer.Stop();
            _animTimer.Start();
        }
        public void HideAnimated()
        {
            Card.CheapPaint = false;
            if (IsDisposed || !Visible) return;

            if (!Elegant) { Hide(); return; }

            Bitmap splash = _splash;
            if (splash == null) { Hide(); return; }

            if (_splashWin == null)
            {
                _splashWin = new SlideWindow(splash);
                _splashWin.Show();
                _splashWin.Hide();
            }
            _splashWin.Left = Left;
            _splashWin.Top = Top;
            _splashWin.Show();
            _splashWin.MakeLayered();
            Hide();

            _slideTargetLeft = Screen.PrimaryScreen.WorkingArea.Right;
            _slideShowing = false;
            _sliding = true;
            _animStart = DateTime.Now;
            _animTimer.Stop();
            _animTimer.Start();
        }
        private int _slideTargetLeft;
        private int _homeLeft;
        private Bitmap _splash;
        private SlideWindow _splashWin;
        public bool IsAnimating { get { return _sliding || _crossFade; } }
        private bool _crossFade;
        private int _fadeAlpha = 255;
        private bool _slideShowing;
        private void FadeTo(double target, Action done)
        {
            _fadeTarget = target;
            _fadeDone = done;
            _animStart = DateTime.Now;
            _animTimer.Stop();
            _animTimer.Start();
        }

        private double _fadeTarget = 1.0;
        private bool _sliding;
        private Action _fadeDone;

        private void AnimTick()
        {
            IntPtr h = IntPtr.Zero;
            int cur = _slideTargetLeft;
            if (_sliding && _splashWin != null) { h = _splashWin.Handle; cur = _splashWin.Left; }
            else if (_sliding) { h = Handle; cur = Left; }

            int diff = _slideTargetLeft - cur;
            bool timeout = (DateTime.Now - _animStart).TotalMilliseconds > 900;
            if (!_sliding || Math.Abs(diff) <= 4 || timeout)
            {
                if (_slideShowing && _splashWin != null && _splashWin.Visible && !_crossFade)
                {
                    // 照片到位：真窗口以不透明状态出现在照片下方，然后只把照片淡出
                    _crossFade = true;
                    _fadeAlpha = 255;
                    Left = _homeLeft;
                    Opacity = 1;
                    Show();
                    _splashWin.SetAlpha(255);
                    try { _splashWin.TopMost = true; } catch { }
                    return;
                }
                if (_crossFade)
                {
                    _fadeAlpha -= 40;
                    if (_fadeAlpha < 0) _fadeAlpha = 0;
                    if (_splashWin != null) _splashWin.SetAlpha((byte)_fadeAlpha);
                    if (_fadeAlpha > 0) return;
                }

                _animTimer.Stop();
                _sliding = false;
                _crossFade = false;
                Opacity = 1;
                Left = _homeLeft;
                if (_splashWin != null && _splashWin.Visible) _splashWin.Hide();
                try { Activate(); BringToFront(); } catch { }
                Action d = _animDone;
                _animDone = null;
                if (d != null) d();
                return;
            }
            int step = Math.Max(60, Math.Abs(diff) / 3);
            int nx = cur + (diff > 0 ? step : -step);
            MoveNoLayout(h, nx, _splashWin != null ? _splashWin.Top : Top);
        }
        private void AnimateTo(int targetLeft, Action done)
        {
            _slideTargetLeft = targetLeft;
            _animDone = done;
            _sliding = true;
            _animStart = DateTime.Now;
            _animTimer.Stop();
            _animTimer.Start();
        }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW：不进任务栏、不进 Alt+Tab
                return cp;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == VendorPipe.WmTurbo)
            {
                _app.TurboToggle();
                return;
            }
            base.WndProc(ref m);
        }

        private void BuildUi()
        {
            // ---- 顶栏（可拖动）
            Panel head = new Panel();
            head.Bounds = new Rectangle(0, 0, S(440), S(56));
            head.BackColor = Theme.Bg;
            Controls.Add(head);
            head.MouseDown += HeaderMouseDown;
            head.MouseMove += HeaderMouseMove;
            head.MouseUp += HeaderMouseUp;

            Label title = Lbl(head, "功耗控制台", 18, 8, 160, 28, 13f, Theme.Text, FontStyle.Bold);
            Lbl(head, "v" + Program.AppVersion, 178, 18, 70, 18, 8f, Theme.Dim, FontStyle.Regular);
            title.MouseDown += HeaderMouseDown;
            title.MouseMove += HeaderMouseMove;
            title.MouseUp += HeaderMouseUp;
            Label sun = Lbl(head, "Ryzen AI MAX+ 395 · 壹号本 SUPER X", 18, 33, 300, 16, 7.5f, Theme.Dim, FontStyle.Regular);
            sun.MouseDown += HeaderMouseDown;
            sun.MouseMove += HeaderMouseMove;
            sun.MouseUp += HeaderMouseUp;

            Button btnTheme = Btn(head, "主题", 328, 13, 48, 26, delegate { ShowTheme(); });
            btnTheme.Font = F(8f, FontStyle.Regular);
            Button btnClose = Btn(head, "✕", 382, 13, 34, 26, delegate { HideAnimated(); });
            btnClose.Font = F(9f, FontStyle.Bold);

            // ---- 页面容器
            _pagePower = new Panel();
            _pagePower.Bounds = new Rectangle(0, S(92), S(440), Math.Max(S(300), ClientSize.Height - S(92)));
            _pagePower.BackColor = Theme.Bg;
            _pagePower.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_pagePower);
            _cardHost = _pagePower;

            // 窗口按钮：页面右下角（智能关闭 | 置顶 | 关闭）
            _btnSmart = Btn(_pagePower, "智能关闭", 0, 0, S(120), S(24), delegate { ToggleSmartClose(); });
            _btnTop = Btn(_pagePower, "置顶", 0, 0, S(120), S(24), delegate { ToggleTopMost(); });
            _btnQuit = Btn(_pagePower, "关闭", 0, 0, S(120), S(24), delegate { HideAnimated(); });
            foreach (Button wb in new Button[] { _btnSmart, _btnTop, _btnQuit })
            {
                wb.Font = F(8f, FontStyle.Regular);
                wb.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            }
            _pagePower.Resize += delegate { PlaceWindowButtons(); };
            Shown += delegate { PlaceWindowButtons(); };
            PlaceWindowButtons();

            _pageProfile = new Panel();
            _pageProfile.Bounds = _pagePower.Bounds;
            _pageProfile.BackColor = Theme.Bg;
            _pageProfile.AutoScroll = true;
            _pageProfile.Anchor = _pagePower.Anchor;
            _pageProfile.Visible = false;
            Controls.Add(_pageProfile);

            _pageLight = new Panel();
            _pageLight.Bounds = _pagePower.Bounds;
            _pageLight.BackColor = Theme.Bg;
            _pageLight.Anchor = _pagePower.Anchor;
            _pageLight.Visible = false;
            Controls.Add(_pageLight);

            // ---- 页签
            _tabPower = Btn(this, "基础设置", 16, 58, 130, 30, delegate { SetTab(0); });
            _tabProfile = Btn(this, "性能自定义", 152, 58, 130, 30, delegate { SetTab(1); });
            _tabLight = Btn(this, "灯效", 288, 58, 136, 30, delegate { SetTab(2); });

            // ==================== 功耗页 ====================
            _btnEco = Btn(_pagePower, "省电模式\n4 线程", 16, 6, 130, 54, delegate { ApplyPowerMode("eco"); });
            _btnOffice = Btn(_pagePower, "日常办公\n12 线程", 152, 6, 130, 54, delegate { ApplyPowerMode("office"); });
            _btnPerf = Btn(_pagePower, "性能模式\n全核", 288, 6, 136, 54, delegate { ApplyPowerMode("perf"); });
            foreach (Button mb in new Button[] { _btnEco, _btnOffice, _btnPerf })
            {
                mb.Font = F(9f, FontStyle.Bold);
                mb.TextAlign = ContentAlignment.MiddleCenter;
                mb.BackColor = Theme.Btn;
                mb.ForeColor = Theme.Text;
                mb.FlatAppearance.BorderColor = Theme.Border;
            }

            Card c1 = AddCard(16, 60, 408, 212, "当前状态");
            _lblSource = Lbl(c1, "—", 16, 32, 200, 32, 18f, Theme.Accent, FontStyle.Bold);
            _lblBadge = Lbl(c1, "检测中", 216, 38, 176, 22, 8.5f, Theme.Dim, FontStyle.Bold);
            _lblBadge.TextAlign = ContentAlignment.MiddleRight;
            _lblBattery = Lbl(c1, "", 16, 68, 376, 16, 8f, Theme.Dim, FontStyle.Regular);
            _lblLimits = Lbl(c1, "", 16, 86, 376, 16, 8f, Theme.Dim, FontStyle.Regular);
            _lblEngine = Lbl(c1, "", 16, 104, 300, 16, 8f, Theme.Dim, FontStyle.Regular);
            Btn(c1, "…", 354, 100, 36, 24, delegate { ChooseDllDir(); });
            _lblAction = Lbl(c1, "", 16, 128, 376, 18, 8f, Theme.Text, FontStyle.Regular);
            _lblPower = Lbl(c1, "", 16, 152, 376, 20, 8.5f, Theme.Dim, FontStyle.Regular);
            try { double _p, _c; Rapl.ReadBoth(out _p, out _c); System.Threading.Thread.Sleep(80); Rapl.ReadBoth(out _p, out _c); } catch { }
            

            Card c2 = AddCard(16, 280, 408, 152, "运行模式");
            _chkAuto = Chk(c2, "自动切换（跟随供电）", 16, 30, 340, OnAutoChanged);
            _chkManual = Chk(c2, "手动模式（固定瓦数，托盘菜单可调）", 16, 54, 360, OnManualChanged);
            Lbl(c2, "供电识别", 16, 92, 70, 20, 8.5f, Theme.Dim, FontStyle.Regular);
            string[] srcNames = new string[] { "自动", "电池", "USB-C", "DC-IN" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                _srcBtns[i] = Btn(c2, srcNames[i], 92 + i * 76, 88, 70, 26, delegate { PickSource(idx); });
                _srcBtns[i].Font = F(8f, FontStyle.Regular);
            }

            Card c3 = AddCard(16, 448, 408, 254, "按键与系统 / 亮度与声音");
            Lbl(c3, "TURBO 键", 16, 32, 80, 20, 8.5f, Theme.Dim, FontStyle.Regular);
            _lblBinding = Lbl(c3, "Ctrl+Win+LMenu", 100, 32, 220, 20, 8.5f, Theme.Text, FontStyle.Regular);

            Lbl(c3, "自定义唤醒", 16, 62, 80, 20, 8.5f, Theme.Dim, FontStyle.Regular);
            _lblCustom = Lbl(c3, "未设置", 100, 62, 168, 20, 8.5f, Theme.Text, FontStyle.Regular);
            Btn(c3, "学习", 268, 58, 60, 26, delegate { BeginLearn(true); });
            Btn(c3, "清除", 334, 58, 58, 26, delegate
            {
                _app.Cfg.CustomVk = 0; _app.Cfg.CustomScan = 0;
                _app.Cfg.CustomMods = 0; _app.Cfg.CustomName = "";
                _app.Save(); SyncFromSettings();
            });

            _btnGameAuto = Btn(c2, "游戏模式：关", 16, 120, 376, 26, delegate { ToggleGameAuto(); });
            _chkStartup = Chk(c3, "开机自动启动（计划任务）", 16, 100, 340, OnStartupChanged);
            _chkTray = Chk(c3, "关闭窗口时最小化到托盘", 16, 126, 340, OnTrayChanged);

            Lbl(c3, "亮度", 16, 160, 70, 20, 8.5f, Theme.Dim, FontStyle.Regular);
            _trkBright = Trk(c3, 92, 154, 200, 0, 100, 50, OnBrightnessChanged);
            _lblBright = Lbl(c3, "—", 304, 160, 80, 20, 9f, Theme.Text, FontStyle.Bold);

            Lbl(c3, "音量", 16, 200, 70, 20, 8.5f, Theme.Dim, FontStyle.Regular);
            _trkVoice = Trk(c3, 92, 194, 200, 0, 100, 50, OnVolumeChanged);
            _lblVoice = Lbl(c3, "—", 304, 200, 80, 20, 9f, Theme.Text, FontStyle.Bold);

            _lblFooter = Lbl(c1, "设置保存在 settings.ini · 需要管理员权限写 SMU", 16, 178, 376, 18, 7.5f, Theme.Dim, FontStyle.Regular);

            // ==================== 档位页 ====================
            Panel host = _cardHost;
            _cardHost = _pageProfile;

            Card p1 = AddCard(16, 8, 408, 104, "供电模式选择");
            _btnBattery = Btn(p1, "电池", 16, 28, 118, 30, delegate { SelectProfile("battery"); });
            _btnTypec = Btn(p1, "USB-C", 140, 28, 118, 30, delegate { SelectProfile("typec"); });
            _btnDcin = Btn(p1, "DC-IN", 264, 28, 118, 30, delegate { SelectProfile("dcin"); });
            _chkCustomSupply = Chk(p1, "自定义供电（优先于三个功率模式）", 16, 66, 376, OnCustomSupplyChanged);

            Card p2 = AddCard(16, 120, 408, 252, "");
            _lblProfileTitle = Lbl(p2, "DC-IN 档位", 16, 10, 300, 24, 11f, Theme.Text, FontStyle.Bold);

            Lbl(p2, "加速功耗", 16, 50, 80, 20, 8.5f, Theme.Dim, FontStyle.Regular);
            _trkFast = Trk(p2, 100, 44, 196, 15, 120, 85, OnFastChanged);
            _lblFastValue = Lbl(p2, "85 W", 304, 50, 80, 20, 9f, Theme.Text, FontStyle.Bold);

            Lbl(p2, "持续功耗", 16, 88, 80, 20, 8.5f, Theme.Dim, FontStyle.Regular);
            _trkSlow = Trk(p2, 100, 82, 196, 5, 120, 65, OnSlowChanged);
            _lblSlowValue = Lbl(p2, "65 W", 304, 88, 80, 20, 9f, Theme.Text, FontStyle.Bold);

            Lbl(p2, "温度墙", 16, 126, 80, 20, 8.5f, Theme.Dim, FontStyle.Regular);
            _trkTemp = Trk(p2, 100, 120, 196, 60, 110, 95, OnTempChanged);
            _lblTempValue = Lbl(p2, "95 ℃", 304, 126, 80, 20, 9f, Theme.Text, FontStyle.Bold);

            _applyTimer = new System.Windows.Forms.Timer();
            _applyTimer.Interval = 500;
            _applyTimer.Tick += delegate
            {
                _applyTimer.Stop();
                if (_editing == _app.Source) { _app.ApplyNow("slider"); RefreshStatus(); }
            };

            _chkPs = Chk(p2, "省电模式", 16, 158, 120, OnModeFlagChanged);
            _chkMp = Chk(p2, "性能模式", 150, 158, 120, OnModeFlagChanged);

            Btn(p2, "立即应用", 16, 192, 120, 32, delegate { _app.ApplyNow("button"); });
            Lbl(p2, "加速 = PPT FAST，持续 = PPT SLOW / STAPM；改动即时保存并下发。", 16, 230, 376, 16, 7.5f, Theme.Dim, FontStyle.Regular);

            _cardHost = host;

            // ==================== 灯效页 ====================
            _lightPage = new LightingForm(_app, true);
            _lightPage.TopLevel = false;
            _lightPage.Dock = DockStyle.Top;
            _lightPage.Height = S(440);
            _pageLight.Controls.Add(_lightPage);
            _lightPage.Show();

            int b = Display.GetBrightness();
            if (b >= 0) { _trkBright.Value = Math.Max(0, Math.Min(100, b)); }
            _lblBright.Text = (_trkBright.Value) + "%";
            int vv = Display.GetVolume();
            if (vv >= 0) { _trkVoice.Value = Math.Max(0, Math.Min(100, vv)); }
            _lblVoice.Text = (_trkVoice.Value) + "%";

            BuildCpuPage();
            RefreshWindowButtons();
            _syncing = true;
            if (_chkCustomSupply != null) _chkCustomSupply.Checked = _app.Cfg.CustomSupply;
            RefreshGameAutoButton();
            _syncing = false;
            ApplyBackground();
            SetTab(0);
        }

        private void SetTab(int index)
        {
            if (_pagePower == null) return;
            _pagePower.Visible = (index == 0);
            _pageProfile.Visible = (index == 1);
            _pageLight.Visible = (index == 2);

            Button[] tabs = new Button[] { _tabPower, _tabProfile, _tabLight };
            for (int i = 0; i < tabs.Length; i++)
            {
                bool on = (i == index);
                tabs[i].BackColor = on ? Theme.Accent : Theme.Btn;
                tabs[i].ForeColor = on ? Color.White : Theme.Text;
                tabs[i].FlatAppearance.BorderColor = on ? Theme.Accent : Theme.Border;
            }
        }

        // ---------------------------------------------------------- CPU 亲和性
        private void BuildCpuPage()
        {
            Panel old = _cardHost;
            _cardHost = _pageProfile;

            int n = Cpu.Cores;
            Card c1 = AddCard(16, 380, 408, 212, "CPU 线程");
            Lbl(c1, "取消勾选即不使用该线程，至少保留 1 个", 16, 30, 270, 20, 8f, Theme.Dim, FontStyle.Regular);
            Button btnAll = Btn(c1, "全选", 300, 28, 92, 26, delegate
            {
                SetCpuChecks(Cpu.MaskFor(0));
                SyncRowChecks();
                UpdateCpuInfo();
            });
            btnAll.BackColor = Theme.Btn;
            btnAll.ForeColor = Theme.Text;
            btnAll.FlatAppearance.BorderColor = Theme.Border;
            for (int i = 0; i < n && i < 32; i++)
            {
                int col = i % 8, row = i / 8;
                CheckBox cb = new CheckBox();
                cb.Text = (i + 1).ToString();
                cb.ForeColor = Theme.Text;
                cb.Font = F(8f, FontStyle.Regular);
                cb.AutoSize = false;
                cb.Bounds = new Rectangle(S(16 + col * 38), S(64 + row * 32), S(36), S(26));
                cb.Checked = true;
                cb.CheckedChanged += delegate
                {
                    if (!_rowSyncing && !AnyChecked()) cb.Checked = true;   // 至少保留一个
                    UpdateCpuInfo(); SyncRowChecks(); ArmCpuApply();
                };
                c1.Controls.Add(cb);
                _cpuChecks.Add(cb);
            }
            // 每排末尾的行全选
            for (int row = 0; row < 4; row++)
            {
                int r = row;
                CheckBox rowChk = new CheckBox();
                rowChk.Text = "全选";
                rowChk.ForeColor = Theme.Dim;
                rowChk.Font = F(7.5f, FontStyle.Regular);
                rowChk.AutoSize = false;
                rowChk.Bounds = new Rectangle(S(330), S(64 + r * 32), S(62), S(24));
                rowChk.CheckedChanged += delegate
                {
                    if (_rowSyncing) return;
                    _rowSyncing = true;
                    for (int i = r * 8; i < r * 8 + 8 && i < _cpuChecks.Count; i++)
                        _cpuChecks[i].Checked = rowChk.Checked;
                    _rowSyncing = false;
                    if (!AnyChecked() && _cpuChecks.Count > 0)
                    {
                        _rowSyncing = true;
                        _cpuChecks[r * 8].Checked = true;   // 至少保留一个
                        _rowSyncing = false;
                    }
                    UpdateCpuInfo();
                    SyncRowChecks();
                    ArmCpuApply();
                };
                c1.Controls.Add(rowChk);
                _rowChecks.Add(rowChk);
            }

            Card c2 = AddCard(16, 604, 408, 190, "应用列表");
            Lbl(c2, "每行一个进程名，留空 = 全部进程", 16, 34, 376, 18, 8f, Theme.Dim, FontStyle.Regular);
            _txtCpuApps = new TextBox();
            _txtCpuApps.Multiline = true;
            _txtCpuApps.ScrollBars = ScrollBars.Vertical;
            _txtCpuApps.BorderStyle = BorderStyle.FixedSingle;
            _txtCpuApps.Font = F(8.5f, FontStyle.Regular);
            _txtCpuApps.BackColor = Theme.Card;
            _txtCpuApps.ForeColor = Theme.Text;
            _txtCpuApps.Bounds = new Rectangle(S(16), S(56), S(376), S(70));
            _txtCpuApps.Text = _app.Cfg.CpuApps ?? "";
            _txtCpuApps.TextChanged += delegate { _app.Cfg.CpuApps = _txtCpuApps.Text; _app.Save(); };
            c2.Controls.Add(_txtCpuApps);

            Btn(c2, "应用", 16, 134, 72, 28, delegate { ApplyCpuFromUi(); });
            Btn(c2, "全核", 92, 134, 72, 28, delegate { SetCpuChecks(Cpu.MaskFor(0)); ApplyCpuFromUi(); });
            Btn(c2, "选择应用", 168, 134, 88, 28, delegate { PickApp(); });
            Btn(c2, "导入", 260, 134, 60, 28, delegate { ImportConfig(); });
            Btn(c2, "导出", 324, 134, 60, 28, delegate { ExportConfig(); });
            _lblCpuInfo = Lbl(c2, "", 16, 166, 376, 20, 8f, Theme.Dim, FontStyle.Regular);

            _cardHost = old;
            _syncing = true;

            _syncing = false;
            RefreshCpuUi();
            ApplyCpuCustomState();
        }

        private ulong CurrentCpuMask()
        {
            ulong m = 0;
            for (int i = 0; i < _cpuChecks.Count; i++)
                if (_cpuChecks[i].Checked) m |= 1UL << i;
            if (m == 0) m = 1;   // 至少保留一个线程
            return m;
        }

        private void SetCpuChecks(ulong mask)
        {
            for (int i = 0; i < _cpuChecks.Count; i++)
                _cpuChecks[i].Checked = ((mask >> i) & 1) == 1;
            SyncRowChecks();
        }

        public void TrackForeground()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                uint fpid;
                GetWindowThreadProcessId(fg, out fpid);
                if (fpid != 0 && (int)fpid != Process.GetCurrentProcess().Id) _lastForegroundPid = (int)fpid;
            }
            catch { }
        }

        private static bool IsAlive(int pid)
        {
            try { Process.GetProcessById(pid); return true; } catch { return false; }
        }

        private int ForegroundPid()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                return (int)pid;
            }
            catch { return 0; }
        }

        private void ApplyCpuMask(ulong mask)
        {
            string list = (_app.Cfg.CpuApps ?? "").Trim();
            string err = null;
            int done = 0;
            int targetPid = 0;

            // 有应用列表：只作用于列表内进程；为空：全局（所有可访问进程）
            if (list.Length > 0)
            {
                string[] names = list.Split(new char[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string nm in names)
                {
                    string name = nm.Trim();
                    if (name.Length == 0) continue;
                    int[] pids = Cpu.FindByName(name);
                    foreach (int pid in pids)
                    {
                        string e2 = Cpu.Apply(pid, mask);
                        if (e2 != null) err = e2; else { done++; targetPid = pid; }
                    }
                }
                if (_lblCpuInfo != null)
                    _lblCpuInfo.Text = err != null ? err : ("已应用 " + Cpu.CountBits(mask) + " 线程到 " + done + " 个进程");
                TrayApp.Log("CPU 亲和性: " + Cpu.CountBits(mask) + " 线程 -> " + names.Length + " 个应用, 成功 " + done);
                return;
            }

            // 全局：遍历所有进程
            Cpu.EnableDebugPrivilege();
            _lastGlobalMask = mask;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id <= 4) continue;
                    string e2 = Cpu.Apply(p.Id, mask);
                    if (e2 == null) done++;
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            if (_lblCpuInfo != null)
                _lblCpuInfo.Text = "已全局应用 " + Cpu.CountBits(mask) + " 线程（" + done + " 个进程）";
            TrayApp.Log("CPU 亲和性: 全局 " + Cpu.CountBits(mask) + " 线程, 进程数 " + done);
            return;
        }

        private void OnCpuCustomChanged(object s, EventArgs e)
        {
            if (_syncing) return;

            _app.Save();
            ApplyCpuCustomState();
        }

        private void ApplyCpuCustomState()
        {
            foreach (CheckBox cb in _cpuChecks)
            {
                cb.Enabled = true;
                cb.ForeColor = Theme.Text;
            }
            if (!string.IsNullOrEmpty(_app.Cfg.PowerMode) && _lblCpuInfo != null)
                _lblCpuInfo.Text = "已选功率模式：" + _app.Cfg.PowerMode + "，线程以模式为准";
            RefreshCpuModeButtons();
        }
        private void OnCustomSupplyChanged(object s, EventArgs e)
        {
            if (_syncing) return;
            _app.Cfg.CustomSupply = _chkCustomSupply.Checked;
            _app.Save();
            _app.ApplyNow("custom-supply");
        }

        private void OnOneClickChanged(object s, EventArgs e)
        {
            if (_syncing) return;
            bool on = false;
            _app.Cfg.CpuCustom = on;
            _app.Cfg.CustomSupply = on;
            _app.Save();
            if (_chkCustomSupply != null) { _syncing = true; _chkCustomSupply.Checked = on; _syncing = false; }
            ApplyCpuCustomState();
            _app.ApplyNow("one-click");
        }

        private void ToggleSmartClose()
        {
            _app.Cfg.SmartClose = !_app.Cfg.SmartClose;
            _app.Save();
            RefreshWindowButtons();
        }

        private void ToggleTopMost()
        {
            _app.Cfg.AlwaysOnTop = !_app.Cfg.AlwaysOnTop;
            _app.Save();
            TopMost = _app.Cfg.AlwaysOnTop;
            DoubleBuffered = true;
            RefreshWindowButtons();
        }

        private void RefreshWindowButtons()
        {
            if (_btnSmart == null) return;
            _btnSmart.BackColor = _app.Cfg.SmartClose ? Theme.Accent : Theme.Btn;
            _btnSmart.ForeColor = _app.Cfg.SmartClose ? Color.White : Theme.Text;
            _btnTop.BackColor = _app.Cfg.AlwaysOnTop ? Theme.Accent : Theme.Btn;
            _btnTop.ForeColor = _app.Cfg.AlwaysOnTop ? Color.White : Theme.Text;
        }

        public void ApplyBackground()
        {
            try
            {
                string p = _app.Cfg.BgImage;
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                {
                    Image old = BackgroundImage;
                    BackgroundImage = Image.FromFile(p);
                    BackgroundImageLayout = ImageLayout.Stretch;
                    if (old != null) old.Dispose();
                    _pagePower.BackColor = Color.Transparent;
                    _pageProfile.BackColor = Color.Transparent;
                    _pageLight.BackColor = Color.Transparent;
                }
                else
                {
                    Image old = BackgroundImage;
                    BackgroundImage = null;
                    if (old != null) old.Dispose();
                    _pagePower.BackColor = Theme.Bg;
                    _pageProfile.BackColor = Theme.Bg;
                    _pageLight.BackColor = Theme.Bg;
                }
            }
            catch { }
        }
        private void PlaceWindowButtons()
        {
            if (_btnSmart == null || _pagePower == null) return;
            int w = _pagePower.ClientSize.Width;
            int gap = S(8);
            int bw = (w - S(32) - gap * 2) / 3;
            int y = _pagePower.ClientSize.Height - S(36);
            _btnSmart.Bounds = new Rectangle(S(16), y, bw, S(24));
            _btnTop.Bounds = new Rectangle(S(16) + bw + gap, y, bw, S(24));
            _btnQuit.Bounds = new Rectangle(S(16) + (bw + gap) * 2, y, bw, S(24));
        }

        private void ToggleGameAuto()
        {
            _app.Cfg.GameAutoPerf = !_app.Cfg.GameAutoPerf;
            _app.Save();
            RefreshGameAutoButton();
        }

        private void RefreshGameAutoButton()
        {
            if (_btnGameAuto == null) return;
            bool on = _app.Cfg.GameAutoPerf;
            _btnGameAuto.Text = on ? "游戏模式：开（运行列表内应用时自动切性能）" : "游戏模式：关";
            _btnGameAuto.BackColor = on ? Theme.Accent : Theme.Btn;
            _btnGameAuto.ForeColor = on ? Color.White : Theme.Text;
            _btnGameAuto.FlatAppearance.BorderColor = on ? Theme.Accent : Theme.Border;
        }

        private void PickApp()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "程序 (*.exe)|*.exe|所有文件|*.*";
            dlg.Multiselect = true;
            dlg.Title = "选择要限制 CPU 线程的应用";
            if (dlg.ShowDialog() != DialogResult.OK) return;
            StringBuilder sb = new StringBuilder(_txtCpuApps.Text ?? "");
            int added = 0;
            foreach (string f in dlg.FileNames)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (sb.ToString().IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    sb.AppendLine(name);
                    added++;
                }
            }
            _txtCpuApps.Text = sb.ToString().Trim();
            if (_lblCpuInfo != null) _lblCpuInfo.Text = "已添加 " + added + " 个应用到列表";
        }

        private void ExportConfig()
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "配置文件 (*.ini)|*.ini";
            dlg.FileName = "RyzenTuner-config.ini";
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("CpuApps=" + (_app.Cfg.CpuApps ?? "").Replace("\r\n", "|").Replace("\n", "|"));
                sb.AppendLine("CpuThreads=" + _app.Cfg.CpuThreads);
                sb.AppendLine("CpuCustom=" + _app.Cfg.CpuCustom);
                sb.AppendLine("PowerMode=" + _app.Cfg.PowerMode);
                sb.AppendLine("CustomSupply=" + _app.Cfg.CustomSupply);
                sb.AppendLine("BatteryFast=" + _app.Cfg.BatteryFast + "|" + _app.Cfg.BatterySlow + "|" + _app.Cfg.BatteryTctl);
                sb.AppendLine("TypecFast=" + _app.Cfg.TypecFast + "|" + _app.Cfg.TypecSlow + "|" + _app.Cfg.TypecTctl);
                sb.AppendLine("DcinFast=" + _app.Cfg.DcinFast + "|" + _app.Cfg.DcinSlow + "|" + _app.Cfg.DcinTctl);
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                if (_lblCpuInfo != null) _lblCpuInfo.Text = "已导出到 " + Path.GetFileName(dlg.FileName);
            }
            catch (Exception ex) { if (_lblCpuInfo != null) _lblCpuInfo.Text = "导出失败：" + ex.Message; }
        }

        private void ImportConfig()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "配置文件 (*.ini)|*.ini|所有文件|*.*";
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                string[] lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
                string skipped = "";
                int applied = 0;
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "CpuApps":
                            {
                                StringBuilder keep = new StringBuilder();
                                foreach (string nm in v.Replace("|", "\r\n").Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    string name = nm.Trim();
                                    if (name.Length == 0) continue;
                                    if (Installed(name)) keep.AppendLine(name);
                                    else skipped += (skipped.Length > 0 ? ", " : "") + name;
                                }
                                _app.Cfg.CpuApps = keep.ToString().Trim();
                                if (_txtCpuApps != null) _txtCpuApps.Text = _app.Cfg.CpuApps;
                                applied++;
                                break;
                            }
                        case "CpuThreads": { int x; if (int.TryParse(v, out x)) { _app.Cfg.CpuThreads = x; applied++; } break; }
                        case "CpuCustom": { _app.Cfg.CpuCustom = (v == "True"); applied++; break; }
                        case "PowerMode": { _app.Cfg.PowerMode = v; applied++; break; }
                        case "CustomSupply": { _app.Cfg.CustomSupply = (v == "True"); applied++; break; }
                        case "BatteryFast":
                            {
                                string[] a = v.Split('|');
                                _app.Cfg.BatteryFast = IntOf(a[0], _app.Cfg.BatteryFast);
                                if (a.Length > 1) _app.Cfg.BatterySlow = IntOf(a[1], _app.Cfg.BatterySlow);
                                if (a.Length > 2) _app.Cfg.BatteryTctl = IntOf(a[2], _app.Cfg.BatteryTctl);
                                applied++; break;
                            }
                        case "TypecFast":
                            {
                                string[] a = v.Split('|');
                                _app.Cfg.TypecFast = IntOf(a[0], _app.Cfg.TypecFast);
                                if (a.Length > 1) _app.Cfg.TypecSlow = IntOf(a[1], _app.Cfg.TypecSlow);
                                if (a.Length > 2) _app.Cfg.TypecTctl = IntOf(a[2], _app.Cfg.TypecTctl);
                                applied++; break;
                            }
                        case "DcinFast":
                            {
                                string[] a = v.Split('|');
                                _app.Cfg.DcinFast = IntOf(a[0], _app.Cfg.DcinFast);
                                if (a.Length > 1) _app.Cfg.DcinSlow = IntOf(a[1], _app.Cfg.DcinSlow);
                                if (a.Length > 2) _app.Cfg.DcinTctl = IntOf(a[2], _app.Cfg.DcinTctl);
                                applied++; break;
                            }
                    }
                }
                _app.Save();
                _syncing = true;
                if (_chkCustomSupply != null) _chkCustomSupply.Checked = _app.Cfg.CustomSupply;
            RefreshGameAutoButton();

                _syncing = false;
                ApplyCpuCustomState();
                if (_lblCpuInfo != null)
                    _lblCpuInfo.Text = "已导入 " + applied + " 项" + (skipped.Length > 0 ? "，跳过未安装：" + skipped : "");
            }
            catch (Exception ex) { if (_lblCpuInfo != null) _lblCpuInfo.Text = "导入失败：" + ex.Message; }
        }

        private static int IntOf(string s, int def)
        {
            int x;
            return int.TryParse(s, out x) ? x : def;
        }

        private static bool Installed(string name)
        {
            try
            {
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                if (Process.GetProcessesByName(name).Length > 0) return true;
                foreach (string root in new string[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                                       Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                {
                    try { if (Directory.Exists(root)) foreach (string d in Directory.GetDirectories(root, "*" + name + "*", SearchOption.TopDirectoryOnly)) return true; }
                    catch { }
                }
            }
            catch { }
            return false;
        }
        private void ApplyCpuFromUi()
        {
            ApplyCpuMask(CurrentCpuMask());
            UpdateCpuInfo();
        }

        private void ApplyPowerMode(string mode)
        {
            _app.Cfg.PowerMode = (_app.Cfg.PowerMode == mode) ? "" : mode;
            if (_app.Cfg.PowerMode == "eco") { _app.Cfg.CpuThreads = 4; }
            else if (_app.Cfg.PowerMode == "office") { _app.Cfg.CpuThreads = 12; }
            else if (_app.Cfg.PowerMode == "perf") { _app.Cfg.CpuThreads = 0; }
            _app.Save();

            if (_app.Cfg.PowerMode == "")
            {
                // 取消模式：CPU 线程与自定义供电回到最高优先级
                ulong mask2 = CurrentCpuMask();
                _app.Cfg.CpuThreads = Cpu.CountBits(mask2);
                _app.Save();
                ApplyCpuMask(mask2);
                RefreshCpuModeButtons();
                if (_lblCpuInfo != null) _lblCpuInfo.Text = "已取消模式：CPU 线程与自定义供电优先";
                _app.ApplyNow("power-mode-off");
                return;
            }

            ulong mask = Cpu.MaskFor(_app.Cfg.CpuThreads);
            SetCpuChecks(mask);
            ApplyCpuMask(mask);
            _app.ApplyNow("power-mode");
            RefreshCpuModeButtons();
            UpdateCpuInfo();
        }

        private void ApplyCpuMode(string mode, int threads)
        {

            _app.Cfg.CpuMode = mode;
            _app.Cfg.CpuThreads = threads;
            _app.Save();

            if (_app.Cfg.CpuCustom)
            {
                // 自定义线程优先：模式只记状态，不改亲和性
                if (_lblCpuInfo != null) _lblCpuInfo.Text = "自定义线程生效中，功耗页模式不覆盖";
                RefreshCpuModeButtons();
                return;
            }

            ulong mask = Cpu.MaskFor(threads);
            SetCpuChecks(mask);
            ApplyCpuMask(mask);
            RefreshCpuModeButtons();
            UpdateCpuInfo();
        }

        private void RefreshCpuModeButtons()
        {
            Button[] btns = new Button[] { _btnEco, _btnOffice, _btnPerf };
            string[] modes = new string[] { "eco", "office", "perf" };
            for (int i = 0; i < btns.Length; i++)
            {
                if (btns[i] == null) continue;
                bool on = _app.Cfg.PowerMode == modes[i];
                btns[i].BackColor = on ? Theme.Accent : Theme.Btn;
                btns[i].ForeColor = on ? Color.White : Theme.Text;
                btns[i].FlatAppearance.BorderColor = on ? Theme.Accent : Theme.Border;
            }
        }

        public void ReapplyGlobalMask()
        {
            if (_lastGlobalMask == 0) return;
            if (!string.IsNullOrEmpty(_app.Cfg.CpuApps)) return;
            if (!_app.Cfg.CpuCustom) return;
            if ((DateTime.Now - _lastReapply).TotalSeconds < 8) return;
            _lastReapply = DateTime.Now;
            Cpu.EnableDebugPrivilege();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id <= 4) continue;
                    Cpu.Apply(p.Id, _lastGlobalMask);
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
        }

        private void ArmCpuApply()
        {
            if (string.IsNullOrEmpty(_app.Cfg.PowerMode))   // 没有高亮模式时，CPU 线程直接接管
            {
                if (_cpuApplyTimer == null)
                {
                    _cpuApplyTimer = new System.Windows.Forms.Timer();
                    _cpuApplyTimer.Interval = 500;
                    _cpuApplyTimer.Tick += delegate
                    {
                        _cpuApplyTimer.Stop();
                        ulong mask = CurrentCpuMask();
                        _app.Cfg.CpuThreads = Cpu.CountBits(mask);
                        _app.Save();
                        ApplyCpuMask(mask);
                    };
                }
                _cpuApplyTimer.Stop();
                _cpuApplyTimer.Start();
            }
            else if (_lblCpuInfo != null)
            {
                _lblCpuInfo.Text = "已高亮模式，线程由模式决定（取消高亮后由此处接管）";
            }
        }

        private bool AnyChecked()
        {
            foreach (CheckBox cb in _cpuChecks) if (cb.Checked) return true;
            return false;
        }

        private void SyncRowChecks()
        {
            if (_rowSyncing || _rowChecks.Count == 0) return;
            _rowSyncing = true;
            for (int r = 0; r < _rowChecks.Count; r++)
            {
                bool all = true;
                for (int i = r * 8; i < r * 8 + 8 && i < _cpuChecks.Count; i++)
                    if (!_cpuChecks[i].Checked) { all = false; break; }
                _rowChecks[r].Checked = all;
            }
            _rowSyncing = false;
        }

        private void UpdateCpuInfo()
        {
            if (_lblCpuInfo == null) return;
            int on = 0;
            foreach (CheckBox cb in _cpuChecks) if (cb.Checked) on++;
            _lblCpuInfo.Text = "当前 " + on + " / " + Cpu.Cores + " 线程";
        }

        private void RefreshCpuUi()
        {
            if (_cpuChecks.Count == 0) return;
            SetCpuChecks(Cpu.MaskFor(_app.Cfg.CpuThreads));
            RefreshCpuModeButtons();
            UpdateCpuInfo();
        }
        private void ShowTheme()
        {
            _dialogOpen = true;
            try { ShowThemeCore(); } finally { _dialogOpen = false; }
        }

        private void ShowThemeCore()
        {
            using (ThemeForm tf = new ThemeForm())
            {
                if (tf.ShowDialog(this) != DialogResult.OK) return;
                _app.Cfg.ThemeName = tf.SelectedName;
                _app.Cfg.AnimMode = tf.AnimMode;
                _app.Cfg.BgImage = tf.BgImage;
                _app.Cfg.CardOpacity = tf.CardOpacity;
                TrayApp.CfgBg = tf.BgImage;
                Theme.CardAlpha = (int)Math.Round(255.0 * Math.Max(0, Math.Min(100, tf.CardOpacity)) / 100.0);
                TrayApp.CfgAnim = tf.AnimMode;
                if (tf.SelectedName == "custom")
                {
                    _app.Cfg.ThemeBg = Theme.ColorToInt(tf.PickBg);
                    _app.Cfg.ThemeCard = Theme.ColorToInt(tf.PickCard);
                    _app.Cfg.ThemeAccent = Theme.ColorToInt(tf.PickAccent);
                    _app.Cfg.ThemeText = Theme.ColorToInt(tf.PickText);
                    _app.Cfg.ThemeDim = Theme.ColorToInt(tf.PickDim);
                }
                _app.Save();
                Theme.LoadFrom(_app.Cfg);
                ApplyBackground();
                _app.RebuildUi();
            }
        }

        private void ShowLighting()
        {
            _dialogOpen = true;
            try { using (LightingForm lf = new LightingForm(_app)) { lf.ShowDialog(this); } }
            finally { _dialogOpen = false; }
        }

        private void ChooseDllDir()
        {
            using (FolderBrowserDialog fb = new FolderBrowserDialog())
            {
                fb.Description = "选择包含 libryzenadj.dll 和 WinRing0x64.sys 的文件夹";
                fb.SelectedPath = _app.EngineDir();
                if (fb.ShowDialog(this) == DialogResult.OK)
                {
                    _app.Cfg.DllDir = fb.SelectedPath;
                    _app.Save();
                    _app.ReinitEngine();
                    RefreshStatus();
                }
            }
        }

        // ---------------------------------------------------------- 同步
        public void SyncFromSettings()
        {
            _syncing = true;
            Settings c = _app.Cfg;

            _chkAuto.Checked = (c.Mode == "auto");
            _chkManual.Checked = (c.Mode == "manual");


            int idx = 0;
            if (c.SourceOverride == "battery") idx = 1;
            else if (c.SourceOverride == "typec") idx = 2;
            else if (c.SourceOverride == "dcin") idx = 3;
            HiLiteSource(idx);

            _chkStartup.Checked = Autostart.IsEnabled();
            _chkTray.Checked = c.CloseToTray;

            _lblBinding.Text = string.IsNullOrEmpty(c.TurboName) ? "Ctrl+Win+LMenu" : c.TurboName;

            if (_lblCustom != null)
                _lblCustom.Text = string.IsNullOrEmpty(c.CustomName) ? "未设置" : c.CustomName;

            LoadProfileToUi(_editing);
            _syncing = false;
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private void LoadProfileToUi(string key)
        {
            Settings c = _app.Cfg;
            int fast, slow, tctl; bool ps, mp;
            if (key == "battery") { fast = c.BatteryFast; slow = c.BatterySlow; tctl = c.BatteryTctl; ps = c.BatteryPs; mp = c.BatteryMp; }
            else if (key == "typec") { fast = c.TypecFast; slow = c.TypecSlow; tctl = c.TypecTctl; ps = c.TypecPs; mp = c.TypecMp; }
            else { fast = c.DcinFast; slow = c.DcinSlow; tctl = c.DcinTctl; ps = c.DcinPs; mp = c.DcinMp; }

            bool old = _syncing;
            _syncing = true;
            _trkFast.Value = Clamp(fast, 15, 120);
            _trkSlow.Value = Clamp(slow, 5, 120);
            _trkTemp.Value = Clamp(tctl, 60, 110);
            _chkPs.Checked = ps;
            _chkMp.Checked = mp;
            _lblFastValue.Text = fast + " W";
            _lblSlowValue.Text = slow + " W";
            _lblTempValue.Text = tctl + " ℃";
            _lblProfileTitle.Text = TrayApp.SourceText(key) + " 档位";
            HiLite(key);
            _syncing = old;
        }

        private void HiLite(string key)
        {
            Button[] bs = new Button[] { _btnBattery, _btnTypec, _btnDcin };
            string[] keys = new string[] { "battery", "typec", "dcin" };
            for (int i = 0; i < bs.Length; i++)
            {
                if (keys[i] == key) { bs[i].BackColor = Theme.Accent; bs[i].ForeColor = Color.White; }
                else { bs[i].BackColor = Theme.Btn; bs[i].ForeColor = Theme.Text; }
            }
        }

        private void SelectProfile(string key)
        {
            _editing = key;
            LoadProfileToUi(key);
        }

        // ---------------------------------------------------------- 事件
        private void OnAutoChanged(object s, EventArgs e)
        {
            if (_syncing) return;
            _app.Cfg.Mode = _chkAuto.Checked ? "auto" : "fixed";
            if (!_chkAuto.Checked && _app.Cfg.FixedSource == null) _app.Cfg.FixedSource = "dcin";
            _app.Save();
            _app.ApplyNow("auto-check");
        }

        private void OnManualChanged(object s, EventArgs e)
        {
            if (_syncing) return;
            if (_chkManual.Checked) _app.Cfg.Mode = "manual";
            else _app.Cfg.Mode = "auto";
            _app.Save();
            _app.ApplyNow("manual-check");
        }

        private void PickSource(int idx)
        {
            string[] map = new string[] { "auto", "battery", "typec", "dcin" };
            _app.Cfg.SourceOverride = map[idx];
            _app.Save();
            _app.RefreshSource();
            HiLiteSource(idx);
            _app.ApplyNow("override");
        }

        private void HiLiteSource(int idx)
        {
            for (int i = 0; i < _srcBtns.Length; i++)
            {
                if (_srcBtns[i] == null) continue;
                if (i == idx) { _srcBtns[i].BackColor = Theme.Accent; _srcBtns[i].ForeColor = Color.White; }
                else { _srcBtns[i].BackColor = Theme.Btn; _srcBtns[i].ForeColor = Theme.Text; }
            }
        }

        private void OnFastChanged(object s, EventArgs e)
        {
            if (_syncing) return;
            int v = _trkFast.Value;
            _lblFastValue.Text = v + " W";
            if (_trkSlow.Value > v) { _trkSlow.Value = v; }
            SaveProfile();
        }

        private void OnSlowChanged(object s, EventArgs e)
        {
            if (_syncing) return;
            int v = _trkSlow.Value;
            _lblSlowValue.Text = v + " W";
            if (v > _trkFast.Value) { _trkFast.Value = v; }
            SaveProfile();
        }

        private void OnTempChanged(object s, EventArgs e)
        {
            if (_syncing) return;
            _lblTempValue.Text = _trkTemp.Value + " ℃";
            SaveProfile();
        }

        private void OnModeFlagChanged(object s, EventArgs e)
        {
            if (_syncing) return;
            if (_chkPs.Checked && _chkMp.Checked)
            {
                if (s == _chkPs) _chkMp.Checked = false; else _chkPs.Checked = false;
            }
            SaveProfile();
        }

        private void SaveProfile()
        {
            _app.Cfg.SetProfile(_editing, _trkFast.Value, _trkSlow.Value, _trkTemp.Value, _chkPs.Checked, _chkMp.Checked);
            _app.Save();
            _app.NotifyUiChanged();
            // 编辑的是当前供电的档位时，500ms 后自动下发
            if (_applyTimer != null && _editing == _app.Source)
            {
                _applyTimer.Stop();
                _applyTimer.Start();
            }
        }

        private void OnBrightnessChanged(object s, EventArgs e)
        {
            if (_syncing) return;
            _lblBright.Text = _trkBright.Value + "%";
            Display.SetBrightness(_trkBright.Value);
        }

        private void OnVolumeChanged(object s, EventArgs e)
        {
            if (_syncing) return;
            _lblVoice.Text = _trkVoice.Value + "%";
            Display.SetVolume(_trkVoice.Value);
        }

        private void OnStartupChanged(object s, EventArgs e)
        {
            if (_syncing) return;
            _app.ToggleAutostart();
            _syncing = true;
            _chkStartup.Checked = Autostart.IsEnabled();
            _syncing = false;
        }

        private void OnTrayChanged(object s, EventArgs e)
        {
            if (_syncing) return;
            _app.Cfg.CloseToTray = _chkTray.Checked;
            _app.Save();
        }

        // ---------------------------------------------------------- 刷新
        public void RefreshStatus()
        {
            string src = _app.Source;
            _lblSource.Text = TrayApp.SourceText(src);
            if (_lblBadge != null)
            {
                _lblBadge.Text = (_app.Cfg.SourceOverride == "auto") ? "自动识别" : "手动指定";
                _lblBadge.ForeColor = (_app.Cfg.SourceOverride == "auto") ? Theme.Green : Theme.Yellow;
            }
            int pct = Engine.BatteryPercent;
            _lblBattery.Text = pct >= 0 ? ("电池 " + pct + "% · " + (Engine.OnAc ? "外接供电" : "放电中")) : (Engine.OnAc ? "外接供电" : "放电中");

            int f, sl, t; bool ps, mp;
            _app.GetTarget(out f, out sl, out t, out ps, out mp);

            string limits;
            if ((DateTime.Now - _lastLimitsRead).TotalSeconds < 1 && _cachedLimits != null)
            {
                limits = _cachedLimits;
            }
            else
            {
                limits = _app.Eng.ReadLimits();
                _cachedLimits = limits;
                _lastLimitsRead = DateTime.Now;
            }
            _lblLimits.Text = "限制 " + (limits.Length > 0 ? limits : "—") + "  ·  目标 " + f + "/" + sl + "W";
            _lblEngine.Text = _app.Eng.Ready ? "引擎 已连接" : ("引擎 未就绪：" + _app.Eng.Error);
            _lblEngine.ForeColor = _app.Eng.Ready ? Theme.Green : Theme.Red;
            if (_app.Status != _lastStatus) { _lastStatus = _app.Status; _lastLimitsRead = DateTime.MinValue; _cachedLimits = null; }
            if (_lblPower != null)
            {
                double cpu, core;
                Rapl.ReadBoth(out cpu, out core);
                double gpu = Rapl.GpuWatts();
                double bat = Battery.PowerWatts();
                string cpuTxt = cpu >= 0 ? cpu.ToString("F0") + " W" : "—";
                string gpuTxt = gpu >= 0 ? gpu.ToString("F0") + " W" : "—";
                string batTxt = double.IsNaN(bat) ? "—" : (bat < 0 ? "放电 " + Math.Abs(bat).ToString("F1") + " W" : (bat > 0 ? "充电 " + bat.ToString("F1") + " W" : "外接供电"));
                _lblPower.Text = "CPU " + cpuTxt + "　GPU " + gpuTxt + "　电池 " + batTxt;
            }
            _lblAction.Text = _app.Status;
            RefreshCpuModeButtons();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && _app.Cfg.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        // ---------------------------------------------------------- 学习
        public void BeginLearn(bool custom = false)
        {
            _dialogOpen = true;
            try { BeginLearnCore(custom); } finally { _dialogOpen = false; }
        }

        private void BeginLearnCore(bool custom)
        {
            using (LearnForm lf = new LearnForm(_app))
            {
                lf.ForCustom = custom;
                if (lf.ShowDialog(this) == DialogResult.OK)
                {
                    SyncFromSettings();
                }
            }
        }
    }

    // ------------------------------------------------------------------ 主题设置
    internal sealed class ThemeForm : Form
    {
        public string SelectedName = Theme.Name;
        public Color PickBg = Theme.Bg;
        public Color PickCard = Theme.Card;
        public Color PickAccent = Theme.Accent;
        public Color PickText = Theme.Text;
        public Color PickDim = Theme.Dim;
        public string AnimMode = "elegant";
        public string BgImage = "";
        public int CardOpacity = 100;
        public string ThemeSaveName = "";

        private Button[] _presets = new Button[3];
        private Button[] _swatches = new Button[5];
        private readonly float _sc;

        private int S(int v) { return (int)Math.Round(v * _sc); }
        private Rectangle R(int x, int y, int w, int h) { return new Rectangle(S(x), S(y), S(w), S(h)); }

        private void SetPickers(Color bg, Color card, Color accent, Color text, Color dim)
        {
            PickBg = bg; PickCard = card; PickAccent = accent; PickText = text; PickDim = dim;
            Color[] cs = new Color[] { bg, card, accent, text, dim };
            for (int i = 0; i < _swatches.Length && i < cs.Length; i++)
            {
                if (_swatches[i] == null) continue;
                _swatches[i].Text = "#" + cs[i].R.ToString("X2") + cs[i].G.ToString("X2") + cs[i].B.ToString("X2");
                _swatches[i].BackColor = cs[i];
                _swatches[i].ForeColor = cs[i].GetBrightness() > 0.5 ? Color.Black : Color.White;
            }
        }
        public ThemeForm()
        {
            Text = "主题设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            using (Graphics gg = Graphics.FromHwnd(IntPtr.Zero)) _sc = gg.DpiX / 96f;
            ClientSize = new Size(S(520), S(566));
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Microsoft YaHei UI", 9f);

            MkLabel("预设主题", 18, 18);
            string[] names = new string[] { "壹号本红", "午夜蓝", "极简白" };
            string[] keys = new string[] { "onex", "midnight", "light" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                Button b = new Button();
                b.Text = names[i];
                b.Bounds = R(18 + i * 160, 44, 152, 40);
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Theme.Card;
                b.ForeColor = Theme.Text;
                b.FlatAppearance.BorderColor = Theme.Border;
                b.Click += delegate
                {
                    SelectedName = keys[idx];
                    Theme.Apply(keys[idx]);
                    PickBg = Theme.Bg; PickCard = Theme.Card; PickAccent = Theme.Accent;
                    PickText = Theme.Text; PickDim = Theme.Dim;
                    Restyle();
                    HiLite();
                };
                Controls.Add(b);
                _presets[i] = b;
            }

            MkLabel("自定义颜色", 18, 104);
            string[] labels = new string[] { "背景", "卡片", "强调色", "文字", "次要文字" };
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                Label lb = MkLabel(labels[i], 18, 112 + i * 32);
                lb.AutoSize = false;
                lb.Bounds = R(18, 138 + i * 40, 80, 24);
                Button sw = new Button();
                sw.Bounds = R(110, 134 + i * 40, 170, 30);
                sw.FlatStyle = FlatStyle.Flat;
                sw.FlatAppearance.BorderColor = Theme.Border;
                sw.Click += delegate { PickColor(idx); };
                Controls.Add(sw);
                _swatches[i] = sw;
            }

            Label lAnim = new Label();
            lAnim.Text = "弹出方式";
            lAnim.ForeColor = Theme.Dim;
            lAnim.Font = new Font("Microsoft YaHei UI", 8.5f);
            lAnim.Bounds = R(18, 358, 90, 22);
            Controls.Add(lAnim);

            Button bClassic = new Button();
            bClassic.Text = "传统";
            bClassic.Bounds = R(110, 354, 90, 30);
            bClassic.FlatStyle = FlatStyle.Flat;
            bClassic.BackColor = Theme.Btn;
            bClassic.ForeColor = Theme.Text;
            bClassic.FlatAppearance.BorderColor = Theme.Border;
            Controls.Add(bClassic);

            Button bElegant = new Button();
            bElegant.Text = "优雅";
            bElegant.Bounds = R(210, 354, 90, 30);
            bElegant.FlatStyle = FlatStyle.Flat;
            bElegant.BackColor = Theme.Btn;
            bElegant.ForeColor = Theme.Text;
            bElegant.FlatAppearance.BorderColor = Theme.Border;
            Controls.Add(bElegant);

            AnimMode = TrayApp.CfgAnim;
            Action refreshAnim = delegate
            {
                bClassic.BackColor = AnimMode == "classic" ? Theme.Accent : Theme.Btn;
                bClassic.ForeColor = AnimMode == "classic" ? Color.White : Theme.Text;
                bElegant.BackColor = AnimMode == "elegant" ? Theme.Accent : Theme.Btn;
                bElegant.ForeColor = AnimMode == "elegant" ? Color.White : Theme.Text;
            };
            bClassic.Click += delegate { AnimMode = "classic"; refreshAnim(); };
            bElegant.Click += delegate { AnimMode = "elegant"; refreshAnim(); };
            refreshAnim();

            Label lBg = new Label();
            lBg.Text = "背景图";
            lBg.ForeColor = Theme.Dim;
            lBg.Font = new Font("Microsoft YaHei UI", 8.5f);
            lBg.Bounds = R(18, 400, 90, 22);
            Controls.Add(lBg);

            Button bBg = new Button();
            bBg.Text = "选择图片…";
            bBg.Bounds = R(110, 396, 150, 30);
            bBg.FlatStyle = FlatStyle.Flat;
            bBg.BackColor = Theme.Btn;
            bBg.ForeColor = Theme.Text;
            bBg.FlatAppearance.BorderColor = Theme.Border;
            Controls.Add(bBg);

            Button bBgClear = new Button();
            bBgClear.Text = "清除";
            bBgClear.Bounds = R(270, 396, 90, 30);
            bBgClear.FlatStyle = FlatStyle.Flat;
            bBgClear.BackColor = Theme.Btn;
            bBgClear.ForeColor = Theme.Text;
            bBgClear.FlatAppearance.BorderColor = Theme.Border;
            Controls.Add(bBgClear);

            BgImage = TrayApp.CfgBg;
            CardOpacity = Theme.CardAlpha * 100 / 255;
            bBg.Click += delegate
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp";
                if (dlg.ShowDialog() == DialogResult.OK) { BgImage = dlg.FileName; }
            };
            bBgClear.Click += delegate { BgImage = ""; };

            Label lHint = new Label();
            lHint.Text = "建议背景图尺寸 ≥ 880 × 1688（竖版，比例接近窗口）";
            lHint.ForeColor = Theme.Dim;
            lHint.Font = new Font("Microsoft YaHei UI", 7.5f);
            lHint.Bounds = R(18, 486, 480, 18);
            Controls.Add(lHint);

            Label lOp = new Label();
            lOp.Text = "面板不透明度";
            lOp.ForeColor = Theme.Dim;
            lOp.Font = new Font("Microsoft YaHei UI", 8.5f);
            lOp.Bounds = R(18, 446, 100, 22);
            Controls.Add(lOp);

            TrackBar trkOp = new TrackBar();
            trkOp.Minimum = 20;
            trkOp.Maximum = 100;
            trkOp.TickStyle = TickStyle.None;
            trkOp.Value = Math.Max(20, Math.Min(100, CardOpacity));
            trkOp.Bounds = R(120, 440, 250, 30);
            Controls.Add(trkOp);

            Label lOpVal = new Label();
            lOpVal.Text = trkOp.Value + "%";
            lOpVal.ForeColor = Theme.Text;
            lOpVal.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            lOpVal.Bounds = R(380, 446, 80, 22);
            Controls.Add(lOpVal);

            trkOp.ValueChanged += delegate
            {
                CardOpacity = trkOp.Value;
                lOpVal.Text = trkOp.Value + "%";
                Theme.CardAlpha = (int)Math.Round(255.0 * trkOp.Value / 100.0);
                if (Owner != null) Owner.Invalidate(true);
            };

            Button ok = new Button();
            ok.Text = "应用";
            ok.Bounds = R(306, 510, 95, 34);
            ok.FlatStyle = FlatStyle.Flat;
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Color.White;
            ok.FlatAppearance.BorderColor = Theme.Accent;
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Bounds = R(410, 510, 95, 34);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Theme.Btn;
            cancel.ForeColor = Theme.Text;
            cancel.FlatAppearance.BorderColor = Theme.Border;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            Restyle();
            HiLite();
        }

        private Label MkLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Bounds = R(x, y, 200, 20);
            l.ForeColor = Theme.Dim;
            l.Font = new Font("Microsoft YaHei UI", 8.5f);
            Controls.Add(l);
            return l;
        }

        private void PickColor(int idx)
        {
            Color cur = idx == 0 ? PickBg : idx == 1 ? PickCard : idx == 2 ? PickAccent : idx == 3 ? PickText : PickDim;
            using (ColorDialog cd = new ColorDialog())
            {
                cd.Color = cur;
                cd.FullOpen = true;
                if (cd.ShowDialog(this) != DialogResult.OK) return;
                if (idx == 0) PickBg = cd.Color;
                else if (idx == 1) PickCard = cd.Color;
                else if (idx == 2) PickAccent = cd.Color;
                else if (idx == 3) PickText = cd.Color;
                else PickDim = cd.Color;
                SelectedName = "custom";
                Restyle();
                HiLite();
            }
        }

        private void Restyle()
        {
            BackColor = PickBg;
            for (int i = 0; i < 5; i++)
            {
                Color c = i == 0 ? PickBg : i == 1 ? PickCard : i == 2 ? PickAccent : i == 3 ? PickText : PickDim;
                _swatches[i].BackColor = c;
                _swatches[i].Text = ToHex(c);
                _swatches[i].ForeColor = (c.GetBrightness() > 0.55f) ? Color.Black : Color.White;
            }
            foreach (Control c in Controls)
            {
                if (c is Label) c.ForeColor = PickDim;
            }
        }

        private static string ToHex(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        private void HiLite()
        {
            string[] keys = new string[] { "onex", "midnight", "light" };
            for (int i = 0; i < 3; i++)
            {
                bool on = (SelectedName == keys[i]);
                _presets[i].FlatAppearance.BorderSize = on ? 2 : 1;
                _presets[i].FlatAppearance.BorderColor = on ? PickAccent : Theme.Border;
                _presets[i].BackColor = on ? Theme.Mix(PickCard, PickAccent, 0.18) : PickCard;
            }
        }
    }

    // ------------------------------------------------------------------ 按键学习
    internal sealed class LearnForm : Form
    {
        private readonly TrayApp _app;
        private Label _info;
        private Label _hit;
        private Button _ok;
        private int _vk;
        private float _sc;
        private int _scan;
        private string _name;
        private string _kind = "key";
        private int _page;
        private int _usage;
        private int _mods;
        private int _padMask;
        private int _padLast;
        private System.Windows.Forms.Timer _padTimer;

        public LearnForm(TrayApp app)
        {
            _app = app;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _sc = g.DpiX / 96f;

            Text = "学习 TURBO 按键";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(S(460), S(240));
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            _info = new Label();
            _info.Text = "请按一下机身 TURBO 键。\r\n程序会捕获它发出的按键。";
            _info.ForeColor = Theme.Text;
            _info.Font = new Font("Microsoft YaHei UI", 10f);
            _info.Bounds = new Rectangle(S(20), S(20), S(420), S(50));
            Controls.Add(_info);

            _hit = new Label();
            _hit.Text = "等待按键…";
            _hit.ForeColor = Theme.Dim;
            _hit.Font = new Font("Microsoft YaHei UI", 9f);
            _hit.Bounds = new Rectangle(S(20), S(80), S(420), S(50));
            Controls.Add(_hit);

            Label tip = new Label();
            tip.Text = "如果一直没反应，说明该键不是标准键盘事件（可能为厂商 HID），可先用键盘上的任意键绑定。";
            tip.ForeColor = Theme.Dim;
            tip.Font = new Font("Microsoft YaHei UI", 8f);
            tip.Bounds = new Rectangle(S(20), S(130), S(420), S(40));
            Controls.Add(tip);

            _ok = new Button();
            _ok.Text = "使用此按键";
            _ok.Enabled = false;
            _ok.FlatStyle = FlatStyle.Flat;
            _ok.BackColor = Theme.Btn;
            _ok.ForeColor = Theme.Text;
            _ok.Bounds = new Rectangle(S(240), S(190), S(100), S(30));
            _ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(_ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Theme.Btn;
            cancel.ForeColor = Theme.Text;
            cancel.Bounds = new Rectangle(S(350), S(190), S(90), S(30));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            _app.Hook.Learning = true;
            _app.Hook.KeyPressed += OnKey;
            _app.Raw.HidInput += OnHid;
            _padTimer = new System.Windows.Forms.Timer();
            _padTimer.Interval = 100;
            _padTimer.Tick += delegate { PollPad(); };
            _padTimer.Start();
            FormClosed += delegate
            {
                _app.Hook.KeyPressed -= OnKey;
                _app.Raw.HidInput -= OnHid;
                if (_padTimer != null) { _padTimer.Stop(); _padTimer.Dispose(); }
                _app.Hook.Learning = false;
            };
        }

        private void PlaceRight()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = S(440);
            int h = Math.Min(S(1050), wa.Height - S(8));
            ClientSize = new Size(w, Math.Max(S(700), h));
            int left = wa.Right - Width - S(2);
            if (left < wa.Left) left = wa.Left;
            Left = left;
            Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
        }

        private bool _dragging;
        private Point _dragStart;

        private void HeaderMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; }
        }

        private void HeaderMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point p = PointToScreen(e.Location);
            Location = new Point(p.X - _dragStart.X, p.Y - _dragStart.Y);
        }

        private void HeaderMouseUp(object sender, MouseEventArgs e) { _dragging = false; }

        private int S(int v) { return (int)Math.Round(v * _sc); }

        private void PollPad()
        {
            ushort b = Pad.Buttons();
            if (b == 0) { _padLast = 0; return; }
            if (b == _padLast) return;
            _padLast = b;
            _kind = "pad";
            _padMask = b;
            _hit.Text = "检测到手柄按键：" + Pad.Name(b) + "\r\n（组合键请同时按住）";
            _hit.ForeColor = Theme.Green;
            _ok.Enabled = true;
        }

        private void OnHid(int page, int usage)
        {
            _kind = "hid";
            _page = page;
            _usage = usage;
            _name = string.Format("HID {0:X4}:{1:X4}", page, usage);
            _hit.Text = string.Format("检测到 HID 事件：页 0x{0:X4}  用法 0x{1:X4}", page, usage);
            _hit.ForeColor = Theme.Green;
            _ok.Enabled = true;
        }

        private void OnKey(int vk, int scan, string name)
        {
            if (vk == 0x1B) return; // ESC 忽略

            _vk = vk;
            _scan = scan;
            _name = name;
            _mods = KeyHook.ModsFor(vk);
            _hit.Text = string.Format("检测到：{0}{1}\r\nVK = 0x{2:X2}   Scan = 0x{3:X2}", KeyHook.ModsName(_mods), name, vk, scan);
            _hit.ForeColor = Theme.Green;
            _ok.Enabled = true;
        }

        public bool ForCustom;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                if (ForCustom)
                {
                    if (_vk != 0)
                    {
                        _app.Cfg.CustomVk = _vk;
                        _app.Cfg.CustomScan = _scan;
                        _app.Cfg.CustomMods = _mods;
                        _app.Cfg.CustomName = KeyHook.ModsName(_mods) + _name;
                    }
                    _app.Save();
                    base.OnFormClosing(e);
                    return;
                }
                if (_kind == "pad" && _padMask != 0)
                {
                    _app.Cfg.TurboKind = "pad";
                    _app.Cfg.TurboPadMask = _padMask;
                    _app.Cfg.TurboName = "Pad " + Pad.Name((ushort)_padMask);
                }
                else if (_kind == "hid")
                {
                    _app.Cfg.TurboKind = "hid";
                    _app.Cfg.TurboPage = _page;
                    _app.Cfg.TurboUsage = _usage;
                    _app.Cfg.TurboName = _name;
                }
                else if (_vk != 0)
                {
                    _app.Cfg.TurboKind = "key";
                    _app.Cfg.TurboVk = _vk;
                    _app.Cfg.TurboScan = _scan;
                    _app.Cfg.TurboMods = _mods;
                    _app.Cfg.TurboName = KeyHook.ModsName(_mods) + _name;
                }
                _app.Save();
            }
            base.OnFormClosing(e);
        }
    }

    // ------------------------------------------------------------------ 灯效窗口
    internal sealed class LightingForm : Form
    {
        private readonly TrayApp _app;
        private readonly float _sc;
        private CheckBox _chk;
        private ComboBox _cboMode, _cboEffect;
        private Button _btnMode, _btnEffect;

        private void MakeThemedPicker(ComboBox cbo, Button btn)
        {
            btn.TextAlign = ContentAlignment.MiddleLeft;
            btn.Font = F(9f, FontStyle.Regular);
            btn.BackColor = Theme.Btn;
            btn.ForeColor = Theme.Text;
            btn.FlatAppearance.BorderColor = Theme.Border;
            Action sync = delegate { try { btn.Text = cbo.SelectedIndex >= 0 ? cbo.Items[cbo.SelectedIndex].ToString() : ""; } catch { } };
            sync();
            btn.Click += delegate
            {
                ContextMenuStrip m = new ContextMenuStrip();
                m.BackColor = Theme.Card;
                m.ForeColor = Theme.Text;
                m.ShowImageMargin = false;
                for (int i = 0; i < cbo.Items.Count; i++)
                {
                    int idx = i;
                    ToolStripMenuItem it = new ToolStripMenuItem(cbo.Items[i].ToString());
                    it.Checked = (cbo.SelectedIndex == i);
                    it.Click += delegate { cbo.SelectedIndex = idx; sync(); };
                    m.Items.Add(it);
                }
                m.Show(btn, new Point(0, btn.Height));
            };
            cbo.SelectedIndexChanged += delegate { sync(); };
        }

        private Panel _swatch;
        private TrackBar _trkSpeed, _trkBright;
        private Label _lblStatus, _lblSpeedValue, _lblBrightValue;
        private int _r = 255, _g = 0, _b = 0;
        private readonly bool _embedded;

        private static readonly string[] Modes = new string[] { "static", "breathing", "effect" };
        private static readonly string[] ModeNames = new string[] { "常亮（自定义颜色）", "呼吸（自定义颜色）", "内置特效（19 种）" };

        protected override void OnPaint(PaintEventArgs e)
        {
            Form host = TopLevel ? this : (Parent != null ? Parent.FindForm() : null);
            bool withImage = _embedded && host != null && host != this && host.BackgroundImage != null;
            if (withImage)
            {
                try
                {
                    Point origin = host.PointToClient(PointToScreen(Point.Empty));
                    Graphics g = e.Graphics;
                    var st = g.Save();
                    g.TranslateTransform(-origin.X, -origin.Y);
                    g.DrawImage(host.BackgroundImage, new Rectangle(0, 0, host.ClientSize.Width, host.ClientSize.Height));
                    g.Restore(st);
                }
                catch { }
            }
            int alpha = withImage ? Theme.CardAlpha : 255;
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(alpha, Theme.Bg)))
                e.Graphics.FillRectangle(sb, ClientRectangle);
        }
        public LightingForm(TrayApp app, bool embedded = false)
        {
            _embedded = embedded;
            _app = app;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _sc = g.DpiX / 96f;

            Settings c = app.Cfg;
            _r = c.RgbR; _g = c.RgbG; _b = c.RgbB;

            Text = "机身 RGB 灯效";
            if (!_embedded) BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = F(9f, FontStyle.Regular);
            ClientSize = new Size(S(embedded ? 408 : 520), S(embedded ? 430 : 420));
            FormBorderStyle = embedded ? FormBorderStyle.None : FormBorderStyle.FixedDialog;
            ShowInTaskbar = !embedded;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            Icon = TrayApp.MakeIcon();

            Label title = Lbl(this, "机身 RGB 灯效", 24, 16, 300, 30, 14f, Theme.Text, FontStyle.Bold);
            if (embedded) title.Visible = false;

            DrawItemEventHandler comboDraw = delegate(object ds, DrawItemEventArgs de)
            {
                if (de.Index < 0) { using (SolidBrush bb = new SolidBrush(Theme.Card)) de.Graphics.FillRectangle(bb, de.Bounds); return; }
                ComboBox cb = ds as ComboBox;
                bool sel = (de.State & DrawItemState.Selected) != 0;
                using (SolidBrush bg = new SolidBrush(sel ? Theme.Accent : Theme.Card))
                    de.Graphics.FillRectangle(bg, de.Bounds);
                using (SolidBrush fg = new SolidBrush(sel ? Color.White : Theme.Text))
                    de.Graphics.DrawString(cb.Items[de.Index].ToString(), cb.Font, fg, de.Bounds.X + 4, de.Bounds.Y + 2);
            };
            _chk = new CheckBox();
            _chk.BackColor = Color.Transparent;
            _chk.ForeColor = Theme.Text;
            _chk.Text = "启用灯效";
            _chk.ForeColor = Theme.Text;
            _chk.Bounds = new Rectangle(S(24), S(58), S(200), S(24));
            _chk.Checked = c.RgbEnabled;
            _chk.CheckedChanged += delegate { UpdateEnabledState(); };
            Controls.Add(_chk);

            Lbl(this, "模式", 24, 96, 60, 22, 9f, Theme.Dim, FontStyle.Regular);
            _cboMode = new ComboBox();
            _cboMode.FlatStyle = FlatStyle.Flat;
            _cboMode.BackColor = Theme.Card;
            _cboMode.DrawMode = DrawMode.OwnerDrawFixed;
            _cboMode.DrawItem += comboDraw;
            _cboMode.ForeColor = Theme.Text;
            _cboMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboMode.Bounds = new Rectangle(S(96), S(92), S(220), S(26));
            _cboMode.Items.AddRange(ModeNames);
            _cboMode.SelectedIndexChanged += delegate { UpdateEnabledState(); };
            Controls.Add(_cboMode);
            _cboMode.Visible = false;
            _btnMode = new Button();
            _btnMode.FlatStyle = FlatStyle.Flat;
            _btnMode.Bounds = new Rectangle(S(96), S(78), S(240), S(28));
            Controls.Add(_btnMode);
            MakeThemedPicker(_cboMode, _btnMode);

            Lbl(this, "特效", 24, 134, 60, 22, 9f, Theme.Dim, FontStyle.Regular);
            _cboEffect = new ComboBox();
            _cboEffect.FlatStyle = FlatStyle.Flat;
            _cboEffect.BackColor = Theme.Card;
            _cboEffect.DrawMode = DrawMode.OwnerDrawFixed;
            _cboEffect.DrawItem += comboDraw;
            _cboEffect.ForeColor = Theme.Text;
            _cboEffect.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboEffect.Bounds = new Rectangle(S(96), S(130), S(220), S(26));
            for (int i = 1; i <= RgbProtocol.EffectNames.Length; i++)
                _cboEffect.Items.Add(i + "  " + RgbProtocol.EffectName(i));
            Controls.Add(_cboEffect);
            _cboEffect.Visible = false;
            _btnEffect = new Button();
            _btnEffect.FlatStyle = FlatStyle.Flat;
            _btnEffect.Bounds = new Rectangle(S(96), S(118), S(240), S(28));
            Controls.Add(_btnEffect);
            MakeThemedPicker(_cboEffect, _btnEffect);

            Lbl(this, "颜色", 24, 172, 60, 22, 9f, Theme.Dim, FontStyle.Regular);
            _swatch = new Panel();
            _swatch.Bounds = new Rectangle(S(96), S(168), S(60), S(26));
            _swatch.BackColor = Color.FromArgb(_r, _g, _b);
            _swatch.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_swatch);
            Button pick = new Button();
            pick.Text = "选择颜色…";
            pick.FlatStyle = FlatStyle.Flat;
            pick.FlatAppearance.BorderColor = Theme.Border;
            pick.BackColor = Theme.Btn;
            pick.ForeColor = Theme.Text;
            pick.Bounds = new Rectangle(S(164), S(166), S(100), S(28));
            pick.Click += delegate { PickColor(); };
            Controls.Add(pick);

            Lbl(this, "亮度", 24, 212, 60, 22, 9f, Theme.Dim, FontStyle.Regular);
            _trkBright = new TrackBar();
            _trkBright.Minimum = 0; _trkBright.Maximum = 3; _trkBright.TickStyle = TickStyle.None;
            _trkBright.Bounds = new Rectangle(S(96), S(206), S(240), S(32));
            _trkBright.BackColor = Theme.Bg;
            _trkBright.ValueChanged += delegate { _lblBrightValue.Text = _trkBright.Value.ToString(); };
            Controls.Add(_trkBright);
            _lblBrightValue = Lbl(this, "3", 344, 212, 40, 22, 9f, Theme.Text, FontStyle.Bold);

            Lbl(this, "速度", 24, 250, 60, 22, 9f, Theme.Dim, FontStyle.Regular);
            _trkSpeed = new TrackBar();
            _trkSpeed.Minimum = 0; _trkSpeed.Maximum = 9; _trkSpeed.TickStyle = TickStyle.None;
            _trkSpeed.Bounds = new Rectangle(S(96), S(244), S(240), S(32));
            _trkSpeed.BackColor = Theme.Bg;
            _trkSpeed.ValueChanged += delegate { _lblSpeedValue.Text = _trkSpeed.Value.ToString(); };
            Controls.Add(_trkSpeed);
            _lblSpeedValue = Lbl(this, "5", 344, 250, 40, 22, 9f, Theme.Text, FontStyle.Bold);

            _lblStatus = Lbl(this, "", 24, 292, embedded ? 360 : 470, 40, 8f, Theme.Dim, FontStyle.Regular);

            Button apply = new Button();
            apply.Text = "应用";
            apply.FlatStyle = FlatStyle.Flat;
            apply.FlatAppearance.BorderColor = Theme.Accent;
            apply.BackColor = Theme.Accent;
            apply.ForeColor = Color.White;
            apply.Bounds = new Rectangle(S(24), S(344), S(110), S(34));
            apply.Click += delegate { Apply(); };
            Controls.Add(apply);

            Button close = new Button();
            close.Text = "关闭";
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderColor = Theme.Border;
            close.BackColor = Theme.Btn;
            close.ForeColor = Theme.Text;
            close.Bounds = new Rectangle(S(144), S(344), S(100), S(34));
            close.Click += delegate { Close(); };
            Controls.Add(close);
            close.Visible = !embedded;

            // 初始值
            _cboMode.SelectedIndex = Math.Max(0, Array.IndexOf(Modes, c.RgbMode ?? "static"));
            _cboEffect.SelectedIndex = Math.Max(0, Math.Min(18, c.RgbEffect - 1));
            _trkBright.Value = Math.Max(0, Math.Min(3, c.RgbBrightness));
            _trkSpeed.Value = Math.Max(0, Math.Min(9, c.RgbSpeed));
            _lblBrightValue.Text = _trkBright.Value.ToString();
            _lblSpeedValue.Text = _trkSpeed.Value.ToString();
            if (embedded)
            {
                foreach (Control ctl in Controls) ctl.Top -= S(44);
            }
            UpdateEnabledState();
        }

        private void PlaceRight()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = S(440);
            int h = Math.Min(S(1050), wa.Height - S(8));
            ClientSize = new Size(w, Math.Max(S(700), h));
            int left = wa.Right - Width - S(2);
            if (left < wa.Left) left = wa.Left;
            Left = left;
            Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
        }

        private bool _dragging;
        private Point _dragStart;

        private void HeaderMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; }
        }

        private void HeaderMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point p = PointToScreen(e.Location);
            Location = new Point(p.X - _dragStart.X, p.Y - _dragStart.Y);
        }

        private void HeaderMouseUp(object sender, MouseEventArgs e) { _dragging = false; }

        private int S(int v) { return (int)Math.Round(v * _sc); }
        private Font F(float size, FontStyle style) { return new Font("Microsoft YaHei UI", size, style); }

        private Label Lbl(Control parent, string text, int x, int y, int w, int h, float size, Color color, FontStyle style)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = color;
            l.Font = F(size, style);
            l.AutoSize = false;
            l.Bounds = new Rectangle(S(x), S(y), S(w), S(h));
            parent.Controls.Add(l);
            return l;
        }

        private void UpdateEnabledState()
        {
            bool on = _chk.Checked;
            int mode = _cboMode.SelectedIndex;
            bool needColor = (mode == 0 || mode == 1);

            _cboMode.Enabled = on;
            // 特效下拉始终可选：选了就自动切到「内置特效」模式
            _cboEffect.Enabled = on;
            _swatch.Enabled = on && needColor;
            _trkBright.Enabled = on;
            _trkSpeed.Enabled = on;
        }

        private void PickColor()
        {
            using (ColorDialog dlg = new ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.Color = Color.FromArgb(_r, _g, _b);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _r = dlg.Color.R; _g = dlg.Color.G; _b = dlg.Color.B;
                    _swatch.BackColor = dlg.Color;
                }
            }
        }

        private void Apply()
        {
            Settings c = _app.Cfg;
            c.RgbMode = Modes[_cboMode.SelectedIndex];
            c.RgbR = _r; c.RgbG = _g; c.RgbB = _b;
            c.RgbSpeed = _trkSpeed.Value;
            c.RgbBrightness = _trkBright.Value;
            c.RgbEffect = _cboEffect.SelectedIndex + 1;
            _app.Save();

            _app.ApplyLighting(_chk.Checked);
            LightingController lc = _app.Lighting;
            _lblStatus.Text = lc.Status + (string.IsNullOrEmpty(lc.LastError) ? "" : "\r\n" + lc.LastError);
            _lblStatus.ForeColor = lc.Ready ? Theme.Green : Theme.Yellow;
        }
    }
    // ------------------------------------------------------------------ 入口
    internal static class Program
    {
        public const string AppVersion = "1.4.8";
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    try { File.AppendAllText(Path.Combine(Store.Dir, "crash.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + e.ExceptionObject + Environment.NewLine); } catch { }
                };
                Directory.SetCurrentDirectory(Store.Dir);
            }
            catch { }

            string selftest = GetArg(args, "--selftest");
            if (selftest != null) { RunSelfTest(selftest); return; }

            if (Array.IndexOf(args, "--pipealert") >= 0) { VendorPipe.RunStandalone(); return; }

            string shotTheme = GetArg(args, "--shot-theme");
            if (shotTheme != null)
            {
                Application.EnableVisualStyles();
                try { Native.SetProcessDPIAware(); } catch { }
                using (ThemeForm tf = new ThemeForm())
                {
                    tf.Show();
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(300);
                    Application.DoEvents();
                    using (Bitmap bmp = new Bitmap(tf.Width, tf.Height))
                    {
                        tf.DrawToBitmap(bmp, new Rectangle(0, 0, tf.Width, tf.Height));
                        bmp.Save(shotTheme, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    tf.Close();
                }
                return;
            }

            string raplTest = GetArg(args, "--rapl");
            if (raplTest != null)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("ready=" + Rapl.Ready);
                for (int i = 0; i < 6; i++)
                {
                    double pkg, core;
                    Rapl.ReadBoth(out pkg, out core);
                    sb.AppendLine("sample" + i + " pkg=" + (pkg < 0 ? "-" : pkg.ToString("F1")) +
                                  " core=" + (core < 0 ? "-" : core.ToString("F1")) +
                                  " gpu=" + (Rapl.GpuWatts() < 0 ? "-" : Rapl.GpuWatts().ToString("F1")));
                    System.Threading.Thread.Sleep(500);
                }
                File.WriteAllText(raplTest, sb.ToString(), Encoding.UTF8);
                return;
            }

            string cpuTest = GetArg(args, "--cpu-test");
            if (cpuTest != null)
            {
                int threads = 0;
                string th = GetArg(args, "--threads");
                int.TryParse(th, out threads);
                ulong mask = Cpu.MaskFor(threads);
                int[] ids = Cpu.FindByName(cpuTest);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("target=" + cpuTest + " threads=" + Cpu.CountBits(mask) + " pids=" + ids.Length);
                foreach (int pid in ids)
                {
                    string e = Cpu.Apply(pid, mask);
                    sb.AppendLine("  pid=" + pid + " -> " + (e ?? "OK"));
                }
                string outFile = GetArg(args, "--out");
                if (outFile != null) File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
                return;
            }

            string probe = GetArg(args, "--probe");
            if (probe != null) { RunProbe(probe); return; }
            string keylog = GetArg(args, "--keylog");
            if (keylog != null) { RunKeyLog(keylog); return; }

            string srcOut = GetArg(args, "--source");
            if (srcOut != null) { File.WriteAllText(srcOut, "describe=" + PowerSource.Describe() + "\ndetect=" + (PowerSource.Detect() ?? "<null>") + "\n"); return; }

            string rgbApply = GetArg(args, "--rgb-apply");
            if (rgbApply != null) { RunRgbApply(rgbApply); return; }

            string rgbProbe = GetArg(args, "--rgb-probe");
            if (rgbProbe != null) { RunRgbProbe(rgbProbe); return; }

            string rgbTest = GetArg(args, "--rgb-test");
            if (rgbTest != null) { RunRgbTest(rgbTest); return; }

            string shotLight = GetArg(args, "--shot-light");
            if (shotLight != null) { RunShotLight(shotLight); return; }

            string shot = GetArg(args, "--shot");
            if (shot != null)
            {
                string tabArg = GetArg(args, "--tab");
                int ti;
                MainForm.ShotTab = (tabArg != null && int.TryParse(tabArg, out ti)) ? ti : 0;
                RunShot(shot);
                return;
            }

            bool wantTray = Array.IndexOf(args, "--tray") >= 0;
            bool elevated = Array.IndexOf(args, "--elevated") >= 0;

            if (!IsAdmin() && !elevated)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    psi.Arguments = "--elevated" + (wantTray ? " --tray" : "");
                    Process.Start(psi);
                    return;
                }
                catch { }
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\RyzenTuner", out created))
            {
                if (!created)
                {
                    bool signaled = false;
                    try
                    {
                        EventWaitHandle ev = EventWaitHandle.OpenExisting("Local\\RyzenTuner.Show");
                        ev.Set();
                        signaled = true;
                    }
                    catch { }

                    if (!signaled)
                    {
                        MessageBox.Show("RyzenTuner 已经在运行。\r\n如果托盘里找不到图标，请在任务管理器里结束 RyzenTuner.exe 后重试。",
                            "RyzenTuner", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { Native.SetProcessDPIAware(); } catch { }

                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    TrayApp.Log("UI 异常: " + e.Exception.Message);
                };

                try
                {
                    Application.Run(new TrayApp(wantTray || Store.Load().StartMinimized));
                }
                catch (Exception ex)
                {
                    TrayApp.Log("致命异常: " + ex);
                }
            }
        }

        private static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        private static void RunSelfTest(string path)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Action<string> put = delegate(string line)
            {
                sb.AppendLine(line);
                try { File.WriteAllText(path, sb.ToString(), Encoding.UTF8); } catch { }
            };
            int fail = 0;

            Store.PathOverride = Path.Combine(Path.GetTempPath(), "ryzentuner-selftest.ini");
            string backup = Store.FilePath + ".bak";
            bool had = File.Exists(Store.FilePath);
            if (had) File.Copy(Store.FilePath, backup, true);

            try
            {
                Settings s = new Settings();
                s.ManualW = 57;
                s.DcinFast = 88;
                s.TurboKind = "key";
                s.TurboVk = 0x77;
                Store.Save(s);
                Settings l = Store.Load();
                bool ok1 = (l.ManualW == 57 && l.DcinFast == 88 && l.TurboVk == 0x77);
                if (!ok1) fail++;
                sb.AppendLine((ok1 ? "[PASS] " : "[FAIL] ") + "settings roundtrip ManualW=" + l.ManualW + " TurboVk=" + l.TurboVk);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                TrayApp app = new TrayApp(true);
                bool ok2 = app.MenuItemCount >= 8;
                if (!ok2) fail++;
                sb.AppendLine((ok2 ? "[PASS] " : "[FAIL] ") + "tray menu items=" + app.MenuItemCount);

                bool ok3 = app.Raw != null && app.Raw.RegisteredCount >= 1;
                if (!ok3) fail++;
                sb.AppendLine((ok3 ? "[PASS] " : "[FAIL] ") + "rawinput registered=" + (app.Raw == null ? -1 : app.Raw.RegisteredCount));

                string key = app.DesiredKey();
                bool ok4 = key.Length > 5 && key.IndexOf('|') > 0;
                if (!ok4) fail++;
                sb.AppendLine((ok4 ? "[PASS] " : "[FAIL] ") + "target key=" + key);

                bool ok5 = app.Eng.Ready || app.Eng.Error.Length > 0;
                if (!ok5) fail++;
                sb.AppendLine((ok5 ? "[PASS] " : "[FAIL] ") + "engine=" + (app.Eng.Ready ? "Ready" : app.Eng.Error));

                app.Shutdown();
            }
            catch (Exception ex)
            {
                fail++;
                sb.AppendLine("[FAIL] exception: " + ex);
            }
            finally
            {
                if (had) { try { File.Copy(backup, Store.FilePath, true); File.Delete(backup); } catch { } }
                else { try { File.Delete(Store.FilePath); } catch { } }
                Store.PathOverride = null;
            }

            sb.AppendLine("FAILURES=" + fail);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void RunRgbApply(string path)
        {
            StringBuilder sb = new StringBuilder();
            Settings c = Store.Load();
            sb.AppendLine("settings: mode=" + c.RgbMode + " rgb=(" + c.RgbR + "," + c.RgbG + "," + c.RgbB + ") speed=" + c.RgbSpeed + " bright=" + c.RgbBrightness + " effect=" + c.RgbEffect);
            TrayApp app = new TrayApp(true);
            c.RgbEnabled = true;
            app.ApplyLighting(true);
            Thread.Sleep(1200);
            RgbDevice d = new RgbDevice();
            bool ok = d.Open();
            sb.AppendLine("open=" + ok + " err=" + (d.LastError ?? ""));
            RgbState st = ok ? d.GetStatus() : null;
            sb.AppendLine("status=" + (st == null ? "<no reply>" : st.ToString()));
            d.Dispose();
            app.Shutdown();
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void RunRgbProbe(string path)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Action<string> put = delegate(string line)
            {
                sb.AppendLine(line);
                try { File.WriteAllText(path, sb.ToString(), Encoding.UTF8); } catch { }
            };

            byte[] pkt = new byte[65];

            // ---- 方案 A：GEN1 (1A2C:B001) 蓝色 ----
            string p1 = RgbDevice.FindPath("VID_1A2C&PID_B001", "MI_00");
            put("A. gen1 path=" + (p1 ?? "none"));
            if (p1 != null)
            {
                RgbDevice d = new RgbDevice();
                if (d.OpenPath(p1))
                {
                    Array.Clear(pkt, 0, pkt.Length);
                    pkt[0] = 0x07; pkt[1] = 0xFF; pkt[2] = 0xFD;
                    pkt[3] = 0x01; pkt[4] = 0x05; pkt[5] = 0x04;
                    put("A. status=" + d.SendPacket(pkt));

                    Array.Clear(pkt, 0, pkt.Length);
                    pkt[0] = 0x07; pkt[1] = 0xFF; pkt[2] = 0xFE;
                    for (int i = 0; i < 18; i++) { pkt[3 + 3 * i] = 0; pkt[4 + 3 * i] = 0; pkt[5 + 3 * i] = 255; }
                    put("A. color=" + d.SendPacket(pkt));
                }
                d.Dispose();
            }

            System.Threading.Thread.Sleep(3000);

            // ---- 方案 B：GEN2 (1A86:1305 MI_02) 品红 ----
            string p2 = RgbDevice.FindPath("VID_1A86&PID_1305", "MI_02");
            put("B. gen2 path=" + (p2 ?? "none"));
            if (p2 != null)
            {
                RgbDevice d = new RgbDevice();
                if (d.OpenPath(p2))
                {
                    Array.Clear(pkt, 0, pkt.Length);
                    pkt[0] = 0xB8; pkt[1] = 0x3F; pkt[2] = 0x01;
                    pkt[3] = 0xFD; pkt[4] = 0x00; pkt[5] = 0x02;
                    pkt[6] = 0x01; pkt[7] = 0x05; pkt[8] = 0x04;
                    pkt[63] = 0x3F; pkt[64] = 0xB8;
                    put("B. status=" + d.SendPacket(pkt));

                    Array.Clear(pkt, 0, pkt.Length);
                    pkt[0] = 0xB8; pkt[1] = 0x3F; pkt[2] = 0x01;
                    pkt[3] = 0xFE; pkt[4] = 0x00; pkt[5] = 0x02;
                    for (int i = 0; i < 18; i++) { pkt[6 + 3 * i] = 255; pkt[7 + 3 * i] = 0; pkt[8 + 3 * i] = 255; }
                    pkt[63] = 0x3F; pkt[64] = 0xB8;
                    put("B. color=" + d.SendPacket(pkt));
                }
                d.Dispose();
            }

            System.Threading.Thread.Sleep(2000);
            put("done");
        }

        private static void RunRgbTest(string path)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Action<string> put = delegate(string line)
            {
                sb.AppendLine(line);
                try { File.WriteAllText(path, sb.ToString(), Encoding.UTF8); } catch { }
            };
            RgbDevice dev = new RgbDevice();
            try
            {
                bool ok = dev.Open();
                put("open=" + ok);
                put("path=" + (dev.Path ?? "(none)"));
                if (!ok) put("err=" + (dev.LastError ?? ""));
                else
                {
                    RgbState st = dev.GetStatus();
                    put("status=" + (st == null ? "<no reply>" : st.ToString()));
                    if (st != null && st.Raw != null)
                        put("raw=" + BitConverter.ToString(st.Raw, 0, Math.Min(20, st.Raw.Length)));

                    bool s = dev.SetStatus(true, 5, 4);
                    bool c = dev.SetMonoColor(0, 0, 255);
                    put("setColor=" + c + " setStatus=" + s + " err=" + (dev.LastError ?? ""));

                    System.Threading.Thread.Sleep(400);
                    RgbState st2 = dev.GetStatus();
                    put("after=" + (st2 == null ? "<no reply>" : st2.ToString()));
                }
            }
            catch (Exception ex) { put("exception: " + ex.Message); }
            finally { dev.Dispose(); }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void RunShotLight(string path)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Native.SetProcessDPIAware(); } catch { }
            TrayApp app = new TrayApp(true);
            using (LightingForm f = new LightingForm(app))
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, -4000);
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(400);
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            app.Shutdown();
        }

        private static void RunKeyLog(string path)
        {
            StreamWriter sw = new StreamWriter(path, false, Encoding.UTF8);
            sw.AutoFlush = true;
            sw.WriteLine("START " + DateTime.Now.ToString("HH:mm:ss.fff"));
            KeyHook hook = new KeyHook();
            hook.KeyPressed += delegate(int vk, int scan, string name)
            {
                sw.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  vk=0x" + vk.ToString("X2") + " scan=0x" + scan.ToString("X2") + " mods=" + KeyHook.CurrentMods + "  " + name);
            };
            hook.Start();

            // WH_KEYBOARD_LL 必须泵消息才能收到回调，这里用一个隐形窗体维持消息循环
            Form host = new Form();
            host.ShowInTaskbar = false;
            host.FormBorderStyle = FormBorderStyle.None;
            host.StartPosition = FormStartPosition.Manual;
            host.Location = new Point(-32000, -32000);
            host.Size = new Size(1, 1);
            host.Opacity = 0;

            System.Windows.Forms.Timer stop = new System.Windows.Forms.Timer();
            stop.Interval = 90000;
            stop.Tick += delegate { stop.Stop(); Application.Exit(); };
            stop.Start();

            Application.Run(host);

            hook.Dispose();
            sw.WriteLine("END");
            sw.Close();
        }

        private static void RunProbe(string path)
        {
            StringBuilder sb = new StringBuilder();
            Settings c = Store.Load();
            sb.AppendLine("=== RyzenTuner 自检报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("模式=" + c.Mode + "  供电覆盖=" + c.SourceOverride);
            sb.AppendLine("档位: 电池 " + c.BatteryFast + "/" + c.BatterySlow + "/" + c.BatteryTctl +
                          "  TypeC " + c.TypecFast + "/" + c.TypecSlow + "/" + c.TypecTctl +
                          "  DCIN " + c.DcinFast + "/" + c.DcinSlow + "/" + c.DcinTctl);
            sb.AppendLine("性能模式=" + c.PerfActive);

            sb.AppendLine();
            sb.AppendLine("[引擎]");
            Engine eng = new Engine();
            eng.Init(string.IsNullOrEmpty(c.DllDir) ? Store.Dir : c.DllDir);
            sb.AppendLine("ready=" + eng.Ready + (eng.Ready ? "" : "  error=" + eng.Error));
            if (eng.Ready) sb.AppendLine("限制: " + eng.ReadLimits());

            sb.AppendLine();
            sb.AppendLine("[供电]");
            sb.AppendLine("ACLine=" + Engine.OnAc + "  电池=" + Engine.BatteryPercent + "%");
            sb.AppendLine("EC: " + PowerSource.Describe());
            sb.AppendLine("判定: " + (PowerSource.Detect() ?? "<null>"));

            sb.AppendLine();
            sb.AppendLine("[厂商硬件层]");
            bool ct = Process.GetProcessesByName("CompatLayerCT").Length > 0;
            sb.AppendLine("CompatLayerCT 运行=" + ct);
            sb.AppendLine("vendor 目录=" + Directory.Exists(Path.Combine(Store.Dir, "vendor")));

            sb.AppendLine();
            sb.AppendLine("[亮度/音量]");
            sb.AppendLine("亮度=" + Display.GetBrightness() + "%  音量=" + Display.GetVolume() + "%");

            sb.AppendLine();
            sb.AppendLine("[灯效]");
            sb.AppendLine("启用=" + c.RgbEnabled + " 模式=" + c.RgbMode + " 颜色=(" + c.RgbR + "," + c.RgbG + "," + c.RgbB + ")");
            try
            {
                RgbDevice d = new RgbDevice();
                if (d.Open())
                {
                    RgbState st = d.GetStatus();
                    sb.AppendLine("设备: 打开成功  " + (st == null ? "状态无回执" : st.ToString()));
                    d.Dispose();
                }
                else sb.AppendLine("设备: 打开失败 " + d.LastError);
            }
            catch (Exception ex) { sb.AppendLine("设备: 异常 " + ex.Message); }

            sb.AppendLine();
            sb.AppendLine("[按键]");
            sb.AppendLine("TURBO: " + (string.IsNullOrEmpty(c.TurboName) ? "Ctrl+Win+LMenu" : c.TurboName));
            sb.AppendLine("自定义: " + (string.IsNullOrEmpty(c.CustomName) ? "未设置" : c.CustomName));

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
        private static void RunShot(string path)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Native.SetProcessDPIAware(); } catch { }
            MainForm.ShotMode = true;
            TrayApp app = new TrayApp(true);
            app.ShowForm();
            MainForm f = app.Form;
            Application.DoEvents();
            System.Threading.Thread.Sleep(500);
            Application.DoEvents();
            using (Bitmap bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            app.Shutdown();
        }

        private static bool IsAdmin()
        {
            try
            {
                System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
