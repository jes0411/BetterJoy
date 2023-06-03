using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static BetterJoyForCemu.HIDapi;

namespace BetterJoyForCemu
{
    public partial class _3rdPartyControllers : Form
    {
        private static readonly string path;

        static _3rdPartyControllers()
        {
            path = Path.GetDirectoryName(Environment.ProcessPath)
                   + "\\3rdPartyControllers";
        }

        public _3rdPartyControllers()
        {
            InitializeComponent();
            list_allControllers.HorizontalScrollbar = true;
            list_customControllers.HorizontalScrollbar = true;

            chooseType.Items.AddRange(new[] { "Pro Controller", "Left Joycon", "Right Joycon" });

            chooseType.FormattingEnabled = true;
            group_props.Controls.Add(chooseType);
            group_props.Enabled = false;

            GetSaved3rdPartyControllers().ForEach(controller => list_customControllers.Items.Add(controller));
            RefreshControllerList();
        }

        public static List<SController> GetSaved3rdPartyControllers()
        {
            var controllers = new List<SController>();

            if (File.Exists(path))
            {
                using var file = new StreamReader(path);
                var line = string.Empty;
                while ((line = file.ReadLine()) != null && line != string.Empty)
                {
                    var split = line.Split('|');
                    //won't break existing config file
                    var serial_number = "";
                    if (split.Length > 4)
                    {
                        serial_number = split[4];
                    }

                    controllers.Add(
                        new SController(
                            split[0],
                            ushort.Parse(split[1]),
                            ushort.Parse(split[2]),
                            byte.Parse(split[3]),
                            serial_number
                        )
                    );
                }
            }

            return controllers;
        }

        private List<SController> GetActive3rdPartyControllers()
        {
            var controllers = new List<SController>();

            foreach (SController v in list_customControllers.Items)
            {
                controllers.Add(v);
            }

            return controllers;
        }

        private void CopyCustomControllers()
        {
            var controllers = GetActive3rdPartyControllers();
            Program.update3rdPartyControllers(controllers);
        }

        private bool ContainsText(ListBox a, string manu)
        {
            foreach (SController v in a.Items)
            {
                if (v == null)
                {
                    continue;
                }

                if (v.name == null)
                {
                    continue;
                }

                if (v.name.Equals(manu))
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshControllerList()
        {
            list_allControllers.Items.Clear();
            var ptr = hid_enumerate(0x0, 0x0);
            var top_ptr = ptr;

            // Add device to list
            for (hid_device_info enumerate; ptr != IntPtr.Zero; ptr = enumerate.next)
            {
                enumerate = (hid_device_info)Marshal.PtrToStructure(ptr, typeof(hid_device_info));

                if (enumerate.serial_number == null)
                {
                    continue;
                }

                // TODO: try checking against interface number instead
                var name = enumerate.product_string + '(' + enumerate.vendor_id + '-' + enumerate.product_id + '-' +
                           enumerate.serial_number + ')';
                if (!ContainsText(list_customControllers, name) && !ContainsText(list_allControllers, name))
                {
                    list_allControllers.Items.Add(
                        new SController(name, enumerate.vendor_id, enumerate.product_id, 0, enumerate.serial_number)
                    );
                    // 0 type is undefined
                    Console.WriteLine("Found controller " + name);
                }
            }

            hid_free_enumeration(top_ptr);
        }

        private void btn_add_Click(object sender, EventArgs e)
        {
            if (list_allControllers.SelectedItem != null)
            {
                list_customControllers.Items.Add(list_allControllers.SelectedItem);
                list_allControllers.Items.Remove(list_allControllers.SelectedItem);

                list_allControllers.ClearSelected();
            }
        }

        private void btn_remove_Click(object sender, EventArgs e)
        {
            if (list_customControllers.SelectedItem != null)
            {
                list_allControllers.Items.Add(list_customControllers.SelectedItem);
                list_customControllers.Items.Remove(list_customControllers.SelectedItem);

                list_customControllers.ClearSelected();
            }
        }

        private void btn_apply_Click(object sender, EventArgs e)
        {
            var sc = "";
            foreach (SController v in list_customControllers.Items)
            {
                sc += v.Serialise() + "\r\n";
            }

            File.WriteAllText(path, sc);
            CopyCustomControllers();
        }

        private void btn_applyAndClose_Click(object sender, EventArgs e)
        {
            btn_apply_Click(sender, e);
            Close();
        }

        private void _3rdPartyControllers_FormClosing(object sender, FormClosingEventArgs e)
        {
            btn_apply_Click(sender, e);
        }

        private void btn_refresh_Click(object sender, EventArgs e)
        {
            RefreshControllerList();
        }

        private void list_allControllers_SelectedValueChanged(object sender, EventArgs e)
        {
            if (list_allControllers.SelectedItem != null)
            {
                tip_device.Show((list_allControllers.SelectedItem as SController).name, list_allControllers);
            }
        }

        private void list_customControllers_SelectedValueChanged(object sender, EventArgs e)
        {
            if (list_customControllers.SelectedItem != null)
            {
                var v = list_customControllers.SelectedItem as SController;
                tip_device.Show(v.name, list_customControllers);

                chooseType.SelectedIndex = v.type - 1;

                group_props.Enabled = true;
            }
            else
            {
                chooseType.SelectedIndex = -1;
                group_props.Enabled = false;
            }
        }

        private void list_customControllers_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Y > list_customControllers.ItemHeight * list_customControllers.Items.Count)
            {
                list_customControllers.SelectedItems.Clear();
            }
        }

        private void list_allControllers_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Y > list_allControllers.ItemHeight * list_allControllers.Items.Count)
            {
                list_allControllers.SelectedItems.Clear();
            }
        }

        private void chooseType_SelectedValueChanged(object sender, EventArgs e)
        {
            if (list_customControllers.SelectedItem != null)
            {
                var v = list_customControllers.SelectedItem as SController;
                v.type = (byte)(chooseType.SelectedIndex + 1);
            }
        }

        public class SController
        {
            public string name;
            public ushort product_id;
            public string serial_number;
            public byte type; // 1 is pro, 2 is left joy, 3 is right joy
            public ushort vendor_id;

            public SController(string name, ushort vendor_id, ushort product_id, byte type, string serial_number)
            {
                this.product_id = product_id;
                this.vendor_id = vendor_id;
                this.type = type;
                this.serial_number = serial_number;
                this.name = name;
            }

            public override bool Equals(object obj)
            {
                //Check for null and compare run-time types.
                if (obj == null || !GetType().Equals(obj.GetType()))
                {
                    return false;
                }

                var s = (SController)obj;
                return s.product_id == product_id && s.vendor_id == vendor_id && s.serial_number == serial_number;
            }

            public override int GetHashCode()
            {
                return Tuple.Create(product_id, vendor_id, serial_number).GetHashCode();
            }

            public override string ToString()
            {
                return name ?? $"Unidentified Device ({product_id})";
            }

            public string Serialise()
            {
                return $"{name}|{vendor_id}|{product_id}|{type}|{serial_number}";
            }
        }
    }
}
