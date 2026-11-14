using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoClicker
{
    /// <summary>
    /// A top-level window (the game): where its content area is, a picture of it even while
    /// other windows cover it, and mouse messages delivered straight to it without moving the
    /// real pointer.
    /// </summary>
    public class GameWindow
    {
        public readonly IntPtr Handle;
        public readonly string Title;
        public readonly string ProcessName;

        GameWindow(IntPtr handle, string title, string processName)
        {
            Handle = handle;
            Title = title;
            ProcessName = processName;
        }

        public override string ToString()
        {
            return Title + "   (" + ProcessName + ")";
        }

        public bool Exists { get { return IsWindow(Handle); } }

        public bool IsMinimized { get { return IsIconic(Handle); } }

        /// <summary>Screen position of the top-left of the window's content area.</summary>
        public Point ClientOrigin
        {
            get
            {
                var p = new POINT();
                ClientToScreen(Handle, ref p);
                return new Point(p.X, p.Y);
            }
        }

        public Size ClientSize
        {
            get
            {
                RECT r;
                GetClientRect(Handle, out r);
                return new Size(r.Right, r.Bottom);
            }
        }

        /// <summary>Picture of the content area, even if covered; null if minimized, closed or unreadable.</summary>
        public Bitmap CaptureClient()
        {
            if (!IsWindow(Handle) || IsIconic(Handle)) return null;
            Size size = ClientSize;
            if (size.Width <= 0 || size.Height <= 0) return null;

            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            bool ok;
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr dc = g.GetHdc();
                ok = PrintWindow(Handle, dc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
                g.ReleaseHdc(dc);
            }
            if (ok) return bmp;
            bmp.Dispose();
            return null;
        }

        /// <summary>Tells the window the pointer moved to <paramref name="client"/>. False if Windows refused the message.</summary>
        public bool PostMove(Point client)
        {
            return PostMove(client, false);
        }

        /// <summary>A pointer move, optionally with the left button held (a drag).</summary>
        public bool PostMove(Point client, bool leftHeld)
        {
            return PostMessage(Handle, WM_MOUSEMOVE, new IntPtr(leftHeld ? MK_LBUTTON : 0), LParam(client));
        }

        /// <summary>Tells the window a button went down or up at <paramref name="client"/>. False if Windows refused the message.</summary>
        public bool PostButton(MouseButton button, bool press, bool secondOfDouble, Point client)
        {
            uint msg, held;
            switch (button)
            {
                case MouseButton.Right:
                    msg = press ? (secondOfDouble ? WM_RBUTTONDBLCLK : WM_RBUTTONDOWN) : WM_RBUTTONUP;
                    held = MK_RBUTTON;
                    break;
                case MouseButton.Middle:
                    msg = press ? (secondOfDouble ? WM_MBUTTONDBLCLK : WM_MBUTTONDOWN) : WM_MBUTTONUP;
                    held = MK_MBUTTON;
                    break;
                default:
                    msg = press ? (secondOfDouble ? WM_LBUTTONDBLCLK : WM_LBUTTONDOWN) : WM_LBUTTONUP;
                    held = MK_LBUTTON;
                    break;
            }
            return PostMessage(Handle, msg, new IntPtr(press ? held : 0), LParam(client));
        }

        static IntPtr LParam(Point p)
        {
            return new IntPtr(((p.Y & 0xFFFF) << 16) | (p.X & 0xFFFF));
        }

        /// <summary>Ordinary application windows that are open right now, excluding this app's own.</summary>
        public static List<GameWindow> List()
        {
            var result = new List<GameWindow>();
            int self = Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr h, IntPtr lParam)
            {
                if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
                if ((GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
                int cloaked;
                if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return true;

                int len = GetWindowTextLength(h);
                if (len == 0) return true;
                var sb = new StringBuilder(len + 1);
                GetWindowText(h, sb, sb.Capacity);

                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == self) return true;
                string name;
                try { name = Process.GetProcessById((int)pid).ProcessName; }
                catch (Exception) { name = "?"; }

                result.Add(new GameWindow(h, sb.ToString(), name));
                return true;
            }, IntPtr.Zero);
            return result;
        }

        /// <summary>The open window best matching a saved process name and title, or null.</summary>
        public static GameWindow Find(string processName, string title)
        {
            List<GameWindow> all = List();
            Func<GameWindow, bool> sameProcess = w => string.Equals(w.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
            Func<GameWindow, bool> sameTitle = w => string.Equals(w.Title, title, StringComparison.OrdinalIgnoreCase);
            foreach (GameWindow w in all) if (sameProcess(w) && sameTitle(w)) return w;
            foreach (GameWindow w in all) if (sameProcess(w)) return w;
            foreach (GameWindow w in all) if (sameTitle(w)) return w;
            return null;
        }

        // ---------------------------------------------------------------- Win32

        const uint WM_MOUSEMOVE = 0x0200;
        const uint WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203;
        const uint WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_RBUTTONDBLCLK = 0x0206;
        const uint WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_MBUTTONDBLCLK = 0x0209;
        const uint MK_LBUTTON = 0x0001, MK_RBUTTON = 0x0002, MK_MBUTTON = 0x0010;
        const uint PW_CLIENTONLY = 0x1, PW_RENDERFULLCONTENT = 0x2;
        const uint GW_OWNER = 4;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x80;
        const int DWMWA_CLOAKED = 14;

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll", SetLastError = true)] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);
    }

    /// <summary>Where step positions are measured from: a game window's content area, or the whole screen.</summary>
    public class StepSpace
    {
        public static readonly StepSpace WholeScreen = new StepSpace(null);

        public readonly GameWindow Window;

        public StepSpace(GameWindow window)
        {
            Window = window;
        }

        /// <summary>Screen position of step coordinate (0, 0).</summary>
        public Point Origin { get { return Window == null ? Point.Empty : Window.ClientOrigin; } }

        public Rectangle ToScreen(Rectangle r)
        {
            Point o = Origin;
            r.Offset(o.X, o.Y);
            return r;
        }

        public Rectangle FromScreen(Rectangle r)
        {
            Point o = Origin;
            r.Offset(-o.X, -o.Y);
            return r;
        }

        /// <summary>Snapshot of an area in step coordinates: read from the game window if there is one (works while covered), else from the screen.</summary>
        public Snapshot Read(Rectangle area)
        {
            if (Window == null) return Snapshot.FromScreen(area);
            using (Bitmap b = Window.CaptureClient())
                return b == null ? null : Snapshot.FromBitmap(b, area);
        }
    }
}
