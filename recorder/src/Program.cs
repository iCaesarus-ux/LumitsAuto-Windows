using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClickRecorder
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "Click Recorder",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

            // A second copy couldn't register the same hotkey, so keep it to one.
            bool firstInstance;
            using (var mutex = new Mutex(true, "ClickRecorder.SingleInstance.3d9a41", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("Click Recorder is already running.", "Click Recorder",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.Run(new RecorderForm());
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetProcessDPIAware();

        /// <summary>
        /// Per-monitor awareness makes click positions and the screen picture real screen pixels, the same as
        /// Auto Clicker's, on every monitor whatever its scaling. Falls back on older Windows.
        /// </summary>
        static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (Exception) { }
            try { if (SetProcessDpiAwareness(2) == 0) return; } catch (Exception) { }
            try { SetProcessDPIAware(); } catch (Exception) { }
        }
    }
}
