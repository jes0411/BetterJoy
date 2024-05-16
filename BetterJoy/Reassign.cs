﻿using System;
using System.Drawing;
using System.Windows.Forms;
using WindowsInput.Events;
using WindowsInput.Events.Sources;

namespace BetterJoy
{
    public partial class Reassign : Form
    {
        private Control _curAssignment;

        private enum ButtonAction
        {
            None,
            Disabled
        }

        public Reassign()
        {
            InitializeComponent();

            var menuJoyButtons = createMenuJoyButtons();

            var menuJoyButtonsNoDisable = createMenuJoyButtons();
            var key = Enum.GetName(typeof(ButtonAction), ButtonAction.Disabled);
            menuJoyButtonsNoDisable.Items.RemoveByKey(key);

            foreach (var c in new[]
                     {
                         btn_capture, btn_home, btn_sl_l, btn_sl_r, btn_sr_l, btn_sr_r, btn_shake, btn_reset_mouse,
                         btn_active_gyro, btn_swap_ab, btn_swap_xy
                     })
            {
                c.Tag = c.Name.Substring(4);
                GetPrettyName(c);

                tip_reassign.SetToolTip(
                    c,
                    "Left-click to detect input.\r\nMiddle-click to clear to default.\r\nRight-click to see more options."
                );
                c.MouseDown += Remap;
                c.Menu = (c.Parent == gb_inputs) ? menuJoyButtons : menuJoyButtonsNoDisable;
                c.TextAlign = ContentAlignment.MiddleLeft;
            }
        }

        private void Menu_joy_buttons_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            var c = sender as Control;

            var clickedItem = e.ClickedItem;

            var caller = (SplitButton)c.Tag;

            string value;
            if (clickedItem.Tag is ButtonAction action)
            {
                if (action == ButtonAction.None)
                {
                    value = "0";
                }
                else
                {
                    value = "act_" + (int)clickedItem.Tag;
                }
            }
            else
            {
                value = "joy_" + (int)clickedItem.Tag;
            }

            Settings.SetValue((string)caller.Tag, value);
            GetPrettyName(caller);
        }

        private void Remap(object sender, MouseEventArgs e)
        {
            var c = sender as SplitButton;
            switch (e.Button)
            {
                case MouseButtons.Left:
                    c.Text = "...";
                    _curAssignment = c;
                    break;
                case MouseButtons.Middle:
                    Settings.SetValue((string)c.Tag, Settings.GetDefaultValue((string)c.Tag));
                    GetPrettyName(c);
                    break;
                case MouseButtons.Right:
                    break;
            }
        }

        private void Reassign_Load(object sender, EventArgs e)
        {
            InputCapture.Global.RegisterEvent(GlobalKeyEvent);
            InputCapture.Global.RegisterEvent(GlobalMouseEvent);
        }

        private void GlobalMouseEvent(object sender, EventSourceEventArgs<MouseEvent> e)
        {
            ButtonCode? button = e.Data.ButtonDown?.Button;

            if (_curAssignment != null && button != null)
            {
                Settings.SetValue((string)_curAssignment.Tag, "mse_" + (int)button);
                AsyncPrettyName(_curAssignment);
                _curAssignment = null;
                e.Next_Hook_Enabled = false;
            }
        }

        private void GlobalKeyEvent(object sender, EventSourceEventArgs<KeyboardEvent> e)
        {
            KeyCode? key = e.Data.KeyDown?.Key;

            if (_curAssignment != null && key != null)
            {
                Settings.SetValue((string)_curAssignment.Tag, "key_" + (int)key);
                AsyncPrettyName(_curAssignment);
                _curAssignment = null;
                e.Next_Hook_Enabled = false;
            }
        }

        private void Reassign_FormClosing(object sender, FormClosingEventArgs e)
        {
            InputCapture.Global.UnregisterEvent(GlobalKeyEvent);
            InputCapture.Global.UnregisterEvent(GlobalMouseEvent);
        }

        private void AsyncPrettyName(Control c)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<Control>(AsyncPrettyName), c);
                return;
            }

            GetPrettyName(c);
        }

        private void GetPrettyName(Control c)
        {
            string val = Settings.Value((string)c.Tag);

            if (val == "0")
            {
                c.Text = "";
            }
            else
            {
                var type =
                        val.StartsWith("act_") ? typeof(ButtonAction) :
                        val.StartsWith("joy_") ? typeof(Joycon.Button) :
                        val.StartsWith("key_") ? typeof(KeyCode) : typeof(ButtonCode);

                c.Text = Enum.GetName(type, int.Parse(val.AsSpan(4)));
            }
        }

        private void btn_apply_Click(object sender, EventArgs e)
        {
            Settings.Save();
        }

        private void btn_ok_Click(object sender, EventArgs e)
        {
            btn_apply_Click(sender, e);
            Close();
        }

        private ContextMenuStrip createMenuJoyButtons()
        {
            var menuJoyButtons = new ContextMenuStrip(components);

            foreach (int tag in Enum.GetValues(typeof(ButtonAction)))
            {
                var name = Enum.GetName(typeof(ButtonAction), tag);
                var temp = new ToolStripMenuItem(name) { Name = name };
                temp.Tag = (ButtonAction)tag;
                menuJoyButtons.Items.Add(temp);
            }

            foreach (int tag in Enum.GetValues(typeof(Joycon.Button)))
            {
                var name = Enum.GetName(typeof(Joycon.Button), tag);
                var temp = new ToolStripMenuItem(name) { Name = name };
                temp.Tag = (Joycon.Button)tag;
                menuJoyButtons.Items.Add(temp);
            }

            menuJoyButtons.ItemClicked += Menu_joy_buttons_ItemClicked;

            return menuJoyButtons;
        }
    }
}
