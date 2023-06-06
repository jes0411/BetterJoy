using System;
using System.Drawing;
using System.Windows.Forms;
using WindowsInput.Events;
using WindowsInput.Events.Sources;

namespace BetterJoy
{
    public partial class Reassign : Form
    {
        private readonly ContextMenuStrip _menuJoyButtons = new();
        private Control _curAssignment;
        private IKeyboardEventSource _keyboard;
        private IMouseEventSource _mouse;

        public Reassign()
        {
            InitializeComponent();

            var menuItem = new ToolStripMenuItem("None");
            menuItem.Tag = -1;
            _menuJoyButtons.Items.Add(menuItem);

            foreach (int i in Enum.GetValues(typeof(Joycon.Button)))
            {
                var temp = new ToolStripMenuItem(Enum.GetName(typeof(Joycon.Button), i));
                temp.Tag = i;
                _menuJoyButtons.Items.Add(temp);
            }

            _menuJoyButtons.ItemClicked += Menu_joy_buttons_ItemClicked;

            foreach (var c in new[]
                     {
                         btn_capture, btn_home, btn_sl_l, btn_sl_r, btn_sr_l, btn_sr_r, btn_shake, btn_reset_mouse,
                         btn_active_gyro
                     })
            {
                c.Tag = c.Name.Substring(4);
                GetPrettyName(c);

                tip_reassign.SetToolTip(
                    c,
                    "Left-click to detect input.\r\nMiddle-click to clear to default.\r\nRight-click to see more options."
                );
                c.MouseDown += Remap;
                c.Menu = _menuJoyButtons;
                c.TextAlign = ContentAlignment.MiddleLeft;
            }
        }

        private void Menu_joy_buttons_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            var c = sender as Control;

            var clickedItem = e.ClickedItem;

            var caller = (SplitButton)c.Tag;

            string value;
            if ((int)clickedItem.Tag == -1)
            {
                value = "0";
            }
            else
            {
                value = "joy_" + clickedItem.Tag;
            }

            Config.SetValue((string)caller.Tag, value);
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
                    Config.SetValue((string)c.Tag, Config.GetDefaultValue((string)c.Tag));
                    GetPrettyName(c);
                    break;
                case MouseButtons.Right:
                    break;
            }
        }

        private void Reassign_Load(object sender, EventArgs e)
        {
            _keyboard = WindowsInput.Capture.Global.KeyboardAsync();
            _keyboard.KeyEvent += Keyboard_KeyEvent;
            _mouse = WindowsInput.Capture.Global.MouseAsync();
            _mouse.MouseEvent += Mouse_MouseEvent;
        }

        private void Mouse_MouseEvent(object sender, EventSourceEventArgs<MouseEvent> e)
        {
            if (_curAssignment != null && e.Data.ButtonDown != null)
            {
                Config.SetValue((string)_curAssignment.Tag, "mse_" + (int)e.Data.ButtonDown.Button);
                AsyncPrettyName(_curAssignment);
                _curAssignment = null;
                e.Next_Hook_Enabled = false;
            }
        }

        private void Keyboard_KeyEvent(object sender, EventSourceEventArgs<KeyboardEvent> e)
        {
            if (_curAssignment != null && e.Data.KeyDown != null)
            {
                Config.SetValue((string)_curAssignment.Tag, "key_" + (int)e.Data.KeyDown.Key);
                AsyncPrettyName(_curAssignment);
                _curAssignment = null;
                e.Next_Hook_Enabled = false;
            }
        }

        private void Reassign_FormClosing(object sender, FormClosingEventArgs e)
        {
            _keyboard.Dispose();
            _mouse.Dispose();
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
            string val;
            switch (val = Config.Value((string)c.Tag))
            {
                case "0":
                    if (c == btn_home)
                    {
                        c.Text = "Guide";
                    }
                    else
                    {
                        c.Text = "";
                    }

                    break;
                default:
                    var t = val.StartsWith("joy_") ? typeof(Joycon.Button) :
                            val.StartsWith("key_") ? typeof(KeyCode) : typeof(ButtonCode);
                    c.Text = Enum.GetName(t, int.Parse(val.AsSpan(4)));
                    break;
            }
        }

        private void btn_apply_Click(object sender, EventArgs e)
        {
            Config.Save();
        }

        private void btn_close_Click(object sender, EventArgs e)
        {
            btn_apply_Click(sender, e);
            Close();
        }
    }
}
