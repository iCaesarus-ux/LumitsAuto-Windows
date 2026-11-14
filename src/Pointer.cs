using System;

namespace AutoClicker
{
    internal static class Pointer
    {
        public static bool UseHid;
        static int hidFails;

        public static bool MoveTo(int x, int y)
        {
            if (UseHid && HidMouse.IsConnected)
            {
                if (HidMouse.MoveTo(x, y)) { hidFails = 0; return true; }
                if (++hidFails >= 6) UseHid = false;   // the port really is gone
            }
            Native.SetCursorPos(x, y);                 // still land on target
            return true;
        }

        public static void Click(MouseButton b, bool doubleClick)
        {
            if (UseHid && HidMouse.IsConnected && HidMouse.Click(b, doubleClick)) return;
            Native.Click(b, doubleClick);
        }

        public static void Button(MouseButton b, bool press)
        {
            if (UseHid && HidMouse.IsConnected && HidMouse.Button(b, press)) return;
            Native.Button(b, press);
        }

        /// <summary>Teleport, for parking and restoring the pointer only.</summary>
        public static void JumpTo(int x, int y) { Native.SetCursorPos(x, y); }
    }
}