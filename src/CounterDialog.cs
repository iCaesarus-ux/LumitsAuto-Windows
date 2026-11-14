using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AutoClicker
{
    /// <summary>How many of each lumit have been met, which ones to catch, and which catches to keep. Updates live while a run is going.</summary>
    public class CounterDialog : Form
    {
        const int DesignListWidth = 736;
        static readonly Color RadiantColor = Color.FromArgb(200, 120, 0);
        static readonly Color PerfectColor = Color.FromArgb(0, 128, 60);

        readonly LumitCounter counter;
        readonly Label lblSummary, lblRadiant, lblPerfect;
        readonly ListView lvLumits;
        readonly Action onChanged;
        bool filling;

        public CounterDialog(LumitCounter counter)
        {
            this.counter = counter;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "Lumits met";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 506);

            lblSummary = new Label { AutoSize = true, Location = new Point(12, 12), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            lblRadiant = new Label { AutoSize = true, Location = new Point(12, 32), ForeColor = SystemColors.GrayText };
            lblPerfect = new Label { AutoEllipsis = true, Location = new Point(12, 52), Size = new Size(DesignListWidth, 18) };
            Controls.Add(lblSummary);
            Controls.Add(lblRadiant);
            Controls.Add(lblPerfect);

            lvLumits = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                CheckBoxes = true,
                Location = new Point(12, 76),
                Size = new Size(DesignListWidth, 316)
            };
            foreach (string col in new[] { "Catch / Lumit", "Met", "Radiants", "Caught", "Kept", "Attack", "Still needs", "Last met" }) lvLumits.Columns.Add(col);
            for (int i = 1; i <= 5; i++) lvLumits.Columns[i].TextAlign = HorizontalAlignment.Right;
            lvLumits.DoubleClick += delegate { EditRules(); };
            lvLumits.ItemChecked += OnCatchTicked;
            lvLumits.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { EditRules(); e.Handled = true; }
                else if (e.KeyCode == Keys.Delete) { AskRemove(); e.Handled = true; }
            };
            Controls.Add(lvLumits);

            Controls.Add(new Label
            {
                Text = "Tick a Lumit to catch it; unticked ones are attacked to kill. Double-click one to choose which catches " +
                       "to keep (IV 25 in the stats you pick, or the natures you pick) or to fix its name. Each rule is ticked off " +
                       "when a catch meets it; a Lumit with no rules left is perfect and is attacked instead of caught. " +
                       "Remove takes a Lumit off this area's list.",
                Location = new Point(12, 396),
                Size = new Size(DesignListWidth, 46),
                ForeColor = SystemColors.GrayText
            });

            var rules = new Button { Text = "Keep rules…", Location = new Point(12, 468), Size = new Size(96, 28) };
            rules.Click += delegate { EditRules(); };
            var copy = new Button { Text = "Copy", Location = new Point(114, 468), Size = new Size(70, 28) };
            copy.Click += delegate { CopyToClipboard(); };
            var log = new Button { Text = "Catch log", Location = new Point(190, 468), Size = new Size(80, 28) };
            log.Click += delegate { OpenLog(); };
            var reset = new Button { Text = "Reset counts…", Location = new Point(276, 468), Size = new Size(100, 28) };
            reset.Click += delegate { AskReset(); };
            var remove = new Button { Text = "Remove…", Location = new Point(382, 468), Size = new Size(80, 28) };
            remove.Click += delegate { AskRemove(); };
            var close = new Button { Text = "Close", Location = new Point(668, 468), Size = new Size(80, 28), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { rules, copy, log, reset, remove, close });
            CancelButton = close;
            var tips = new ToolTip();
            tips.SetToolTip(copy, "Copies the table, to paste into a spreadsheet or a message.");
            tips.SetToolTip(log, "Every catch with its IVs, nature and what was done. Pictures of released\ncatches are kept in the \"released\" folder next to it.");
            tips.SetToolTip(reset, "Starts the counts from zero. Your catch ticks and keep rules stay.");
            tips.SetToolTip(remove, "Takes the selected Lumit off this area's list, with its counts and rules.");

            ResumeLayout(false);
            PerformLayout();

            onChanged = delegate
            {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((MethodInvoker)Fill); }
                catch (InvalidOperationException) { } // closing
            };
            counter.Changed += onChanged;
            Fill();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            float scale = lvLumits.Width / (float)DesignListWidth;
            int[] widths = { 166, 40, 60, 56, 40, 84, 222, 62 };
            for (int i = 0; i < widths.Length; i++) lvLumits.Columns[i].Width = (int)(widths[i] * scale);
        }

        void Fill()
        {
            int total = counter.Total;
            lblSummary.Text = total.ToString("N0") + " lumit" + (total == 1 ? "" : "s") + " met   •   " + counter.ThisRun.ToString("N0") + " this run";
            lblRadiant.Text = counter.LastRadiantAfter > 0
                ? counter.SinceRadiant.ToString("N0") + " since the last radiant (that one took " + counter.LastRadiantAfter.ToString("N0") + ")"
                : "No radiant met yet.";

            List<LumitCounter.Entry> all = counter.Entries();
            List<string> perfect = all.Where(x => x.IsPerfect).Select(x => x.Display).ToList();
            lblPerfect.Text = perfect.Count > 0
                ? "Perfect (" + perfect.Count + "): " + string.Join(", ", perfect)
                : "No perfect Lumits yet.";
            lblPerfect.ForeColor = perfect.Count > 0 ? PerfectColor : SystemColors.GrayText;

            string selected = SelectedName;
            filling = true;
            lvLumits.BeginUpdate();
            lvLumits.Items.Clear();
            foreach (LumitCounter.Entry x in all)
            {
                var item = new ListViewItem(x.Display) { Tag = x.Name, Checked = x.Catch };
                item.SubItems.Add(x.Met.ToString("N0"));
                item.SubItems.Add(x.Radiants > 0 ? x.Radiants.ToString("N0") : "");
                item.SubItems.Add(x.Caught > 0 ? x.Caught.ToString("N0") : "");
                item.SubItems.Add(x.Caught > 0 ? x.Kept.ToString("N0") : "");
                item.SubItems.Add(x.Catch ? x.AttackName + (x.TypicalSiphon > 0 ? " ≈ " + Math.Round(x.TypicalSiphon * 100) + "%" : "") : "");
                item.SubItems.Add(NeedsText(x));
                item.SubItems.Add(When(x.LastMet));
                if (x.Radiants > 0) item.ForeColor = RadiantColor;
                if (x.IsPerfect) item.ForeColor = PerfectColor;
                if (x.Name == LumitCounter.Unreadable) item.ForeColor = SystemColors.GrayText;
                lvLumits.Items.Add(item);
                if (x.Name == selected) item.Selected = true;
            }
            lvLumits.EndUpdate();
            filling = false;
        }

        static string NeedsText(LumitCounter.Entry x)
        {
            if (x.IsPerfect) return "★ Perfect";
            if (!x.HasKeepRules) return x.Catch ? "keeps all (no rules)" : "";
            return x.NeedsText;
        }

        static string When(DateTime t)
        {
            if (t == DateTime.MinValue) return "";
            return t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString("d MMM", CultureInfo.CurrentCulture);
        }

        string SelectedName
        {
            get { return lvLumits.SelectedItems.Count > 0 ? lvLumits.SelectedItems[0].Tag as string : null; }
        }

        void OnCatchTicked(object sender, ItemCheckedEventArgs e)
        {
            if (filling) return;
            string name = e.Item.Tag as string;
            if (name == null || name == LumitCounter.Unreadable)
            {
                if (e.Item.Checked) BeginInvoke((MethodInvoker)Fill); // can't catch by a name that wasn't read
                return;
            }
            counter.SetCatch(name, e.Item.Checked);
        }

        void EditRules()
        {
            string name = SelectedName;
            if (name == null) return;
            LumitCounter.Entry entry = counter.Lookup(name);
            if (entry == null) return;
            using (var dlg = new RulesDialog(counter, entry)) dlg.ShowDialog(this);
        }

        void CopyToClipboard()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Lumit\tCaught as\tMet\tRadiants\tCaught\tKept\tCatch\tStill needs");
            foreach (LumitCounter.Entry x in counter.Entries())
                sb.AppendLine(x.Name + "\t" + x.CaughtName + "\t" + x.Met + "\t" + x.Radiants + "\t" + x.Caught + "\t" + x.Kept + "\t" +
                              (x.Catch ? "yes" : "") + "\t" + NeedsText(x));
            sb.AppendLine("Total\t\t" + counter.Total);
            try { Clipboard.SetText(sb.ToString()); }
            catch (Exception) { } // clipboard busy; nothing lost
        }

        void OpenLog()
        {
            string file = CatchLog.Default.FilePath;
            if (!File.Exists(file))
            {
                MessageBox.Show(this, "Nothing has been caught yet.", "Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { Process.Start(file); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't open the log:\n" + ex.Message, "Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void AskReset()
        {
            if (MessageBox.Show(this, "Start counting from zero? This clears the counts of all " + counter.Total.ToString("N0") +
                    " lumits met so far. Your catch ticks and keep rules stay.",
                    "Auto Clicker", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            counter.Reset();
        }

        void AskRemove()
        {
            string name = SelectedName;
            if (name == null) return;
            if (MessageBox.Show(this, "Remove " + name + " from this area? Its counts, catch tick and keep rules go. " +
                    "If it's met here again, it comes back as a new Lumit.",
                    "Auto Clicker", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            counter.Remove(name);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) counter.Changed -= onChanged;
            base.Dispose(disposing);
        }
    }

    /// <summary>One lumit: catch it or not, which catches to keep, and its name.</summary>
    public class RulesDialog : Form
    {
        readonly LumitCounter counter;
        readonly LumitCounter.Entry entry;
        readonly TextBox txtName, txtNewNature;
        readonly CheckBox chkCatch;
        readonly ComboBox cbAttack;
        readonly Dictionary<Stat, CheckBox> ivBoxes = new Dictionary<Stat, CheckBox>();
        readonly CheckedListBox lstNatures;

        public RulesDialog(LumitCounter counter, LumitCounter.Entry entry)
        {
            this.counter = counter;
            this.entry = entry;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "Keep rules: " + entry.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(400, 588);

            Controls.Add(new Label { Text = "Name in fights:", AutoSize = true, Location = new Point(12, 15) });
            txtName = new TextBox { Location = new Point(110, 12), Width = 150, Text = entry.Name };
            Controls.Add(txtName);
            Controls.Add(new Label
            {
                Text = entry.CaughtName.Length > 0 ? "caught as " + entry.CaughtName : "not caught yet",
                AutoSize = true,
                Location = new Point(268, 15),
                ForeColor = SystemColors.GrayText
            });

            chkCatch = new CheckBox { Text = "Catch it (unticked: attack to kill)", AutoSize = true, Location = new Point(12, 44), Checked = entry.Catch };
            Controls.Add(chkCatch);
            Controls.Add(new Label { Text = "Weaken it with:", AutoSize = true, Location = new Point(12, 77) });
            cbAttack = new ComboBox { Location = new Point(110, 73), Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
            for (int i = 0; i < LumitCounter.LeadMoves.Length; i++) cbAttack.Items.Add(LumitCounter.LeadMoves[i] + " (" + LumitCounter.Slots[i] + ")");
            cbAttack.SelectedIndex = Math.Max(0, Math.Min(LumitCounter.LeadMoves.Length - 1, entry.Attack));
            Controls.Add(cbAttack);
            new ToolTip { AutoPopDelay = 20000 }.SetToolTip(cbAttack,
                "Your lead's move, until the next hit would usually kill it; then Capture.\nThe names are Infibex's moves: it's the button in that place that counts.");

            var grpIv = new GroupBox { Text = "Keep a catch with IV 25 in any of", Location = new Point(12, 106), Size = new Size(376, 84) };
            Controls.Add(grpIv);
            for (int i = 0; i < Stats.All.Length; i++)
            {
                Stat s = Stats.All[i];
                var c = new CheckBox { Text = Stats.Label(s), AutoSize = true, Location = new Point(14 + (i % 3) * 120, 24 + (i / 3) * 28), Checked = (entry.KeepIvs & s) != 0 };
                grpIv.Controls.Add(c);
                ivBoxes[s] = c;
            }

            var grpNature = new GroupBox { Text = "…or with any of these natures", Location = new Point(12, 198), Size = new Size(376, 236) };
            Controls.Add(grpNature);
            lstNatures = new CheckedListBox { Location = new Point(14, 22), Size = new Size(348, 166), CheckOnClick = true, IntegralHeight = false };
            foreach (string n in counter.KnownNatures().Union(entry.KeepNatures, StringComparer.OrdinalIgnoreCase).OrderBy(n => n))
                lstNatures.Items.Add(new NatureItem(n), entry.KeepNatures.Contains(n, StringComparer.OrdinalIgnoreCase));
            grpNature.Controls.Add(lstNatures);
            txtNewNature = new TextBox { Location = new Point(14, 198), Width = 180 };
            grpNature.Controls.Add(txtNewNature);
            var add = new Button { Text = "Add nature", Location = new Point(200, 196), Size = new Size(90, 26) };
            add.Click += delegate { AddNature(); };
            grpNature.Controls.Add(add);

            string found = entry.FoundText;
            Controls.Add(new Label
            {
                Text = found.Length > 0 ? "Found already: " + found : "Nothing found yet.",
                AutoEllipsis = true,
                Location = new Point(12, 440),
                Size = new Size(376, 18),
                ForeColor = found.Length > 0 ? Color.FromArgb(0, 128, 60) : SystemColors.GrayText
            });
            Controls.Add(new Label
            {
                Text = "Either rule is enough; a catch matching neither is released (in practice mode only noted). " +
                       "A rule a catch surely meets is unticked here, since you have one now. With none left the Lumit " +
                       "is perfect and is attacked, not caught. Tick a rule again to look for another.",
                Location = new Point(12, 462),
                Size = new Size(376, 80),
                ForeColor = SystemColors.GrayText
            });

            var ok = new Button { Text = "OK", Location = new Point(222, 548), Size = new Size(80, 28) };
            ok.Click += delegate { Commit(); };
            var cancel = new Button { Text = "Cancel", Location = new Point(308, 548), Size = new Size(80, 28), DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            txtNewNature.Enter += delegate { AcceptButton = null; };
            txtNewNature.Leave += delegate { AcceptButton = ok; };
            txtNewNature.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { AddNature(); e.SuppressKeyPress = true; } };

            ResumeLayout(false);
            PerformLayout();
        }

        void AddNature()
        {
            string n = LumitNames.Clean(txtNewNature.Text);
            if (n.Length == 0) return;
            int existing = -1;
            for (int i = 0; i < lstNatures.Items.Count; i++)
                if (string.Equals(((NatureItem)lstNatures.Items[i]).Name, n, StringComparison.OrdinalIgnoreCase)) existing = i;
            if (existing < 0) existing = lstNatures.Items.Add(new NatureItem(n));
            lstNatures.SetItemChecked(existing, true);
            txtNewNature.Clear();
        }

        void Commit()
        {
            Stat ivs = Stat.None;
            foreach (KeyValuePair<Stat, CheckBox> kv in ivBoxes) if (kv.Value.Checked) ivs |= kv.Key;
            var natures = lstNatures.CheckedItems.Cast<NatureItem>().Select(i => i.Name).ToList();
            counter.SetCatch(entry.Name, chkCatch.Checked);
            counter.SetKeepRules(entry.Name, ivs, natures);
            counter.SetAttack(entry.Name, cbAttack.SelectedIndex);
            string newName = LumitNames.Clean(txtName.Text);
            if (newName.Length > 0 && newName != entry.Name) counter.Rename(entry.Name, newName);
            DialogResult = DialogResult.OK;
        }

        /// <summary>A nature in the list, shown with what it raises and lowers.</summary>
        class NatureItem
        {
            public readonly string Name;

            public NatureItem(string name)
            {
                Name = name;
            }

            public override string ToString()
            {
                string effect = Natures.Effect(Name);
                return effect.Length > 0 ? Name + "   (" + effect + ")" : Name;
            }
        }
    }
}
