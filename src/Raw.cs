using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RyzenTuner
{
    /// <summary>
    /// 捕获键盘之外的原生输入（消费类控制键、厂商自定义 HID）。
    /// TURBO 按键在部分机型上不会映射成键盘事件，只能通过 Raw Input 看到。
    /// </summary>
    internal sealed class RawInputForm : Form
    {
        private const int WM_INPUT = 0x00FF;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDI_DEVICEINFO = 0x2000000B;
        private const int RIM_TYPEHID = 2;
        private const uint RIDEV_INPUTSINK = 0x00000100;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public int dwType;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public int dwType;
            public int dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RID_DEVICE_INFO_HID
        {
            public int dwVendorId;
            public int dwProductId;
            public int dwVersionNumber;
            public ushort usUsagePage;
            public ushort usUsage;
            public int pad1;
            public int pad2;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct RID_DEVICE_INFO
        {
            [FieldOffset(0)] public int cbSize;
            [FieldOffset(4)] public int dwType;
            [FieldOffset(8)] public RID_DEVICE_INFO_HID hid;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint num, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputDeviceList(IntPtr list, ref uint count, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

        private readonly Dictionary<IntPtr, int[]> _map = new Dictionary<IntPtr, int[]>();
        public int RegisteredCount { get; private set; }
        public event Action<int, int> HidInput;

        public RawInputForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Size = new Size(1, 1);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-4000, -4000);
            CreateHandle();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { Register(); } catch { }
        }

        private void Register()
        {
            List<RAWINPUTDEVICE> list = new List<RAWINPUTDEVICE>();
            HashSet<int> seen = new HashSet<int>();

            // 消费类控制（多媒体键）
            list.Add(MakeDevice(0x0C, 0x01, Handle));
            seen.Add((0x0C << 16) | 0x01);

            uint count = 0;
            uint elemSize = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
            if (GetRawInputDeviceList(IntPtr.Zero, ref count, elemSize) != 0 || count == 0)
            {
                RegisterList(list);
                return;
            }

            IntPtr devBuf = Marshal.AllocHGlobal((int)(count * elemSize));
            try
            {
                uint got = count;
                if (GetRawInputDeviceList(devBuf, ref got, elemSize) != uint.MaxValue)
                {
                    for (int i = 0; i < got; i++)
                    {
                        IntPtr el = (IntPtr)((long)devBuf + i * elemSize);
                        IntPtr hDev = Marshal.ReadIntPtr(el, 0);
                        int type = Marshal.ReadInt32(el, 8);
                        if (type != RIM_TYPEHID) continue;

                        RID_DEVICE_INFO info = new RID_DEVICE_INFO();
                        info.cbSize = Marshal.SizeOf(typeof(RID_DEVICE_INFO));
                        uint size = (uint)info.cbSize;
                        IntPtr buf = Marshal.AllocHGlobal((int)size);
                        try
                        {
                            Marshal.StructureToPtr(info, buf, false);
                            if (GetRawInputDeviceInfo(hDev, RIDI_DEVICEINFO, buf, ref size) == uint.MaxValue) continue;
                            RID_DEVICE_INFO outInfo = (RID_DEVICE_INFO)Marshal.PtrToStructure(buf, typeof(RID_DEVICE_INFO));
                            int page = outInfo.hid.usUsagePage;
                            int usage = outInfo.hid.usUsage;
                            if (page == 0 || usage == 0) continue;

                            bool vendor = page >= 0xFF00;
                            bool consumer = (page == 0x0C);
                            if (!vendor && !consumer) continue;

                            int key = (page << 16) | usage;
                            if (!seen.Add(key)) continue;
                            list.Add(MakeDevice((ushort)page, (ushort)usage, Handle));
                            _map[hDev] = new int[] { page, usage };
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(buf);
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(devBuf);
            }

            RegisterList(list);
        }

        private void RegisterList(List<RAWINPUTDEVICE> list)
        {
            int ok = 0;
            foreach (RAWINPUTDEVICE d in list)
            {
                RAWINPUTDEVICE[] one = new RAWINPUTDEVICE[] { d };
                try
                {
                    if (RegisterRawInputDevices(one, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)))) ok++;
                }
                catch { }
            }
            RegisteredCount = ok;
        }

        private static RAWINPUTDEVICE MakeDevice(ushort page, ushort usage, IntPtr hwnd)
        {
            RAWINPUTDEVICE d = new RAWINPUTDEVICE();
            d.usUsagePage = page;
            d.usUsage = usage;
            d.dwFlags = RIDEV_INPUTSINK;
            d.hwndTarget = hwnd;
            return d;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                try { HandleInput(m.LParam); } catch { }
            }
            base.WndProc(ref m);
        }

        private void HandleInput(IntPtr lParam)
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            if (GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0) return;
            if (size == 0 || size > 4096) return;

            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(lParam, RID_INPUT, buf, ref size, headerSize) == uint.MaxValue) return;
                RAWINPUTHEADER hdr = (RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(RAWINPUTHEADER));
                if (hdr.dwType != RIM_TYPEHID) return;

                int[] sig;
                if (!_map.TryGetValue(hdr.hDevice, out sig)) return;
                Action<int, int> h = HidInput;
                if (h != null) h(sig[0], sig[1]);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _map.Clear(); }
            base.Dispose(disposing);
        }
    }
}
