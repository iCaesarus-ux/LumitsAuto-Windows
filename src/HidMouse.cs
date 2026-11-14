using System;
using System.Drawing;
using System.IO.Ports;
using System.Threading;

namespace AutoClicker
{
    /// <summary>Mouse input through the Pico HID bridge, so Windows sees a real mouse.</summary>
    internal static class HidMouse
    {
        static SerialPort port;
        static readonly object gate = new object();

        public static bool IsConnected { get { return port != null && port.IsOpen; } }

        public static bool Connect(string portName)
        {
            try
            {
                var p = new SerialPort(portName, 115200) { DtrEnable = true, ReadTimeout = 300, WriteTimeout = 300 };
                p.Open();
                port = p;
                HumanMouse.TickMs = 12;
                return Send("p");
            }
            catch (Exception) { port = null; return false; }
        }

        public static void Disconnect()
        {
            lock (gate)
            {
                try { if (port != null) port.Close(); } catch (Exception) { }
                port = null;
                HumanMouse.TickMs = 7;
            }
        }

        static string Arg(MouseButton b)
        {
            return b == MouseButton.Right ? "r" : b == MouseButton.Middle ? "m" : "l";
        }

        static int Clamp(int v) { return Math.Max(-127, Math.Min(127, v)); }

        /// <summary>One command, one ack. False on refusal, timeout or a dead port.</summary>
        static bool Send(string line)
        {
            lock (gate)
            {
                if (!IsConnected) return false;
                try
                {
                    port.WriteLine(line);
                    return port.ReadLine().Trim() == "ok";
                }
                catch (TimeoutException) { return false; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>Closed loop toward an absolute screen point. The path supplies the timing, this supplies the accuracy.</summary>
        public static bool MoveTo(int x, int y, int tol = 1, int maxSteps = 4)
        {
            for (int i = 0; i < maxSteps; i++)
            {
                Point p;
                if (!Native.TryGetCursorPos(out p)) return false;
                int dx = x - p.X, dy = y - p.Y;
                if (Math.Abs(dx) <= tol && Math.Abs(dy) <= tol) return true;
                if (!Send("m " + Clamp(dx) + " " + Clamp(dy))) return false;
                Thread.Sleep(1);
            }
            Point q;
            if (!Native.TryGetCursorPos(out q)) return false;
            return Math.Abs(x - q.X) <= tol + 2 && Math.Abs(y - q.Y) <= tol + 2;
        }

        public static void Click(MouseButton b, bool doubleClick)
        {
            string a = Arg(b);
            Send("c " + a);
            if (doubleClick) { Thread.Sleep(55); Send("c " + a); }
        }

        public static void Button(MouseButton b, bool press)
        {
            Send((press ? "d " : "u ") + Arg(b));
        }
    }
}