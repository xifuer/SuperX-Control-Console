using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RyzenTuner
{
    /// <summary>读取默认播放设备的峰值电平，用于音乐律动。</summary>
    internal static class AudioPeak
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioMeterInformation
        {
            int GetPeakValue(out float peak);
        }

        private static IAudioMeterInformation _meter;
        private static DateTime _retry = DateTime.MinValue;

        public static float Get()
        {
            try
            {
                if (_meter == null)
                {
                    if ((DateTime.Now - _retry).TotalSeconds < 5) return 0f;
                    IMMDeviceEnumerator en = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
                    IMMDevice dev;
                    if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0) { _retry = DateTime.Now; return 0f; }
                    object o;
                    Guid iid = typeof(IAudioMeterInformation).GUID;
                    if (dev.Activate(ref iid, 1, IntPtr.Zero, out o) != 0) { _retry = DateTime.Now; return 0f; }
                    _meter = (IAudioMeterInformation)o;
                }
                float v;
                if (_meter.GetPeakValue(out v) != 0) { _meter = null; return 0f; }
                if (v < 0) v = 0;
                if (v > 1) v = 1;
                return v;
            }
            catch
            {
                _meter = null;
                _retry = DateTime.Now;
                return 0f;
            }
        }
    }

    internal sealed class LightingController : IDisposable
    {
        private readonly RgbDevice _dev = new RgbDevice();
        private System.Windows.Forms.Timer _timer;
        private DateTime _start = DateTime.Now;
        private float _level;
        private string _mode = "static";
        private byte _r, _g, _b;
        private int _speed, _brightness, _effect;

        public bool Ready { get; private set; }
        public string Status { get; private set; }
        public string LastError { get { return _dev.LastError; } }

        public LightingController()
        {
            Status = "未启用";
        }

        public bool Connect()
        {
            if (Ready) return true;
            if (!_dev.Open())
            {
                Status = _dev.LastError;
                return false;
            }
            Ready = true;
            Status = "已连接 RGB 设备";
            return true;
        }

        public void Configure(bool enabled, string mode, int r, int g, int b, int speed, int brightness, int effect)
        {
            _mode = mode ?? "static";
            _r = (byte)Clamp(r, 0, 255);
            _g = (byte)Clamp(g, 0, 255);
            _b = (byte)Clamp(b, 0, 255);
            _speed = Clamp(speed, 0, 9);
            _brightness = Clamp(brightness, 0, 3);
            _effect = Clamp(effect, 1, 19);
            _start = DateTime.Now;

            if (!enabled)
            {
                if (Ready)
                {
                    _dev.SetStatus(false, 0, 0);
                    Status = "灯效已关闭";
                }
                StopTimer();
                return;
            }

            if (!Connect()) return;

            // 顺序必须与厂商驱动一致：先写状态（含 enabled），再写特效/颜色，
            // 否则 MCU 会用状态里保存的旧特效覆盖新特效。
            bool ok1, ok2;
            if (_mode == "static" || _mode == "breathing" || _mode == "music")
            {
                ok1 = _dev.SetStatus(true, _speed, 3);
                ok2 = _dev.SetMonoColor(_r, _g, _b);
            }
            else
            {
                ok1 = _dev.SetStatus(true, _speed, _brightness);
                ok2 = _dev.SetEffect(_mode == "flow" ? RgbProtocol.EffectFlowing : _effect);
            }

            if (!ok1 || !ok2)
                Status = "写入失败：" + (_dev.LastError ?? "未知错误");
            else
                Status = "已应用：" + ModeName();
            StartTimer();
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public void Dispose() { Shutdown(); }

        public void Shutdown()
        {
            StopTimer();
            if (Ready) { try { _dev.SetStatus(false, 0, 0); } catch { } }
            _dev.Dispose();
            Ready = false;
        }

        private void StartTimer()
        {
            if (_timer == null)
            {
                _timer = new System.Windows.Forms.Timer();
                _timer.Interval = 200;
                _timer.Tick += delegate { Tick(); };
            }
            if (_mode == "breathing" || _mode == "music") _timer.Start();
            else _timer.Stop();
        }

        private void StopTimer()
        {
            if (_timer != null) _timer.Stop();
        }

        private void Tick()
        {
            if (!Ready) return;
            try
            {
                if (_mode == "breathing")
                {
                    // 三角波：约 3 秒一个周期
                    double t = (DateTime.Now - _start).TotalSeconds;
                    double phase = (t % 3.0) / 3.0;
                    double k = phase < 0.5 ? phase * 2 : (1 - phase) * 2;
                    byte s = (byte)(k * 255);
                    _dev.SetMonoColor((byte)(_r * s / 255), (byte)(_g * s / 255), (byte)(_b * s / 255));
                }
                else if (_mode == "music")
                {
                    float p = AudioPeak.Get();
                    if (p > _level) _level = p;
                    else _level *= 0.65f;
                    if (_level < 0.08f) _level = 0.08f;
                    byte s = (byte)(_level * 255);
                    _dev.SetMonoColor((byte)(_r * s / 255), (byte)(_g * s / 255), (byte)(_b * s / 255));
                }
            }
            catch { }
        }

        private string ModeName()
        {
            if (_mode == "static") return "单色";
            if (_mode == "breathing") return "呼吸（自定义色）";
            if (_mode == "flow") return "流彩";
            if (_mode == "music") return "音乐律动";
            return "特效：" + RgbProtocol.EffectName(_effect);
        }
    }
}
