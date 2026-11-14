using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker
{
    /// <summary>A text box that records a key combination instead of text. Delete/Backspace clears it.</summary>
    public class HotkeyBox : TextBox
    {
        static readonly Color EditingColor = Color.FromArgb(255, 248, 214);
        static readonly Color InvalidColor = Color.FromArgb(255, 222, 222);

        Keys hotkey;
        bool invalid;

        public event EventHandler HotkeyChanged;

        public HotkeyBox()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            TextAlign = HorizontalAlignment.Center;
            Cursor = Cursors.Hand;
            UpdateLook();
        }

        public Keys Hotkey
        {
            get { return hotkey; }
            set { hotkey = value; Text = Format(value); }
        }

        /// <summary>Shown in red when the combination couldn't be registered.</summary>
        public bool Invalid
        {
            get { return invalid; }
            set { invalid = value; UpdateLook(); }
        }

        /// <summary>Sets the combination as if the user typed it, raising <see cref="HotkeyChanged"/> if it changed.</summary>
        public void SetFromUser(Keys keys)
        {
            if (keys == hotkey)
            {
                Text = Format(keys);
                return;
            }
            Hotkey = keys;
            var handler = HotkeyChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            // Let plain Tab / Shift+Tab move focus; capture everything else.
            Keys code = keyData & Keys.KeyCode;
            Keys mods = keyData & Keys.Modifiers;
            if (code == Keys.Tab && (mods == Keys.None || mods == Keys.Shift)) return false;
            return true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            Keys code = e.KeyCode;

            if (e.Modifiers == Keys.None && (code == Keys.Back || code == Keys.Delete))
            {
                SetFromUser(Keys.None);
                return;
            }
            if (e.Modifiers == Keys.None && code == Keys.Escape)
            {
                // Plain Esc as a global hotkey would steal Esc from every other program.
                Text = Format(hotkey);
                return;
            }
            if (code == Keys.ShiftKey || code == Keys.ControlKey || code == Keys.Menu || code == Keys.LWin || code == Keys.RWin)
            {
                Text = Format(e.Modifiers) + "+…";
                return;
            }
            SetFromUser(code | e.Modifiers);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            e.Handled = true;
            Text = Format(hotkey);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            UpdateLook();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Text = Format(hotkey);
            UpdateLook();
        }

        void UpdateLook()
        {
            BackColor = Focused ? EditingColor : invalid ? InvalidColor : SystemColors.Window;
        }

        public static string Format(Keys keys)
        {
            if (keys == Keys.None) return "None";

            var parts = new List<string>();
            if ((keys & Keys.Control) == Keys.Control) parts.Add("Ctrl");
            if ((keys & Keys.Alt) == Keys.Alt) parts.Add("Alt");
            if ((keys & Keys.Shift) == Keys.Shift) parts.Add("Shift");
            Keys code = keys & Keys.KeyCode;
            if (code != Keys.None) parts.Add(KeyName(code));
            return string.Join("+", parts);
        }

        static string KeyName(Keys code)
        {
            if (code >= Keys.D0 && code <= Keys.D9) return ((int)code - (int)Keys.D0).ToString();
            if (code >= Keys.NumPad0 && code <= Keys.NumPad9) return "Num" + ((int)code - (int)Keys.NumPad0);
            switch (code)
            {
                case Keys.Prior: return "PageUp";
                case Keys.Next: return "PageDown";
                case Keys.Return: return "Enter";
                case Keys.Escape: return "Esc";
                case Keys.Capital: return "CapsLock";
                case Keys.Scroll: return "ScrollLock";
                case Keys.Snapshot: return "PrintScreen";
                case Keys.Back: return "Backspace";
                case Keys.Delete: return "Del";
                case Keys.Insert: return "Ins";
                case Keys.Add: return "Num+";
                case Keys.Subtract: return "Num-";
                case Keys.Multiply: return "Num*";
                case Keys.Divide: return "Num/";
                case Keys.Decimal: return "Num.";
                case Keys.Oemtilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                default: return code.ToString();
            }
        }
    }
}
