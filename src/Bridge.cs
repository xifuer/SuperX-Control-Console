using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace RyzenTuner
{
    /// <summary>
    /// 监听 OneXConsole 的快捷面板：它一出现就触发回调。
    /// 用于在没有键盘事件的情况下，让物理 TURBO 键唤醒本程序。
    /// </summary>
    internal sealed class OxPanelWatch : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr h, StringBuilder s, int n);

        private delegate bool EnumProc(IntPtr h, IntPtr l);

        private readonly Timer _timer;
        private readonly HashSet<IntPtr> _visible = new HashSet<IntPtr>();
        private readonly HashSet<uint> _pids = new HashSet<uint>();
        private DateTime _lastFire = DateTime.MinValue;

        public event Action Triggered;
        public bool Enabled = true;

        public OxPanelWatch()
        {
            _timer = new Timer();
            _timer.Interval = 400;
            _timer.Tick += delegate { Poll(); };
        }

        public void Start()
        {
            RefreshPids();
            Seed();           // 把当前已存在的窗口记为“已知”，避免启动就误触发
            _timer.Start();
        }

        private void RefreshPids()
        {
            _pids.Clear();
            foreach (Process p in Process.GetProcessesByName("OneXConsole"))
                _pids.Add((uint)p.Id);
            foreach (Process p in Process.GetProcessesByName("CompatLayerCT"))
                _pids.Add((uint)p.Id);
        }

        private string Describe(IntPtr h)
        {
            StringBuilder tt = new StringBuilder(256);
            GetWindowText(h, tt, 256);
            StringBuilder cc = new StringBuilder(128);
            GetClassName(h, cc, 128);
            return "title=[" + tt + "] cls=" + cc;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);

        private List<IntPtr> Collect()
        {
            List<IntPtr> list = new List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (_pids.Contains(pid))
                {
                    StringBuilder sb = new StringBuilder(128);
                    GetClassName(h, sb, 128);
                    string cls = sb.ToString();
                    // 只看 Electron 的窗口，忽略工具窗口/隐藏宿主
                    if (cls.StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase))
                        list.Add(h);
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        private void Seed()
        {
            _visible.Clear();
            foreach (IntPtr h in Collect()) _visible.Add(h);
        }

        private void Poll()
        {
            if (!Enabled) return;
            RefreshPids();
            List<IntPtr> now = Collect();

            IntPtr newHandle = IntPtr.Zero;
            foreach (IntPtr h in now)
            {
                if (!_visible.Contains(h)) { newHandle = h; break; }
            }
            bool isNew = (newHandle != IntPtr.Zero);

            // 句柄集合可能被复用：消失的窗口要移除，下次再出现才算新
            _visible.RemoveWhere(delegate(IntPtr h) { return !now.Contains(h); });
            foreach (IntPtr h in now) _visible.Add(h);

            if (!isNew) return;
            if ((DateTime.Now - _lastFire).TotalMilliseconds < 1200) return;
            _lastFire = DateTime.Now;

            TrayApp.Log("桥接触发: " + Describe(newHandle));
            Action a = Triggered;
            if (a != null) a();
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr h, int cmd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr h);

        /// <summary>关闭 OneXConsole 的快捷面板（标题为空的 Electron 窗口），不触碰主窗口。</summary>
        public static int CloseQuickPanels()
        {
            int closed = 0;
            List<uint> pids = new List<uint>();
            foreach (Process p in Process.GetProcessesByName("OneXConsole")) pids.Add((uint)p.Id);

            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (!pids.Contains(pid)) return true;

                StringBuilder cls = new StringBuilder(128);
                GetClassName(h, cls, 128);
                if (!cls.ToString().StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase)) return true;

                // 用隐藏而不是关闭：OneXConsole 必须继续运行才能合成 TURBO 的按键
                ShowWindow(h, 0 /* SW_HIDE */);
                closed++;
                return true;
            }, IntPtr.Zero);

            return closed;
        }
        public void Dispose()
        {
            try { _timer.Stop(); _timer.Dispose(); } catch { }
        }
    }
}
