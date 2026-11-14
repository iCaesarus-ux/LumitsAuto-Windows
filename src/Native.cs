using System;
using System.Runtime.InteropServices;

namespace AutoClicker
{
    /// <summary>Win32 calls for clicking, global hotkeys, timer resolution and DPI awareness.</summary>
    internal static class Native
    {
        public const int WM_HOTKEY = 0x0312;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_NOREPEAT = 0x4000;

        const uint INPUT_MOUSE = 0;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // MOUSEINPUT is the largest member of the native INPUT union, so this layout matches on x86 and x64.
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetCursorPos(out POINT point);

        /// <summary>Current cursor position in screen pixels; false when it can't be read (e.g. the PC is locked).</summary>
        public static bool TryGetCursorPos(out System.Drawing.Point position)
        {
            POINT p;
            bool ok = GetCursorPos(out p);
            position = new System.Drawing.Point(p.X, p.Y);
            return ok;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [StructLayout(LayoutKind.Sequential)]
        struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        static extern bool FlashWindowEx(ref FLASHWINFO info);

        /// <summary>Flashes the window's taskbar button until the window is brought to the front.</summary>
        public static void Flash(IntPtr hwnd)
        {
            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO)),
                hwnd = hwnd,
                dwFlags = 0x3 | 0xC // FLASHW_ALL | FLASHW_TIMERNOFG
            };
            FlashWindowEx(ref info);
        }

        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint uPeriod);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetProcessDPIAware();

        /// <summary>
        /// Per-monitor awareness keeps recorded and replayed coordinates in real screen pixels,
        /// including multi-monitor setups with different scaling. Falls back on older Windows.
        /// </summary>
        public static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (Exception) { }
            try { if (SetProcessDpiAwareness(2) == 0) return; } catch (Exception) { }
            try { SetProcessDPIAware(); } catch (Exception) { }
        }

        static void ButtonFlags(MouseButton button, out uint down, out uint up)
        {
            switch (button)
            {
                case MouseButton.Right: down = MOUSEEVENTF_RIGHTDOWN; up = MOUSEEVENTF_RIGHTUP; break;
                case MouseButton.Middle: down = MOUSEEVENTF_MIDDLEDOWN; up = MOUSEEVENTF_MIDDLEUP; break;
                default: down = MOUSEEVENTF_LEFTDOWN; up = MOUSEEVENTF_LEFTUP; break;
            }
        }

        /// <summary>Presses or releases a mouse button, for clicks whose hold time is controlled by the caller.</summary>
        public static void Button(MouseButton button, bool press)
        {
            uint down, up;
            ButtonFlags(button, out down, out up);
            var inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = press ? down : up;
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>An instant click (press and release in the same moment).</summary>
        public static void Click(MouseButton button, bool doubleClick)
        {
            uint down, up;
            ButtonFlags(button, out down, out up);

            int count = doubleClick ? 4 : 2;
            var inputs = new INPUT[count];
            for (int i = 0; i < count; i++)
            {
                inputs[i].type = INPUT_MOUSE;
                inputs[i].mi.dwFlags = i % 2 == 0 ? down : up;
            }
            SendInput((uint)count, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>Sends an empty mouse event (no movement, no buttons) to check the INPUT layout. Returns true if Windows accepted it.</summary>
        public static bool SelfTest()
        {
            var inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            return SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) == 1;
        }
    }
}
