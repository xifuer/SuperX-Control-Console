using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RyzenTunerSetup
{
    internal static class Program
    {
        public const string AppName = "功耗控制台";
        public const string AppExe = "RyzenTuner.exe";
        public const string Version = "1.5.0";
        public const string RegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RyzenTuner";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = false;
            string dir = null;
            bool noTask = false;
            bool noRun = false;
            string shot = null;

            // 双击 uninstall.exe 时直接走卸载，不需要参数
            bool uninstall = Path.GetFileName(SetupPath).Equals("uninstall.exe", StringComparison.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "/uninstall" || a == "--uninstall" || a == "/u") uninstall = true;
                else if (a == "/silent" || a == "/s") silent = true;
                else if (a == "/noshortcuts" || a == "/notask") noTask = true;
                else if (a == "/norun") noRun = true;
                else if (a == "/dir" && i + 1 < args.Length) { dir = args[++i]; i++; }
                else if (a == "/shot" && i + 1 < args.Length) { shot = args[++i]; i++; }
            }

            if (shot != null)
            {
                SetupForm f = new SetupForm(dir);
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                Application.DoEvents();
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
                }
                f.Close();
                return;
            }

            if (uninstall) { Uninstall(silent); return; }

            if (silent)
            {
                string target = string.IsNullOrEmpty(dir) ? DefaultDir() : dir;
                try { Install(target, !noTask, !noRun); }
                catch (Exception ex)
                {
                    try
                    {
                        string log = Path.Combine(Path.GetTempPath(), "ryzentuner_setup_error.log");
                        File.AppendAllText(log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  dir=" + target + Environment.NewLine + ex.ToString() + Environment.NewLine + Environment.NewLine);
                    }
                    catch { }
                    Environment.Exit(1);
                }
                return;
            }

            Application.Run(new SetupForm(dir));
        }

        public static string DefaultDir()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return Path.Combine(pf, "RyzenTuner");
        }

        public static string SetupPath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        public static void Install(string dir, bool autostart, bool launch)
        {
            Directory.CreateDirectory(dir);

            // 0) 先结束正在运行的实例，避免升级时文件被占用
            foreach (string name in new string[] { "RyzenTuner", "CompatLayerCT" })
            {
                try
                {
                    foreach (Process pr in Process.GetProcessesByName(name))
                    {
                        try { pr.Kill(); pr.WaitForExit(3000); } catch { }
                    }
                }
                catch { }
            }
            System.Threading.Thread.Sleep(600);

            // 停止端口驱动服务，释放 WinRing0x64.sys / inpoutx64.dll 的占用
            foreach (string svc in new string[] { "WinRing0_1_2_0", "inpoutx64" })
            {
                try { RunHidden("sc.exe", "stop " + svc); } catch { }
            }
            System.Threading.Thread.Sleep(900);

            // 1) 解包
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload"))
            {
                if (s == null) throw new Exception("安装包损坏：找不到 payload 资源");
                string tmp = Path.Combine(Path.GetTempPath(), "ryzentuner_payload.zip");
                using (FileStream fs = File.Create(tmp)) s.CopyTo(fs);
                using (ZipArchive zip = ZipFile.OpenRead(tmp))
                {
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        string dest = Path.Combine(dir, e.FullName);
                        if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        try { e.ExtractToFile(dest, true); }
                        catch (IOException)
                        {
                            // 文件被占用（如驱动 .sys 正在使用）：已存在就跳过
                            if (!File.Exists(dest)) throw;
                        }
                    }
                }
                try { File.Delete(tmp); } catch { }
            }

            string exe = Path.Combine(dir, AppExe);
            if (!File.Exists(exe)) throw new Exception("安装包损坏：缺少 " + AppExe);

            // 2) 卸载程序
            string uninstall = Path.Combine(dir, "uninstall.exe");
            try { File.Copy(SetupPath, uninstall, true); }
            catch (Exception ex) { throw new Exception("无法写入 uninstall.exe：" + ex.Message); }

            // 3) 快捷方式
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"), exe, dir);
            string sm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
            Directory.CreateDirectory(sm);
            CreateShortcut(Path.Combine(sm, AppName + ".lnk"), exe, dir);

            // 4) 开机自启（可选）
            if (autostart) CreateTask(exe);

            // 5) 注册卸载信息
            using (RegistryKey k = Registry.LocalMachine.CreateSubKey(RegKey))
            {
                k.SetValue("DisplayName", AppName + " (RyzenTuner)");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "壹号本 SUPER X");
                k.SetValue("InstallLocation", dir);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long size = 0;
                try
                {
                    foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        size += new FileInfo(f).Length;
                }
                catch { }
                k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
                k.SetValue("UninstallString", "\"" + uninstall + "\" /uninstall");
            }

            if (launch)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(exe);
                    psi.WorkingDirectory = dir;
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch { }
            }
        }

        public static void CreateShortcut(string lnkPath, string target, string workDir)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { AppName });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch { }
        }

        public static void CreateTask(string exe)
        {
            string xml = string.Format(
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Description>{0}</Description></RegistrationInfo>" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{2}</UserId><Delay>PT2S</Delay></LogonTrigger></Triggers>" +
                "<Principals><Principal id=\"Author\"><UserId>{2}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate>" +
                "<StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>" +
                "<IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>" +
                "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden>" +
                "<RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>{1}</Command><Arguments>--elevated --tray</Arguments></Exec></Actions></Task>",
                AppName, exe, Environment.UserDomainName + "\\" + Environment.UserName);

            string tmp = Path.Combine(Path.GetTempPath(), "ryzentuner_task.xml");
            File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);
            RunHidden("schtasks.exe", "/Create /TN \"RyzenTuner\" /XML \"" + tmp + "\" /F");
            try { File.Delete(tmp); } catch { }
        }

        public static void DeleteTask()
        {
            RunHidden("schtasks.exe", "/Delete /TN \"RyzenTuner\" /F");
        }

        public static void RunHidden(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                Process p = Process.Start(psi);
                p.WaitForExit(15000);
            }
            catch { }
        }

        public static void Uninstall(bool silent)
        {
            string dir = Path.GetDirectoryName(SetupPath);

            // 安全校验：只允许删除确实包含本程序的目录
            if (!File.Exists(Path.Combine(dir, AppExe)))
            {
                if (!silent) MessageBox.Show("当前目录不是 " + AppName + " 的安装位置：\n" + dir, "卸载",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!silent && MessageBox.Show("确定要卸载 " + AppName + " 吗？\n\n安装目录：" + dir, "卸载",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            // 1) 结束正在运行的实例，并等待退出
            try
            {
                foreach (Process p in Process.GetProcessesByName("RyzenTuner"))
                {
                    try { p.Kill(); p.WaitForExit(4000); } catch { }
                }
                System.Threading.Thread.Sleep(600);

            // 停止端口驱动服务，释放 WinRing0x64.sys / inpoutx64.dll 的占用
            foreach (string svc in new string[] { "WinRing0_1_2_0", "inpoutx64" })
            {
                try { RunHidden("sc.exe", "stop " + svc); } catch { }
            }
            System.Threading.Thread.Sleep(900);
            }
            catch { }

            // 2) 计划任务
            DeleteTask();

            // 3) 快捷方式
            try
            {
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
                if (File.Exists(lnk)) File.Delete(lnk);
                string sm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
                if (Directory.Exists(sm)) Directory.Delete(sm, true);
            }
            catch { }

            // 4) 注册表
            try { Registry.LocalMachine.DeleteSubKeyTree(RegKey, false); } catch { }

            // 5) 延迟自删除：用临时批处理等待本进程退出后整目录删除
            try
            {
                string cmdPath = Path.Combine(Path.GetTempPath(), "superx_uninstall_" + Guid.NewGuid().ToString("N") + ".cmd");
                string script =
                    "@echo off\r\n" +
                    "ping 127.0.0.1 -n 3 > nul\r\n" +
                    "rmdir /s /q \"" + dir + "\"\r\n" +
                    "del /f /q \"%~f0\"\r\n";
                File.WriteAllText(cmdPath, script, System.Text.Encoding.Default);

                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"\"" + cmdPath + "\"\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch { }

            if (!silent)
                MessageBox.Show("已卸载。\n\n目录将在几秒后删除完毕。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly string _presetDir;
        private readonly float _sc;
        private TextBox _txtDir;
        private CheckBox _chkStart;
        private CheckBox _chkRun;
        private Button _btnInstall;
        private Label _lblStatus;
        private ProgressBar _bar;

        private static readonly Color Bg = Color.FromArgb(14, 14, 16);
        private static readonly Color CardC = Color.FromArgb(23, 23, 26);
        private static readonly Color Fg = Color.FromArgb(242, 242, 242);
        private static readonly Color DimC = Color.FromArgb(154, 154, 154);
        private static readonly Color Accent = Color.FromArgb(226, 59, 46);

        private int S(int v) { return (int)Math.Round(v * _sc); }
        private Rectangle R(int x, int y, int w, int h) { return new Rectangle(S(x), S(y), S(w), S(h)); }
        private Font Fnt(float size, FontStyle style) { return new Font("Microsoft YaHei UI", size, style); }

        public SetupForm(string presetDir)
        {
            _presetDir = presetDir;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _sc = g.DpiX / 96f;

            base.Text = "安装 " + Program.AppName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(S(520), S(400));
            BackColor = Bg;
            ForeColor = Fg;
            Font = Fnt(9f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Panel head = new Panel();
            head.Bounds = R(0, 0, 520, 84);
            head.BackColor = Accent;
            Controls.Add(head);

            Label t = new Label();
            t.Text = Program.AppName;
            t.ForeColor = Color.White;
            t.Font = Fnt(16f, FontStyle.Bold);
            t.AutoSize = false;
            t.Bounds = R(22, 12, 440, 34);
            head.Controls.Add(t);

            Label sub = new Label();
            sub.Text = "Ryzen AI MAX+ 395 · 壹号本 SUPER X    v" + Program.Version;
            sub.ForeColor = Color.FromArgb(255, 220, 215);
            sub.Font = Fnt(8.5f, FontStyle.Regular);
            sub.AutoSize = false;
            sub.Bounds = R(22, 50, 460, 22);
            head.Controls.Add(sub);

            Label lDir = new Label();
            lDir.Text = "安装位置";
            lDir.ForeColor = DimC;
            lDir.Font = Fnt(8.5f, FontStyle.Regular);
            lDir.Bounds = R(22, 100, 200, 20);
            Controls.Add(lDir);

            _txtDir = new TextBox();
            _txtDir.Bounds = R(22, 124, 380, 28);
            _txtDir.BackColor = CardC;
            _txtDir.ForeColor = Fg;
            _txtDir.BorderStyle = BorderStyle.FixedSingle;
            _txtDir.Font = Fnt(9f, FontStyle.Regular);
            _txtDir.Text = string.IsNullOrEmpty(_presetDir) ? Program.DefaultDir() : _presetDir;
            Controls.Add(_txtDir);

            Button browse = MakeButton("浏览…", 412, 123, 86, 30, false);
            browse.Click += delegate
            {
                using (FolderBrowserDialog fb = new FolderBrowserDialog())
                {
                    fb.SelectedPath = _txtDir.Text;
                    if (fb.ShowDialog(this) == DialogResult.OK) _txtDir.Text = Path.Combine(fb.SelectedPath, "RyzenTuner");
                }
            };
            Controls.Add(browse);

            _chkStart = MakeCheck("开机自动启动（计划任务，最高权限）", 22, 172);
            _chkStart.Checked = true;
            Controls.Add(_chkStart);

            _chkRun = MakeCheck("安装完成后立即运行", 22, 204);
            _chkRun.Checked = true;
            Controls.Add(_chkRun);

            _bar = new ProgressBar();
            _bar.Bounds = R(22, 252, 476, 8);
            _bar.Style = ProgressBarStyle.Continuous;
            Controls.Add(_bar);

            _lblStatus = new Label();
            _lblStatus.Text = "准备就绪";
            _lblStatus.ForeColor = DimC;
            _lblStatus.Font = Fnt(8.5f, FontStyle.Regular);
            _lblStatus.Bounds = R(22, 266, 380, 22);
            Controls.Add(_lblStatus);

            _btnInstall = MakeButton("安装", 378, 330, 120, 40, true);
            _btnInstall.Font = Fnt(10f, FontStyle.Bold);
            _btnInstall.Click += delegate { DoInstall(); };
            Controls.Add(_btnInstall);

            Label hint = new Label();
            hint.Text = "将创建桌面与开始菜单快捷方式，并写入卸载信息。";
            hint.ForeColor = DimC;
            hint.Font = Fnt(8f, FontStyle.Regular);
            hint.Bounds = R(22, 300, 340, 22);
            Controls.Add(hint);
        }

        private CheckBox MakeCheck(string text, int x, int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.ForeColor = Fg;
            c.Font = Fnt(9f, FontStyle.Regular);
            c.Bounds = R(x, y, 470, 26);
            return c;
        }

        private Button MakeButton(string text, int x, int y, int w, int h, bool primary)
        {
            Button b = new Button();
            b.Text = text;
            b.Bounds = R(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = primary ? Accent : CardC;
            b.ForeColor = primary ? Color.White : Fg;
            b.Font = Fnt(9f, primary ? FontStyle.Bold : FontStyle.Regular);
            b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(52, 52, 58);
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void DoInstall()
        {
            string dir = _txtDir.Text.Trim();
            if (dir.Length == 0) { MessageBox.Show(this, "请填写安装位置"); return; }

            _btnInstall.Enabled = false;
            _lblStatus.Text = "正在复制文件…";
            _bar.Value = 20;
            Application.DoEvents();

            try
            {
                Program.Install(dir, _chkStart.Checked, _chkRun.Checked);
                _bar.Value = 100;
                _lblStatus.Text = "安装完成";
                MessageBox.Show(this, "安装完成。\n\n桌面和开始菜单已创建快捷方式。", Program.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "安装失败";
                MessageBox.Show(this, "安装失败：" + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnInstall.Enabled = true;
            }
        }
    }
}
