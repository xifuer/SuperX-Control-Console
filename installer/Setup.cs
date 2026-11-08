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
        public const string Version = "1.0.0";
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
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "/uninstall" || a == "--uninstall") { UninstallInteractive(); return; }
                if (a == "/silent" || a == "/s") silent = true;
                if (a == "/noshortcuts" || a == "/notask") noTask = true;
                if (a == "/norun") noRun = true;
                if (a == "/dir" && i + 1 < args.Length) dir = args[++i];
            }

            if (silent)
            {
                string target = string.IsNullOrEmpty(dir) ? DefaultDir() : dir;
                try { Install(target, !noTask, !noRun); }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.Exit(1); }
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
                        e.ExtractToFile(dest, true);
                    }
                }
                try { File.Delete(tmp); } catch { }
            }

            string exe = Path.Combine(dir, AppExe);
            if (!File.Exists(exe)) throw new Exception("安装包损坏：缺少 " + AppExe);

            // 2) 卸载程序
            string uninstall = Path.Combine(dir, "uninstall.exe");
            try { File.Copy(SetupPath, uninstall, true); } catch { }

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
                "<Triggers><LogonTrigger><Enabled>true</Enabled><Delay>PT20S</Delay></LogonTrigger></Triggers>" +
                "<Principals><Principal id=\"Author\"><GroupId>S-1-5-32-544</GroupId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate>" +
                "<StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>" +
                "<IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>" +
                "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden>" +
                "<RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>{1}</Command><Arguments>--elevated</Arguments></Exec></Actions></Task>",
                AppName, exe);

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

        public static void UninstallInteractive()
        {
            string dir = Path.GetDirectoryName(SetupPath);
            if (MessageBox.Show("确定要卸载 " + AppName + " 吗？\n\n安装目录：" + dir, "卸载",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            try
            {
                foreach (Process p in Process.GetProcessesByName("RyzenTuner"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }

            DeleteTask();

            try
            {
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
                if (File.Exists(lnk)) File.Delete(lnk);
                string sm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
                if (Directory.Exists(sm)) Directory.Delete(sm, true);
            }
            catch { }

            try { Registry.LocalMachine.DeleteSubKeyTree(RegKey, false); } catch { }

            // 自删除：先复制到临时目录再删
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "ryzentuner_uninst.exe");
                File.Copy(SetupPath, tmp, true);
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + dir + "\" /s /q & rmdir /s /q \"" + dir + "\" & del /f /q \"" + tmp + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch { }
            MessageBox.Show("已卸载。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly string _presetDir;
        private TextBox _txtDir;
        private CheckBox _chkStart;
        private CheckBox _chkRun;
        private Button _btnInstall;
        private Label _lblStatus;
        private ProgressBar _bar;

        private static readonly Color Bg = Color.FromArgb(14, 14, 16);
        private static readonly Color Card = Color.FromArgb(23, 23, 26);
        private static readonly Color Text = Color.FromArgb(242, 242, 242);
        private static readonly Color Dim = Color.FromArgb(154, 154, 154);
        private static readonly Color Accent = Color.FromArgb(226, 59, 46);

        public SetupForm(string presetDir)
        {
            _presetDir = presetDir;

            base.Text = "安装 " + Program.AppName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(480, 320);
            BackColor = Bg;
            ForeColor = Text;
            Font = new Font("Microsoft YaHei UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Panel head = new Panel();
            head.Bounds = new Rectangle(0, 0, 480, 78);
            head.BackColor = Accent;
            Controls.Add(head);

            Label t = new Label();
            t.Text = Program.AppName;
            t.ForeColor = Color.White;
            t.Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold);
            t.AutoSize = false;
            t.Bounds = new Rectangle(22, 12, 300, 32);
            head.Controls.Add(t);

            Label sub = new Label();
            sub.Text = "Ryzen AI MAX+ 395 · 壹号本 SUPER X   v" + Program.Version;
            sub.ForeColor = Color.FromArgb(255, 215, 210);
            sub.Font = new Font("Microsoft YaHei UI", 8.5f);
            sub.AutoSize = false;
            sub.Bounds = new Rectangle(22, 46, 420, 20);
            head.Controls.Add(sub);

            AddLabel("安装位置", 22, 96);
            _txtDir = new TextBox();
            _txtDir.Bounds = new Rectangle(22, 120, 360, 26);
            _txtDir.BackColor = Card;
            _txtDir.ForeColor = Text;
            _txtDir.BorderStyle = BorderStyle.FixedSingle;
            _txtDir.Text = string.IsNullOrEmpty(_presetDir) ? Program.DefaultDir() : _presetDir;
            Controls.Add(_txtDir);

            Button browse = MakeButton("浏览…", 390, 119, 68, 28, false);
            browse.Click += delegate
            {
                using (FolderBrowserDialog fb = new FolderBrowserDialog())
                {
                    fb.SelectedPath = _txtDir.Text;
                    if (fb.ShowDialog(this) == DialogResult.OK) _txtDir.Text = Path.Combine(fb.SelectedPath, "RyzenTuner");
                }
            };
            Controls.Add(browse);

            _chkStart = MakeCheck("开机自动启动（计划任务，最高权限）", 22, 164);
            _chkStart.Checked = true;
            Controls.Add(_chkStart);

            _chkRun = MakeCheck("安装完成后立即运行", 22, 192);
            _chkRun.Checked = true;
            Controls.Add(_chkRun);

            _bar = new ProgressBar();
            _bar.Bounds = new Rectangle(22, 232, 436, 6);
            _bar.Style = ProgressBarStyle.Continuous;
            Controls.Add(_bar);

            _lblStatus = new Label();
            _lblStatus.Text = "准备就绪";
            _lblStatus.ForeColor = Dim;
            _lblStatus.Bounds = new Rectangle(22, 244, 300, 20);
            Controls.Add(_lblStatus);

            _btnInstall = MakeButton("安装", 346, 268, 112, 36, true);
            _btnInstall.Click += delegate { DoInstall(); };
            Controls.Add(_btnInstall);
        }

        private Label AddLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = Dim;
            l.Bounds = new Rectangle(x, y, 200, 20);
            Controls.Add(l);
            return l;
        }

        private CheckBox MakeCheck(string text, int x, int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.ForeColor = Text;
            c.Bounds = new Rectangle(x, y, 400, 24);
            return c;
        }

        private Button MakeButton(string text, int x, int y, int w, int h, bool primary)
        {
            Button b = new Button();
            b.Text = text;
            b.Bounds = new Rectangle(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = primary ? Accent : Card;
            b.ForeColor = primary ? Color.White : Text;
            b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(48, 48, 52);
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void DoInstall()
        {
            string dir = _txtDir.Text.Trim();
            if (dir.Length == 0) { MessageBox.Show("请填写安装位置"); return; }

            _btnInstall.Enabled = false;
            _lblStatus.Text = "正在复制文件…";
            _bar.Value = 20;
            Application.DoEvents();

            try
            {
                Program.Install(dir, _chkStart.Checked, _chkRun.Checked);
                _bar.Value = 100;
                _lblStatus.Text = "安装完成";
                MessageBox.Show("安装完成。\n\n桌面和开始菜单已创建快捷方式。", Program.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "安装失败";
                MessageBox.Show("安装失败：" + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnInstall.Enabled = true;
            }
        }
    }
}
