using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClickRecorder
{
    /// <summary>
    /// Sees every mouse button press on the PC, yours and other programs' alike, through Windows' low-level mouse
    /// hook. It runs on a thread of its own, so a busy window never delays a press or makes Windows drop the hook.
    /// Presses on this app's own windows are left out.
    /// </summary>
    sealed class MouseHook : IDisposable
    {
        public struct Press
        {
            /// <summary>When it happened, in <see cref="Stopwatch"/> ticks.</summary>
            public long Ticks;
            public int X, Y;
            public MouseButtons Button;
        }

        readonly Action<Press> onPress;
        readonly int ownProcess = Process.GetCurrentProcess().Id;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        Thread thread;
        uint threadId;
        IntPtr hook;
        HookProc proc; // kept here so it isn't collected while Windows still calls it
        int error;

        /// <param name="onPress">Called on the hook's own thread for each press, so it has to be quick and thread-safe.</param>
        public MouseHook(Action<Press> onPress)
        {
            this.onPress = onPress;
        }

        public bool Running { get { return thread != null; } }

        /// <summary>Starts watching. False, with Windows' reason in <paramref name="problem"/>, if the hook couldn't be set.</summary>
        public bool Start(out string problem)
        {
            problem = null;
            if (thread != null) return true;
            ready.Reset();
            thread = new Thread(Run) { IsBackground = true, Name = "Mouse hook" };
            thread.Start();
            ready.WaitOne();
            if (hook != IntPtr.Zero) return true;
            thread.Join();
            thread = null;
            problem = new Win32Exception(error).Message;
            return false;
        }

        public void Stop()
        {
            if (thread == null) return;
            PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            thread.Join(2000);
            thread = null;
        }

        public void Dispose()
        {
            Stop();
            ready.Dispose();
        }

        void Run()
        {
            threadId = GetCurrentThreadId();
            MSG msg;
            PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_NOREMOVE); // makes sure the thread has a queue for WM_QUIT
            proc = Callback;
            hook = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) error = Marshal.GetLastWin32Error();
            ready.Set();
            if (hook == IntPtr.Zero) return;
            try
            {
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            finally
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }

        IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            long now = Stopwatch.GetTimestamp();
            if (code >= 0)
            {
                var info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                MouseButtons button = ButtonOf(wParam.ToInt64(), info.mouseData);
                if (button != MouseButtons.None && !OnOwnWindow(info.pt))
                {
                    try
                    {
                        onPress(new Press { Ticks = now, X = info.pt.X, Y = info.pt.Y, Button = button });
                    }
                    catch (Exception)
                    {
                        // Never let a problem here hold up the mouse.
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        static MouseButtons ButtonOf(long message, uint mouseData)
        {
            switch (message)
            {
                case WM_LBUTTONDOWN: return MouseButtons.Left;
                case WM_RBUTTONDOWN: return MouseButtons.Right;
                case WM_MBUTTONDOWN: return MouseButtons.Middle;
                case WM_XBUTTONDOWN: return (mouseData >> 16) == 2 ? MouseButtons.XButton2 : MouseButtons.XButton1;
                default: return MouseButtons.None;
            }
        }

        bool OnOwnWindow(POINT pt)
        {
            IntPtr window = WindowFromPoint(pt);
            if (window == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            return pid == ownProcess;
        }

        // ---------------------------------------------------------------- Windows

        const int WH_MOUSE_LL = 14;
        const uint WM_QUIT = 0x0012;
        const uint PM_NOREMOVE = 0;
        const long WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_XBUTTONDOWN = 0x020B;

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public POINT pt;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessage(ref MSG msg);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(POINT pt);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}
