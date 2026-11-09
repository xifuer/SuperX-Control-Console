using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace RyzenTuner
{
    /// <summary>
    /// 通过 OneXConsole 的 CompatLayerCT 命名管道订阅 TURBO 按键。
    /// 协议：JSON 行  {"id","path","parameters"}  ->  {"id","code","msg","result"}
    /// 事件：  {"id":"__SocketBizEvent","result":"{eventName,eventParams}"}
    /// </summary>
    internal sealed class VendorPipe : IDisposable
    {
        private const string PipeName = "CompatLayerCT";
        private const int Port = 1013;

        private readonly string _dir;
        private NamedPipeClientStream _pipe;
        private StreamWriter _writer;
        private StreamReader _reader;
        private Thread _thread;
        private volatile bool _running;
        private bool _startedLayer;
        private Process _layerProc;
        private bool _lastDown;

        private static void Drain(StreamReader r)
        {
            Thread t = new Thread(delegate()
            {
                try { while (r.ReadLine() != null) { } } catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        public event Action TurboPressed;
        public bool Connected { get { return _pipe != null && _pipe.IsConnected; } }
        public string LastError { get; private set; }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr FindWindow(string cls, string title);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        public const int WmTurbo = 0x8001;

        /// <summary>非提权辅助进程：订阅管道并把按键转发给主窗口。</summary>
        public static void RunStandalone()
        {
            bool created;
            using (System.Threading.Mutex mtx = new System.Threading.Mutex(true, "Local\\RyzenTunerPipe", out created))
            {
                if (!created) return;
                TrayApp.Log("管道助手启动 pid=" + Process.GetCurrentProcess().Id + " 提权=" + IsElevated());
                VendorPipe p = new VendorPipe();
                p.TurboPressed += delegate
                {
                    try
                    {
                        // 用命名事件通知主程序（不依赖窗口句柄，主窗口可能是懒加载的）
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting("Local\\RyzenTunerTurbo"))
                        {
                            ev.Set();
                        }
                    }
                    catch { }
                };
                if (!p.Start()) return;
                System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
            }
        }
        public VendorPipe()
        {
            _dir = LocateLayerDir();
        }

        private static string LocateLayerDir()
        {
            // 查找顺序：设置里指定 → 程序自带 vendor → OneXConsole 安装目录
            System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
            try
            {
                Settings cfg = Store.Load();
                if (cfg != null && !string.IsNullOrEmpty(cfg.VendorDir)) list.Add(cfg.VendorDir);
            }
            catch { }
            list.Add(System.IO.Path.Combine(Store.Dir, "vendor"));
            list.Add(@"C:\Program Files\OneXConsole\resources\resources");
            list.Add(@"C:\Program Files (x86)\OneXConsole\resources\resources");
            string[] candidates = list.ToArray();
            foreach (string d in candidates)
            {
                if (string.IsNullOrEmpty(d)) continue;
                if (File.Exists(Path.Combine(d, "CompatLayerCT.exe")))
                {
                    TrayApp.Log("硬件层: " + d);
                    return d;
                }
            }
            TrayApp.Log("硬件层: 未找到（TURBO 需要 CompatLayerCT，见 docs/VENDOR.md）");
            return null;
        }

        public bool Available { get { return _dir != null; } }

        private static bool IsElevated()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private void LaunchViaExplorer()
        {
            string vbs = Path.Combine(Path.GetTempPath(), "ryzentuner_ct.vbs");
            string exe = Path.Combine(_dir, "CompatLayerCT.exe");
            string body =
                "Set sh = CreateObject(\"WScript.Shell\")\r\n" +
                "sh.CurrentDirectory = \"" + _dir + "\"\r\n" +
                "sh.Run \"\"\"" + exe + "\"\" " + Port + " OneXConsole 2\", 0, False\r\n";
            File.WriteAllText(vbs, body, Encoding.ASCII);
            Process.Start("explorer.exe", "\"" + vbs + "\"");
            _startedLayer = true;
        }

        public bool Start()
        {
            if (_dir == null) { LastError = "未找到 CompatLayerCT.exe"; return false; }

            // OneXConsole 正在运行时它独占管道，我们不插手
            if (Process.GetProcessesByName("OneXConsole").Length > 0)
            {
                LastError = "OneXConsole 正在运行，由它接管 TURBO";
                return false;
            }

            try
            {
                // 旧实例由主程序负责清理；这里只保证能连上

                if (Process.GetProcessesByName("CompatLayerCT").Length == 0)
                {
                    // 统一经由资源管理器启动（隐藏窗口、无 stdio 重定向），这是唯一验证可用的事件推送方式
                    LaunchViaExplorer();
                    Thread.Sleep(6000);
                    if (false)
                    {
                    ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(_dir, "CompatLayerCT.exe"));
                    psi.Arguments = Port + " OneXConsole 2";
                    psi.WorkingDirectory = _dir;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    Process proc = Process.Start(psi);
                    _layerProc = proc;
                    // 必须用独立线程持续排空输出，否则它写满缓冲区就会退出
                    Drain(proc.StandardOutput);
                    Drain(proc.StandardError);
                    _startedLayer = true;
                    Thread.Sleep(1500);
                    }
                }

                if (!Connect()) { LastError = "管道连接失败"; return false; }
                _running = true;
                _thread = new Thread(Loop);
                _thread.IsBackground = true;
                _thread.Start();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        private bool Connect()
        {
            for (int i = 0; i < 12; i++)
            {
                try
                {
                    _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
                    _pipe.Connect(1500);
                    _writer = new StreamWriter(_pipe, new UTF8Encoding(false));
                    _writer.AutoFlush = true;
                    _reader = new StreamReader(_pipe, Encoding.UTF8);
                    return true;
                }
                catch
                {
                    Thread.Sleep(400);
                }
            }
            return false;
        }

        private string Call(string path, string json)
        {
            if (_writer == null) return null;
            string id = Guid.NewGuid().ToString();
            lock (_writer)
            {
                _writer.Write("{\"id\":\"" + id + "\",\"path\":\"" + path + "\",\"parameters\":" + (json ?? "null") + "}\n");
                _writer.Flush();
            }
            return id;
        }

        private string SendSync(string path)
        {
            if (_writer == null) return "no pipe";
            string id = Guid.NewGuid().ToString();
            lock (_writer)
            {
                _writer.Write("{\"id\":\"" + id + "\",\"path\":\"" + path + "\",\"parameters\":{}}\n");
                _writer.Flush();
            }
            DateTime end = DateTime.Now.AddSeconds(4);
            while (DateTime.Now < end)
            {
                string line = _reader.ReadLine();
                if (line == null) return "closed";
                if (line.IndexOf(id, StringComparison.Ordinal) >= 0) return line;
            }
            return "timeout";
        }

        private void Loop()
        {
            try
            {
                Thread.Sleep(200);
                string r1 = SendSync("/keyboard/quickButtonHookStart/1");
                TrayApp.Log("hookStart -> " + r1);
                Thread.Sleep(200);
                string r2 = SendSync("/func/takeOverTurboButton/true");
                TrayApp.Log("takeOver -> " + r2);

                DateTime lastResend = DateTime.Now;
                while (_running)
                {
                    string line = _reader.ReadLine();
                    if (line == null) { TrayApp.Log("TURBO 订阅中断：管道已关闭"); break; }

                    if (line.IndexOf("__SocketBizEvent", StringComparison.Ordinal) >= 0)
                        TrayApp.Log("管道事件: " + line);

                    if ((DateTime.Now - lastResend).TotalSeconds > 20)
                    {
                        lastResend = DateTime.Now;
                        Call("/func/takeOverTurboButton/true", "{}");
                    }

                    if (line.IndexOf("TurboButtonStatus", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // 参数 "1"=按下 "0"=松开；只认 0→1 的上升沿，避免重复触发
                    bool down = line.IndexOf("\"1\"", StringComparison.Ordinal) >= 0;
                    if (down && !_lastDown)
                    {
                        Action a = TurboPressed;
                        if (a != null) a();
                    }
                    _lastDown = down;
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        public void Dispose()
        {
            _running = false;
            try { Call("/keyboard/quickButtonHookStop", "{}"); } catch { }
            try { Call("/func/takeOverTurboButton/false", "{}"); } catch { }
            try { if (_pipe != null) _pipe.Dispose(); } catch { }
            if (_startedLayer)
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName("CompatLayerCT"))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }
            }
        }
    }
}
