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
        /// <summary>How far the pointer really moves per unit asked for, measured once on connect.</summary>
        static double gain = 1.0;

        public static bool IsConnected { get { return port != null && port.IsOpen; } }

        public static bool Connect(string portName)
        {
            try
            {
                var p = new SerialPort(portName, 115200) { DtrEnable = true, ReadTimeout = 300, WriteTimeout = 300 };
                p.Open();
                port = p;
                HumanMouse.TickMs = 12;
                if (!Send("p"))
                {
                    try { p.Close(); } catch (Exception) { }
                    port = null;
                    return false;
                }
                gain = MeasureGain();
                return true;
            }
            catch (Exception)
            {
                try { if (port != null) port.Close(); } catch (Exception) { }
                port = null;
                return false;
            }
        }

        public static void Disconnect()
        {
            lock (gate)
            {
                try { if (port != null) port.Close(); } catch (Exception) { }
                port = null;
                gain = 1.0;
                HumanMouse.TickMs = 7;
            }
        }

        static string Arg(MouseButton b)
        {
            return b == MouseButton.Right ? "r" : b == MouseButton.Middle ? "m" : "l";
        }

        static int Clamp(int v) { return Math.Max(-127, Math.Min(127, v)); }

        /// <summary>One report's worth of a delta, corrected for the measured gain. Never rounds to nothing.</summary>
        static int Step(int d)
        {
            int v = (int)Math.Round(d / gain);
            if (v == 0 && d != 0) v = d > 0 ? 1 : -1;
            return Clamp(v);
        }

        /// <summary>How far the pointer moves per unit asked for. Moves the pointer 100 right and back.</summary>
        static double MeasureGain()
        {
            Point a, b;
            if (!Native.TryGetCursorPos(out a)) return 1.0;
            if (!Send("m 100 0")) return 1.0;
            Thread.Sleep(60);
            if (!Native.TryGetCursorPos(out b)) return 1.0;
            Send("m " + Clamp(a.X - b.X) + " 0");
            double g = (b.X - a.X) / 100.0;
            return g < 0.4 || g > 4 ? 1.0 : g;
        }

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
        public static bool MoveTo(int x, int y, int tol = 1)
        {
            Point p;
            if (!Native.TryGetCursorPos(out p)) return false;
            int budget = Math.Min(200, (Math.Abs(x - p.X) + Math.Abs(y - p.Y)) / 20 + 10);
            for (int i = 0; i < budget; i++)
            {
                int dx = x - p.X, dy = y - p.Y;
                if (Math.Abs(dx) <= tol && Math.Abs(dy) <= tol) return true;
                if (!Send("m " + Step(dx) + " " + Step(dy))) return false;

                // The report takes a few ms to land; reading again straight away sees the old
                // position and asks for the same move twice, which overshoots.
                Point after = p;
                for (int w = 0; w < 10; w++)
                {
                    Thread.Sleep(1);
                    if (!Native.TryGetCursorPos(out after)) return false;
                    if (after.X != p.X || after.Y != p.Y) break;
                }
                p = after;
            }
            return Math.Abs(x - p.X) <= tol + 2 && Math.Abs(y - p.Y) <= tol + 2;
        }

        public static bool Click(MouseButton b, bool doubleClick)
        {
            string a = Arg(b);
            bool ok = Send("c " + a);
            if (doubleClick) { Thread.Sleep(55); ok &= Send("c " + a); }
            return ok;
        }

        public static bool Button(MouseButton b, bool press)
        {
            return Send((press ? "d " : "u ") + Arg(b));
        }
    }
}