using System;

namespace AutoClicker
{
    internal static class Pointer
    {
        public static bool UseHid;

        public static bool MoveTo(int x, int y)
        {
            if (UseHid && HidMouse.IsConnected && HidMouse.MoveTo(x, y)) return true;
            if (UseHid) UseHid = false;   // the port went away; finish the run on SendInput
            Native.SetCursorPos(x, y);
            return true;
        }

        public static void Click(MouseButton b, bool doubleClick)
        {
            if (UseHid && HidMouse.IsConnected) HidMouse.Click(b, doubleClick);
            else Native.Click(b, doubleClick);
        }

        public static void Button(MouseButton b, bool press)
        {
            if (UseHid && HidMouse.IsConnected) HidMouse.Button(b, press);
            else Native.Button(b, press);
        }

        /// <summary>Teleport, for parking and restoring the pointer only.</summary>
        public static void JumpTo(int x, int y) { Native.SetCursorPos(x, y); }
    }
}