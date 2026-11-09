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
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
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
        private VendorPipe _vpipe;
        private EventWaitHandle _turboEvent;
        private DateTime _lastTurboAt = DateTime.MinValue;
        private DateTime _lastCtrlAt = DateTime.MinValue;
        private DateTime _lastWinAt = DateTime.MinValue;
        public static string CfgAnim = "elegant";
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
            ShowForm();
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
            if (_form != null && !_form.IsDisposed && _form.Visible) _form.RefreshStatus();
            Log(reason + " -> " + _status);
        }

        private void ResolveTarget(out int fast, out int slow, out int tctl, out bool ps, out bool mp)
        {
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
    internal sealed class MainForm : Form
    {
        public static bool ShotMode;
        public static int ShotTab;

        private readonly TrayApp _app;
        private readonly float _sc;

        private Label _lblSource, _lblBadge, _lblBattery, _lblLimits, _lblEngine, _lblAction, _lblBinding, _lblCustom;
        private Label _lblFastValue, _lblSlowValue, _lblTempValue, _lblManualValue, _lblProfileTitle;
        private TrackBar _trkFast, _trkSlow, _trkTemp, _trkManual;
        private CheckBox _chkAuto, _chkManual, _chkPs, _chkMp, _chkStartup, _chkTray;
        private Button[] _srcBtns = new Button[4];
        private Button _btnBattery, _btnTypec, _btnDcin, _btnPerf;
        private Button _tabPower, _tabProfile, _tabLight;
        private Panel _pagePower, _pageProfile, _pageLight, _cardHost;
        private LightingForm _lightPage;
        private string _editing = "dcin";
        private bool _syncing;
        private string _cachedLimits;
        private System.Windows.Forms.Timer _applyTimer;
        private TrackBar _trkBright, _trkVoice;
        private Label _lblBright, _lblVoice;
        private System.Windows.Forms.Timer _avTimer;
        private string _lastStatus;
        private bool _dialogOpen;
        private DateTime _ignoreDeactivateUntil = DateTime.MinValue;
        private System.Windows.Forms.Timer _animTimer;
        private int _animTarget;
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
            TopMost = true;
            ShowInTaskbar = false;
            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 15;
            _animTimer.Tick += delegate { AnimTick(); };
            Icon = TrayApp.MakeIcon();
            Deactivate += delegate
            {
                if (_dialogOpen) return;
                if (DateTime.Now < _ignoreDeactivateUntil) return;
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
            CheckBox c = new CheckBox();
            c.Text = text;
            c.ForeColor = Theme.Text;
            c.Font = F(9f, FontStyle.Regular);
            c.AutoSize = false;
            c.Bounds = new Rectangle(S(x), S(y), S(w), S(22));
            c.CheckedChanged += onChanged;
            parent.Controls.Add(c);
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

        public void ShowAnimated()
        {
            if (IsDisposed) return;
            _ignoreDeactivateUntil = DateTime.Now.AddMilliseconds(500);
            PlaceRight();
            int target = Left;
            if (Elegant)
            {
                Left = Screen.PrimaryScreen.WorkingArea.Right;
                Show();
                AnimateTo(target, null);
            }
            else
            {
                Show();
            }
            try { Activate(); BringToFront(); } catch { }
        }

        public void HideAnimated()
        {
            if (IsDisposed || !Visible) return;
            if (Elegant)
                AnimateTo(Screen.PrimaryScreen.WorkingArea.Right, delegate { Hide(); });
            else
                Hide();
        }

        private void AnimateTo(int targetLeft, Action done)
        {
            _animTarget = targetLeft;
            _animDone = done;
            _animTimer.Stop();
            _animTimer.Start();
        }

        private void AnimTick()
        {
            int diff = _animTarget - Left;
            if (Math.Abs(diff) <= 3)
            {
                Left = _animTarget;
                _animTimer.Stop();
                Action d = _animDone;
                _animDone = null;
                if (d != null) d();
                return;
            }
            int step = Math.Max(18, Math.Abs(diff) / 3);
            Left += diff > 0 ? step : -step;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW: hide from taskbar and Alt+Tab
                return cp;
            }
        }

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

            Label title = Lbl(head, "功耗控制台", 18, 8, 260, 28, 13f, Theme.Text, FontStyle.Bold);
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

            _pageProfile = new Panel();
            _pageProfile.Bounds = _pagePower.Bounds;
            _pageProfile.BackColor = Theme.Bg;
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
            _tabPower = Btn(this, "功耗", 16, 58, 130, 30, delegate { SetTab(0); });
            _tabProfile = Btn(this, "档位", 152, 58, 130, 30, delegate { SetTab(1); });
            _tabLight = Btn(this, "灯效", 288, 58, 136, 30, delegate { SetTab(2); });

            // ==================== 功耗页 ====================
            _btnPerf = Btn(_pagePower, "⚡  一键性能模式", 16, 8, 408, 44, delegate { _app.OneClickPerformance(); RefreshStatus(); });
            _btnPerf.Font = F(11f, FontStyle.Bold);
            _btnPerf.BackColor = Theme.Accent;
            _btnPerf.ForeColor = Color.White;
            _btnPerf.FlatAppearance.BorderColor = Theme.Accent;
            _btnPerf.FlatAppearance.MouseOverBackColor = Theme.Mix(Theme.Accent, Color.White, 0.15);
            _btnPerf.FlatAppearance.MouseDownBackColor = Theme.Mix(Theme.Accent, Color.Black, 0.15);

            Card c1 = AddCard(16, 60, 408, 176, "当前状态");
            _lblSource = Lbl(c1, "—", 16, 32, 200, 32, 18f, Theme.Accent, FontStyle.Bold);
            _lblBadge = Lbl(c1, "检测中", 216, 38, 176, 22, 8.5f, Theme.Dim, FontStyle.Bold);
            _lblBadge.TextAlign = ContentAlignment.MiddleRight;
            _lblBattery = Lbl(c1, "", 16, 68, 376, 16, 8f, Theme.Dim, FontStyle.Regular);
            _lblLimits = Lbl(c1, "", 16, 86, 376, 16, 8f, Theme.Dim, FontStyle.Regular);
            _lblEngine = Lbl(c1, "", 16, 104, 300, 16, 8f, Theme.Dim, FontStyle.Regular);
            Btn(c1, "…", 354, 100, 36, 24, delegate { ChooseDllDir(); });
            _lblAction = Lbl(c1, "", 16, 128, 376, 18, 8f, Theme.Text, FontStyle.Regular);
            Lbl(c1, "按唤醒键开关本窗口（默认 TURBO 的 Ctrl+Win+LMenu）", 16, 150, 376, 16, 7.5f, Theme.Dim, FontStyle.Regular);

            Card c2 = AddCard(16, 244, 408, 118, "运行模式");
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

            Card c3 = AddCard(16, 370, 408, 176, "按键与系统");
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

            _chkStartup = Chk(c3, "开机自动启动（计划任务）", 16, 100, 340, OnStartupChanged);
            _chkTray = Chk(c3, "关闭窗口时最小化到托盘", 16, 126, 340, OnTrayChanged);

            Card c4 = AddCard(16, 554, 408, 118, "亮度与声音");
            Lbl(c4, "亮度", 16, 36, 70, 20, 8.5f, Theme.Dim, FontStyle.Regular);
            _trkBright = Trk(c4, 92, 30, 200, 0, 100, 50, OnBrightnessChanged);
            _lblBright = Lbl(c4, "—", 304, 36, 80, 20, 9f, Theme.Text, FontStyle.Bold);

            Lbl(c4, "音量", 16, 76, 70, 20, 8.5f, Theme.Dim, FontStyle.Regular);
            _trkVoice = Trk(c4, 92, 70, 200, 0, 100, 50, OnVolumeChanged);
            _lblVoice = Lbl(c4, "—", 304, 76, 80, 20, 9f, Theme.Text, FontStyle.Bold);

            Lbl(_pagePower, "设置保存在 settings.ini · 需要管理员权限写 SMU", 18, 686, 380, 18, 7.5f, Theme.Dim, FontStyle.Regular);

            // ==================== 档位页 ====================
            Panel host = _cardHost;
            _cardHost = _pageProfile;

            Card p1 = AddCard(16, 8, 408, 66, "选择档位");
            _btnBattery = Btn(p1, "电池", 16, 28, 118, 30, delegate { SelectProfile("battery"); });
            _btnTypec = Btn(p1, "USB-C", 140, 28, 118, 30, delegate { SelectProfile("typec"); });
            _btnDcin = Btn(p1, "DC-IN", 264, 28, 118, 30, delegate { SelectProfile("dcin"); });

            Card p2 = AddCard(16, 82, 408, 252, "");
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
            InitAvTimer();
            int b = Display.GetBrightness();
            if (b >= 0) { _trkBright.Value = Math.Max(0, Math.Min(100, b)); }
            _lblBright.Text = (_trkBright.Value) + "%";
            int vv = Display.GetVolume();
            if (vv >= 0) { _trkVoice.Value = Math.Max(0, Math.Min(100, vv)); }
            _lblVoice.Text = (_trkVoice.Value) + "%";

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

        private void InitAvTimer()
        {
            // 亮度与音量都实时下发，不再防抖
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
            _lblAction.Text = _app.Status;
            if (_btnPerf != null)
                _btnPerf.Text = _app.Cfg.PerfActive ? "↩  恢复原功耗设置" : "⚡  一键性能模式";
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

        private Button[] _presets = new Button[3];
        private Button[] _swatches = new Button[5];
        private readonly float _sc;

        private int S(int v) { return (int)Math.Round(v * _sc); }
        private Rectangle R(int x, int y, int w, int h) { return new Rectangle(S(x), S(y), S(w), S(h)); }

        public ThemeForm()
        {
            Text = "主题设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            using (Graphics gg = Graphics.FromHwnd(IntPtr.Zero)) _sc = gg.DpiX / 96f;
            ClientSize = new Size(S(520), S(470));
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

            Button ok = new Button();
            ok.Text = "应用";
            ok.Bounds = R(306, 414, 95, 34);
            ok.FlatStyle = FlatStyle.Flat;
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Color.White;
            ok.FlatAppearance.BorderColor = Theme.Accent;
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Bounds = R(410, 414, 95, 34);
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
        private Panel _swatch;
        private TrackBar _trkSpeed, _trkBright;
        private Label _lblStatus, _lblSpeedValue, _lblBrightValue;
        private int _r = 255, _g = 0, _b = 0;
        private readonly bool _embedded;

        private static readonly string[] Modes = new string[] { "static", "breathing", "effect" };
        private static readonly string[] ModeNames = new string[] { "常亮（自定义颜色）", "呼吸（自定义颜色）", "内置特效（19 种）" };

        public LightingForm(TrayApp app, bool embedded = false)
        {
            _embedded = embedded;
            _app = app;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _sc = g.DpiX / 96f;

            Settings c = app.Cfg;
            _r = c.RgbR; _g = c.RgbG; _b = c.RgbB;

            Text = "机身 RGB 灯效";
            BackColor = Theme.Bg;
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

            _chk = new CheckBox();
            _chk.Text = "启用灯效";
            _chk.ForeColor = Theme.Text;
            _chk.Bounds = new Rectangle(S(24), S(58), S(200), S(24));
            _chk.Checked = c.RgbEnabled;
            _chk.CheckedChanged += delegate { UpdateEnabledState(); };
            Controls.Add(_chk);

            Lbl(this, "模式", 24, 96, 60, 22, 9f, Theme.Dim, FontStyle.Regular);
            _cboMode = new ComboBox();
            _cboMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboMode.Bounds = new Rectangle(S(96), S(92), S(220), S(26));
            _cboMode.Items.AddRange(ModeNames);
            _cboMode.SelectedIndexChanged += delegate { UpdateEnabledState(); };
            Controls.Add(_cboMode);

            Lbl(this, "特效", 24, 134, 60, 22, 9f, Theme.Dim, FontStyle.Regular);
            _cboEffect = new ComboBox();
            _cboEffect.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboEffect.Bounds = new Rectangle(S(96), S(130), S(220), S(26));
            for (int i = 1; i <= RgbProtocol.EffectNames.Length; i++)
                _cboEffect.Items.Add(i + "  " + RgbProtocol.EffectName(i));
            Controls.Add(_cboEffect);

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
            _trkBright.ValueChanged += delegate { _lblBrightValue.Text = _trkBright.Value.ToString(); };
            Controls.Add(_trkBright);
            _lblBrightValue = Lbl(this, "3", 344, 212, 40, 22, 9f, Theme.Text, FontStyle.Bold);

            Lbl(this, "速度", 24, 250, 60, 22, 9f, Theme.Dim, FontStyle.Regular);
            _trkSpeed = new TrackBar();
            _trkSpeed.Minimum = 0; _trkSpeed.Maximum = 9; _trkSpeed.TickStyle = TickStyle.None;
            _trkSpeed.Bounds = new Rectangle(S(96), S(244), S(240), S(32));
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
