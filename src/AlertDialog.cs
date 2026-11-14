using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AutoClicker
{
    /// <summary>Where the enemy's name card is (for the radiant check and the counter), and how to tell you.</summary>
    public class AlertDialog : Form
    {
        static readonly Color GoodColor = Color.FromArgb(0, 128, 0);
        static readonly Color BadColor = Color.FromArgb(190, 30, 30);
        static readonly Color RadiantColor = Color.FromArgb(200, 140, 0);

        readonly AppSettings settings;
        readonly StepSpace space;
        readonly IList<ClickPoint> screenSteps;
        readonly CheckBox chkEnabled, chkOnStop;
        readonly ComboBox cbStep;
        readonly Label lblArea, lblTest, lblSend;
        readonly PictureBox picArea;
        readonly NumericUpDown numGold;
        readonly TextBox txtPhone;
        readonly GroupBox grpLook, grpHow;
        Rectangle area;

        public AlertDialog(AppSettings settings, int stepCount, StepSpace space, IList<ClickPoint> screenSteps)
        {
            this.settings = settings;
            this.space = space;
            this.screenSteps = screenSteps;
            area = settings.AlertArea;
            if (area.Width <= 0 && space.Window != null) area = GoldText.LumitsNameAreaFor(space.Window.ClientSize);

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "Alerts";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 492);

            chkEnabled = new CheckBox
            {
                Text = "Alert me when an enemy has a gold (radiant) name",
                AutoSize = true,
                Location = new Point(14, 12),
                Checked = settings.AlertEnabled
            };
            Controls.Add(chkEnabled);

            // Where to look (also used by the lumits counter, so it stays usable with the check off)
            grpLook = new GroupBox { Text = "Enemy name card (radiant check and lumits counter)", Location = new Point(12, 40), Size = new Size(436, 192) };
            Controls.Add(grpLook);
            AddLabel(grpLook, "Check before clicking step:", 14, 26);
            cbStep = new ComboBox { Location = new Point(180, 22), Width = 60, DropDownStyle = ComboBoxStyle.DropDownList };
            for (int i = 1; i <= Math.Max(1, stepCount); i++) cbStep.Items.Add(i.ToString());
            cbStep.SelectedIndex = Math.Max(0, Math.Min(cbStep.Items.Count - 1, settings.AlertStep - 1));
            grpLook.Controls.Add(cbStep);
            AddLabel(grpLook, "(the attack step)", 246, 26).ForeColor = SystemColors.GrayText;

            AddLabel(grpLook, "Name area:", 14, 58);
            lblArea = AddLabel(grpLook, "", 86, 58);
            AddButton(grpLook, "Pick area…", 228, 52, 96, 26, delegate { PickArea(); });
            var btnDefault = AddButton(grpLook, "Lumits default", 330, 52, 96, 26, delegate { UseDefault(); });
            btnDefault.Enabled = space.Window != null;

            picArea = new PictureBox
            {
                Location = new Point(14, 88),
                Size = new Size(200, 64),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            grpLook.Controls.Add(picArea);
            AddButton(grpLook, "Test now", 228, 88, 96, 26, delegate { TestNow(); });
            lblTest = AddLabel(grpLook, "", 228, 122);

            AddLabel(grpLook, "Alert at", 14, 162);
            numGold = new NumericUpDown { Location = new Point(66, 159), Width = 50, Minimum = 1, Maximum = 50, TextAlign = HorizontalAlignment.Right };
            numGold.Value = Math.Max(1, Math.Min(50, settings.AlertGoldPercent));
            grpLook.Controls.Add(numGold);
            AddLabel(grpLook, "% gold or more (radiant ≈ 11%, normal 0%)", 120, 162).ForeColor = SystemColors.GrayText;

            // How to alert me
            grpHow = new GroupBox { Text = "Alert my phone", Location = new Point(12, 240), Size = new Size(436, 204) };
            Controls.Add(grpHow);
            AddLabel(grpHow, "ntfy topic, or Discord webhook URL:", 14, 24);
            txtPhone = new TextBox { Location = new Point(14, 44), Width = 316, Text = settings.AlertPhone };
            grpHow.Controls.Add(txtPhone);
            AddButton(grpHow, "Send test", 336, 42, 90, 26, delegate { SendTest(); });
            lblSend = AddLabel(grpHow, "", 14, 72);
            chkOnStop = new CheckBox
            {
                Text = "Also when a run stops by itself (with the reason and a screenshot)",
                AutoSize = true,
                Location = new Point(14, 94),
                Checked = settings.AlertOnStop
            };
            grpHow.Controls.Add(chkOnStop);
            var hint = new Label
            {
                Text = "ntfy: install the free ntfy app, subscribe to a topic with a hard-to-guess name, and type the same name here.\n" +
                       "Discord: in your own server, Edit Channel → Integrations → Webhooks → New Webhook → Copy Webhook URL.",
                Location = new Point(14, 120),
                Size = new Size(410, 78),
                ForeColor = SystemColors.GrayText
            };
            grpHow.Controls.Add(hint);

            var ok = new Button { Text = "OK", Location = new Point(282, 454), Size = new Size(80, 28) };
            ok.Click += delegate { Commit(); };
            var cancel = new Button { Text = "Cancel", Location = new Point(368, 454), Size = new Size(80, 28), DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            UpdateAreaLabel();

            ResumeLayout(false);
            PerformLayout();
        }

        void UpdateAreaLabel()
        {
            lblArea.Text = area.Width > 0 ? area.X + ", " + area.Y + "   " + area.Width + " × " + area.Height : "not set";
        }

        void ShowPicture(Bitmap bmp)
        {
            Image old = picArea.Image;
            picArea.Image = bmp;
            if (old != null) old.Dispose();
        }

        void UseDefault()
        {
            area = GoldText.LumitsNameAreaFor(space.Window.ClientSize);
            UpdateAreaLabel();
            TestNow();
        }

        void PickArea()
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            Rectangle picked = Rectangle.Empty;
            try
            {
                ScreenGrab.WhileHidden(new Form[] { Owner, this }, delegate
                {
                    using (Bitmap frame = ScreenGrab.CaptureVirtualScreen())
                    using (var overlay = new PickOverlay(frame, vs, screenSteps, true))
                    {
                        overlay.ShowDialog();
                        if (overlay.Picks.Count > 0) picked = overlay.Picks[0];
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't capture the screen:\n" + ex.Message, "Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (picked.Width <= 0) return;
            area = space.FromScreen(picked);
            UpdateAreaLabel();
            TestNow();
        }

        void TestNow()
        {
            if (area.Width <= 0)
            {
                ShowTest("Pick the name area first", BadColor);
                return;
            }
            Bitmap shot = null;
            if (space.Window != null) shot = GoldText.Capture(space, area);
            else ScreenGrab.WhileHidden(new Form[] { Owner, this }, delegate { shot = GoldText.Capture(space, area); });
            if (shot == null)
            {
                ShowTest("Couldn't read that area", BadColor);
                return;
            }
            double gold = GoldText.Percent(shot, new Rectangle(Point.Empty, shot.Size));
            string name = ReadName(shot);
            ShowPicture(shot);
            bool radiant = gold >= (double)numGold.Value;
            string reads = name == null ? "Can't read names on this PC"
                         : name.Length == 0 ? "No name read (is a fight on?)"
                         : "Reads: " + name;
            ShowTest(reads + "\nGold now: " + gold.ToString("0.0") + "% — " + (radiant ? "RADIANT" : "normal name"), radiant ? RadiantColor : GoodColor);
        }

        /// <summary>Reads the name on a worker thread: Windows' text recognition shouldn't wait on the window's own thread.</summary>
        static string ReadName(Bitmap card)
        {
            string name = "";
            var t = new Thread(() => name = LumitNames.Read(card)) { IsBackground = true };
            t.Start();
            return t.Join(5000) ? name : "";
        }

        void ShowTest(string text, Color color)
        {
            lblTest.Text = text;
            lblTest.ForeColor = color;
        }

        void SendTest()
        {
            string target = txtPhone.Text;
            lblSend.Text = "Sending…";
            lblSend.ForeColor = SystemColors.GrayText;
            var t = new Thread(() =>
            {
                string error = PhoneNotifier.Send(target, "Auto Clicker test", "If you can read this, radiant alerts will reach your phone.", null);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        lblSend.Text = error == null ? "Sent. Check your phone." : "Not sent: " + error;
                        lblSend.ForeColor = error == null ? GoodColor : BadColor;
                    });
                }
                catch (InvalidOperationException) { } // dialog already closed
            }) { IsBackground = true };
            t.Start();
        }

        void Commit()
        {
            if (chkEnabled.Checked && area.Width <= 0)
            {
                MessageBox.Show(this, "Pick the area where the enemy's name appears first.", "Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            settings.AlertEnabled = chkEnabled.Checked;
            settings.AlertStep = cbStep.SelectedIndex + 1;
            settings.AlertArea = area;
            settings.AlertGoldPercent = (int)numGold.Value;
            settings.AlertPhone = txtPhone.Text.Trim();
            settings.AlertOnStop = chkOnStop.Checked;
            DialogResult = DialogResult.OK;
        }

        static Label AddLabel(Control parent, string text, int x, int y)
        {
            var l = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
            parent.Controls.Add(l);
            return l;
        }

        static Button AddButton(Control parent, string text, int x, int y, int w, int h, EventHandler onClick)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && picArea.Image != null) picArea.Image.Dispose();
            base.Dispose(disposing);
        }
    }
}
