using System;
using System.Runtime.InteropServices;

namespace RyzenTuner
{
    /// <summary>XInput 手柄按钮读取，用于“手柄组合键”唤醒。</summary>
    internal static class Pad
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct GAMEPAD
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
        public struct STATE
        {
            public uint dwPacketNumber;
            public GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint Get14(uint index, out STATE state);

        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
        private static extern uint Get13(uint index, out STATE state);

        private static int _api = 0; // 0 未定 1 = 1.4 2 = 1.3 3 = 不可用

        public static bool Available
        {
            get { return _api != 3; }
        }

        public static ushort Buttons()
        {
            STATE s;
            if (_api == 1 && Get14(0, out s) == 0) return s.Gamepad.wButtons;
            if (_api == 2 && Get13(0, out s) == 0) return s.Gamepad.wButtons;

            if (_api == 0)
            {
                try
                {
                    if (Get14(0, out s) == 0) { _api = 1; return s.Gamepad.wButtons; }
                    _api = 1;
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }

                try
                {
                    if (Get13(0, out s) == 0) { _api = 2; return s.Gamepad.wButtons; }
                    _api = 2;
                }
                catch (DllNotFoundException) { _api = 3; }
                catch (EntryPointNotFoundException) { _api = 3; }
            }
            return 0;
        }

        public static string Name(ushort mask)
        {
            if (mask == 0) return "无";
            string[] names = new string[]
            {
                "上", "下", "左", "右", "Start", "Back", "L3", "R3",
                "LB", "RB", null, null, "A", "B", "X", "Y"
            };
            string result = "";
            for (int i = 0; i < 16; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                string n = names[i];
                if (n == null) n = "0x" + (1 << i).ToString("X4");
                if (result.Length > 0) result += "+";
                result += n;
            }
            return result;
        }
    }
}
