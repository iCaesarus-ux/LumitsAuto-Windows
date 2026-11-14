using System;
using System.Threading;
using System.Windows.Forms;

namespace AutoClicker
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Native.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "Auto Clicker",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

            // A second copy couldn't register the same hotkeys, so keep it to one.
            bool firstInstance;
            using (var mutex = new Mutex(true, "AutoClicker.SingleInstance.6b1f0c", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("Auto Clicker is already running.", "Auto Clicker",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.Run(new MainForm());
            }
        }
    }
}
