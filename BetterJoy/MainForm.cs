using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterJoy.Controller;
using BetterJoy.Properties;
using Microsoft.Win32;

namespace BetterJoy
{
    public partial class MainForm : Form
    {
        public enum NonOriginalController
        {
            Disabled = 0,
            DefaultCalibration = 1,
            ControllerCalibration = 2
        }

        private readonly List<Button> _con;

        private readonly bool _doNotRejoin = bool.Parse(ConfigurationManager.AppSettings["DoNotRejoinJoycons"]);
        private readonly List<Button> _loc;
        private readonly bool _showAsDs4 = bool.Parse(ConfigurationManager.AppSettings["ShowAsDS4"]);
        private readonly bool _showAsXInput = bool.Parse(ConfigurationManager.AppSettings["ShowAsXInput"]);

        private readonly bool _toRumble = bool.Parse(ConfigurationManager.AppSettings["EnableRumble"]);

        public readonly bool AllowCalibration = bool.Parse(ConfigurationManager.AppSettings["AllowCalibration"]);
        public bool CalibrateIMU;
        public bool CalibrateSticks;
        public readonly List<KeyValuePair<string, short[]>> CaliIMUData;
        public readonly List<KeyValuePair<string, ushort[]>> CaliSticksData;
        private int _count;
        private Timer _countDown;
        public bool NonOriginal;
        public readonly float ShakeDelay = float.Parse(ConfigurationManager.AppSettings["ShakeInputDelay"]);
        public readonly bool ShakeInputEnabled = bool.Parse(ConfigurationManager.AppSettings["EnableShakeInput"]);
        public readonly float ShakeSesitivity = float.Parse(ConfigurationManager.AppSettings["ShakeInputSensitivity"]);
        public bool UseControllerStickCalibration;
        public readonly List<short> Xg;
        public readonly List<short> Yg;
        public readonly List<short> Zg;
        public readonly List<short> Xa;
        public readonly List<short> Ya;
        public readonly List<short> Za;
        public readonly List<ushort> Xs1;
        public readonly List<ushort> Ys1;
        public readonly List<ushort> Xs2;
        public readonly List<ushort> Ys2;

        public MainForm()
        {
            Xg = new List<short>();
            Yg = new List<short>();
            Zg = new List<short>();
            Xa = new List<short>();
            Ya = new List<short>();
            Za = new List<short>();
            CaliIMUData = new List<KeyValuePair<string, short[]>>
            {
                new("0", new short[6] { 0, 0, 0, -710, 0, 0 })
            };

            Xs1 = new List<ushort>();
            Ys1 = new List<ushort>();
            Xs2 = new List<ushort>();
            Ys2 = new List<ushort>();
            CaliSticksData = new List<KeyValuePair<string, ushort[]>>
            {
                new("0", new ushort[12] { 2048, 2048, 2048, 2048, 2048, 2048, 2048, 2048, 2048, 2048, 2048, 2048 })
            };
            SetNonOriginalControllerSettings();

            InitializeComponent();

            if (!AllowCalibration)
            {
                AutoCalibrate.Hide();
            }

            _con = new List<Button> { con1, con2, con3, con4 };
            _loc = new List<Button> { loc1, loc2, loc3, loc4 };

            //list all options
            var myConfigs = ConfigurationManager.AppSettings.AllKeys;
            var childSize = new Size(180, 20);
            for (var i = 0; i != myConfigs.Length; i++)
            {
                settingsTable.RowCount++;
                settingsTable.Controls.Add(
                    new Label
                    {
                        Text = myConfigs[i],
                        TextAlign = ContentAlignment.BottomLeft,
                        AutoEllipsis = true,
                        Size = childSize,
                        AutoSize = false
                    },
                    0,
                    i
                );

                var value = ConfigurationManager.AppSettings[myConfigs[i]];
                Control childControl;
                if (value == "true" || value == "false")
                {
                    childControl = new CheckBox { Checked = bool.Parse(value), Size = childSize };
                }
                else
                {
                    childControl = new TextBox { Text = value, Size = childSize };
                }

                childControl.MouseClick += cbBox_Changed;
                settingsTable.Controls.Add(childControl, 1, i);
            }

            // Trick to have bottom padding in the console control
            console.Controls.Add(new Label()
            {
                Height = 6,
                Dock = DockStyle.Bottom,
                BackColor = console.BackColor,
            });

            Shown += MainForm_Shown;
        }

        private void SetNonOriginalControllerSettings()
        {
            Enum.TryParse(
                ConfigurationManager.AppSettings["NonOriginalController"],
                true,
                out NonOriginalController nonOriginalController
            );
            switch (nonOriginalController)
            {
                case NonOriginalController.Disabled:
                    NonOriginal = false;
                    break;
                case NonOriginalController.DefaultCalibration:
                case NonOriginalController.ControllerCalibration:
                    NonOriginal = true;
                    break;
            }

            switch (nonOriginalController)
            {
                case NonOriginalController.Disabled:
                case NonOriginalController.ControllerCalibration:
                    UseControllerStickCalibration = true;
                    break;
                case NonOriginalController.DefaultCalibration:
                    UseControllerStickCalibration = false;
                    break;
            }
        }

        private void HideToTray()
        {
            WindowState = FormWindowState.Minimized;
            notifyIcon.Visible = true;
            notifyIcon.BalloonTipText = "Double click the tray icon to maximise!";
            notifyIcon.ShowBalloonTip(0);
            ShowInTaskbar = false;
            Hide();
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Icon = Resources.betterjoy_icon;
            notifyIcon.Visible = false;

            // Scroll to end
            console.SelectionStart = console.Text.Length;
            console.ScrollToCaret();
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                HideToTray();
            }
        }

        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ShowFromTray();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            Config.Init(CaliIMUData, CaliSticksData);

            startInTrayBox.Checked = Config.IntValue("StartInTray") == 1;

            if (Config.IntValue("StartInTray") == 1)
            {
                HideToTray();
            }
            else
            {
                ShowFromTray();
            }

            SystemEvents.PowerModeChanged += OnPowerChange;
            Refresh();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            Program.Start();
        }

        private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                await Program.Stop();
                SystemEvents.PowerModeChanged -= OnPowerChange;
                Application.Exit();
            }
        }

        private void OnPowerChange(object s, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Resume:
                    AppendTextBox("Resume session.");
                    break;
                case PowerModes.Suspend:
                    AppendTextBox("Suspend session.");
                    break;
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            donationLink.LinkVisited = true;
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/d3xMachina/BetterJoy",
                UseShellExecute = true
            });
        }

        public void AppendTextBox(string value)
        {
            // https://stackoverflow.com/questions/519233/writing-to-a-textbox-from-another-thread
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendTextBox), value);
                return;
            }

            console.AppendText(value + "\r\n");
        }

        private async void LocBtnClickAsync(object sender, EventArgs e)
        {
            var bb = sender as Button;

            if (bb.Tag.GetType() == typeof(Button))
            {
                var button = bb.Tag as Button;

                if (button.Tag.GetType() == typeof(Joycon))
                {
                    var v = (Joycon)button.Tag;
                    v.SetRumble(160.0f, 320.0f, 1.0f);
                    await Task.Delay(300);
                    v.SetRumble(160.0f, 320.0f, 0);
                }
            }
        }

        public void ConBtnClick(int padId)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<int>(ConBtnClick), padId);
            }

            var button = _con[padId];
            ConBtnClick(button, EventArgs.Empty);
        }

        private void ConBtnClick(object sender, EventArgs e)
        {
            var button = sender as Button;

            if (button.Tag.GetType() == typeof(Joycon))
            {
                var v = (Joycon)button.Tag;

                if (v.Other == null && !v.IsPro)
                {
                    // needs connecting to other joycon (so messy omg)
                    var succ = false;

                    if (Program.Mgr.Controllers.Count == 1 || _doNotRejoin)
                    {
                        // when want to have a single joycon in vertical mode
                        v.Other = v; // hacky; implement check in Joycon.cs to account for this
                        succ = true;
                    }
                    else
                    {
                        foreach (var jc in Program.Mgr.Controllers)
                        {
                            if (!jc.IsPro && jc.IsLeft != v.IsLeft && jc != v && jc.Other == null)
                            {
                                v.Other = jc;
                                jc.Other = v;

                                v.DisconnectViGEm();

                                // setting the other joycon's button image
                                foreach (var b in _con)
                                {
                                    if (b.Tag == jc)
                                    {
                                        b.BackgroundImage = jc.IsLeft ? Resources.jc_left : Resources.jc_right;
                                    }
                                }

                                succ = true;
                                break;
                            }
                        }
                    }

                    if (succ)
                    {
                        foreach (var b in _con)
                        {
                            if (b.Tag == v)
                            {
                                b.BackgroundImage = v.IsLeft ? Resources.jc_left : Resources.jc_right;
                            }
                        }
                    }
                }
                else if (v.Other != null && !v.IsPro)
                {
                    // needs disconnecting from other joycon
                    ReenableViGEm(v);
                    ReenableViGEm(v.Other);

                    button.BackgroundImage = v.IsLeft ? Resources.jc_left_s : Resources.jc_right_s;

                    foreach (var b in _con)
                    {
                        if (b.Tag == v.Other)
                        {
                            b.BackgroundImage = v.Other.IsLeft ? Resources.jc_left_s : Resources.jc_right_s;
                        }
                    }

                    v.Other.Other = null;
                    v.Other = null;
                }
            }
        }

        private void startInTrayBox_CheckedChanged(object sender, EventArgs e)
        {
            Config.SetValue("StartInTray", startInTrayBox.Checked ? "1" : "0");
            Config.Save();
        }

        private void btn_open3rdP_Click(object sender, EventArgs e)
        {
            using var partyForm = new _3rdPartyControllers();
            partyForm.ShowDialog();
        }

        private async void settingsApply_Click(object sender, EventArgs e)
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;

            for (var row = 0; row < ConfigurationManager.AppSettings.AllKeys.Length; row++)
            {
                var valCtl = settingsTable.GetControlFromPosition(1, row);
                var keyCtl = settingsTable.GetControlFromPosition(0, row).Text;

                if (valCtl.GetType() == typeof(CheckBox) && settings[keyCtl] != null)
                {
                    settings[keyCtl].Value = ((CheckBox)valCtl).Checked.ToString().ToLower();
                }
                else if (valCtl.GetType() == typeof(TextBox) && settings[keyCtl] != null)
                {
                    settings[keyCtl].Value = ((TextBox)valCtl).Text.ToLower();
                }
            }

            try
            {
                configFile.Save(ConfigurationSaveMode.Modified);
            }
            catch (ConfigurationErrorsException)
            {
                AppendTextBox("Error writing app settings.");
            }

            // Prevent joycons poweroff when applying settings
            ConfigurationManager.AppSettings["AutoPowerOff"] = "false";

            await Program.Stop();
            SystemEvents.PowerModeChanged -= OnPowerChange;
            Program.AllowAnotherInstance();
            Restart();
        }

        private void Restart()
        {
            var info = new ProcessStartInfo
            {
                Arguments = "",
                WorkingDirectory = Environment.CurrentDirectory,
                FileName = Application.ExecutablePath
            };
            Process.Start(info);
            Application.Exit();
        }

        private void ReenableViGEm(Joycon v)
        {
            if (_showAsXInput && v.OutXbox == null)
            {
                v.OutXbox = new OutputControllerXbox360();

                if (_toRumble)
                {
                    v.OutXbox.FeedbackReceived += v.ReceiveRumble;
                }

                v.OutXbox.Connect();
            }

            if (_showAsDs4 && v.OutDs4 == null)
            {
                v.OutDs4 = new OutputControllerDualShock4();

                if (_toRumble)
                {
                    v.OutDs4.FeedbackReceived += v.Ds4_FeedbackReceived;
                }

                v.OutDs4.Connect();
            }
        }

        private void foldLbl_Click(object sender, EventArgs e)
        {
            rightPanel.Visible = !rightPanel.Visible;
            foldLbl.Text = rightPanel.Visible ? "<" : ">";
        }

        private void cbBox_Changed(object sender, EventArgs e)
        {
            var coord = settingsTable.GetPositionFromControl(sender as Control);

            var valCtl = settingsTable.GetControlFromPosition(coord.Column, coord.Row);
            var keyCtl = settingsTable.GetControlFromPosition(coord.Column - 1, coord.Row).Text;

            try
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.AppSettings.Settings;
                if (valCtl.GetType() == typeof(CheckBox) && settings[keyCtl] != null)
                {
                    settings[keyCtl].Value = ((CheckBox)valCtl).Checked.ToString().ToLower();
                }
                else if (valCtl.GetType() == typeof(TextBox) && settings[keyCtl] != null)
                {
                    settings[keyCtl].Value = ((TextBox)valCtl).Text.ToLower();
                }

                if (keyCtl == "HomeLEDOn")
                {
                    var on = settings[keyCtl].Value.ToLower() == "true";
                    foreach (var j in Program.Mgr.Controllers)
                    {
                        j.SetHomeLight(on);
                    }
                }

                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            }
            catch (ConfigurationErrorsException)
            {
                AppendTextBox("Error writing app settings.");
                Trace.WriteLine($"rw {coord.Row}, column {coord.Column}, {sender.GetType()}, {keyCtl}");
            }
        }

        private void StartCalibrate(object sender, EventArgs e)
        {
            var nbControllers = Program.Mgr.Controllers.Count;
            if (nbControllers == 0)
            {
                console.Text = "Please connect a single controller.\r\n";
                return;
            }

            if (nbControllers > 1)
            {
                console.Text = "Please calibrate one controller at a time (disconnect others).\r\n";
                return;
            }

            AutoCalibrate.Enabled = false;
            _countDown = new Timer();
            _count = 4;
            CountDownIMU(null, null);
            _countDown.Tick += CountDownIMU;
            _countDown.Interval = 1000;
            _countDown.Enabled = true;
        }

        private void StartGetIMUData()
        {
            Xg.Clear();
            Yg.Clear();
            Zg.Clear();
            Xa.Clear();
            Ya.Clear();
            Za.Clear();
            _countDown = new Timer();
            _count = 3;
            CalibrateIMU = true;
            _countDown.Tick += CalcIMUData;
            _countDown.Interval = 1000;
            _countDown.Enabled = true;
        }

        private void btn_reassign_open_Click(object sender, EventArgs e)
        {
            using var mapForm = new Reassign();
            mapForm.ShowDialog();
        }

        private void CountDownIMU(object sender, EventArgs e)
        {
            if (_count == 0)
            {
                console.Text = "Calibrating IMU...\r\n";
                _countDown.Stop();
                StartGetIMUData();
            }
            else
            {
                console.Text = "Please keep the controller flat.\r\n";
                console.Text += "Calibration will start in " + _count + " seconds.\r\n";
                _count--;
            }
        }

        private void CalcIMUData(object sender, EventArgs e)
        {
            if (_count == 0)
            {
                _countDown.Stop();
                CalibrateIMU = false;

                var j = Program.Mgr.Controllers.First();
                var serNum = j.SerialNumber;
                var serIndex = FindSerIMU(serNum);
                var arr = new short[6] { 0, 0, 0, 0, 0, 0 };
                if (serIndex == -1)
                {
                    CaliIMUData.Add(
                        new KeyValuePair<string, short[]>(
                            serNum,
                            arr
                        )
                    );
                }
                else
                {
                    arr = CaliIMUData[serIndex].Value;
                }

                var rnd = new Random();
                arr[0] = (short)quickselect_median(Xg.ConvertAll(x => (int)x), rnd.Next);
                arr[1] = (short)quickselect_median(Yg.ConvertAll(x => (int)x), rnd.Next);
                arr[2] = (short)quickselect_median(Zg.ConvertAll(x => (int)x), rnd.Next);
                arr[3] = (short)quickselect_median(Xa.ConvertAll(x => (int)x), rnd.Next);
                arr[4] = (short)quickselect_median(Ya.ConvertAll(x => (int)x), rnd.Next);
                arr[5] = (short)quickselect_median(Za.ConvertAll(x => (int)x), rnd.Next);

                console.Text += "IMU Calibration completed!!!\r\n";
                Config.SaveCaliIMUData(CaliIMUData);
                j.GetActiveIMUData();

                _countDown = new Timer();
                _count = 5;
                CountDownSticksCenter(null, null);
                _countDown.Tick += CountDownSticksCenter;
                _countDown.Interval = 1000;
                _countDown.Enabled = true;
            }
            else
            {
                _count--;
            }
        }

        private void CountDownSticksCenter(object sender, EventArgs e)
        {
            if (_count == 0)
            {
                console.Text = "Calibrating Sticks center position...\r\n";
                _countDown.Stop();
                StartGetSticksCenterData();
            }
            else
            {
                console.Text = "Please keep the sticks at the center position.\r\n";
                console.Text += "Calibration will start in " + _count + " seconds.\r\n";
                _count--;
            }
        }

        private void StartGetSticksCenterData()
        {
            Xs1.Clear();
            Ys1.Clear();
            Xs2.Clear();
            Ys2.Clear();
            _countDown = new Timer();
            _count = 3;
            CalibrateSticks = true;
            _countDown.Tick += CalcSticksCenterData;
            _countDown.Interval = 1000;
            _countDown.Enabled = true;
        }

        private void CalcSticksCenterData(object sender, EventArgs e)
        {
            if (_count == 0)
            {
                _countDown.Stop();
                CalibrateSticks = false;

                var j = Program.Mgr.Controllers.First();
                var serNum = j.SerialNumber;
                var serIndex = FindSerSticks(serNum);
                const int stickCaliSize = 6;
                var arr = new ushort[stickCaliSize * 2] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                if (serIndex == -1)
                {
                    CaliSticksData.Add(
                        new KeyValuePair<string, ushort[]>(
                            serNum,
                            arr
                        )
                    );
                }
                else
                {
                    arr = CaliSticksData[serIndex].Value;
                }

                var rnd = new Random();
                arr[2] = (ushort)Math.Round(quickselect_median(Xs1.ConvertAll(x => (int)x), rnd.Next));
                arr[3] = (ushort)Math.Round(quickselect_median(Ys1.ConvertAll(x => (int)x), rnd.Next));
                arr[2 + stickCaliSize] = (ushort)Math.Round(quickselect_median(Xs2.ConvertAll(x => (int)x), rnd.Next));
                arr[3 + stickCaliSize] = (ushort)Math.Round(quickselect_median(Ys2.ConvertAll(x => (int)x), rnd.Next));
                console.Text += "Sticks center position Calibration completed!!!\r\n";

                _countDown = new Timer();
                _count = 8;
                CountDownSticksMinMax(null, null);
                _countDown.Tick += CountDownSticksMinMax;
                _countDown.Interval = 1000;
                _countDown.Enabled = true;
            }
            else
            {
                _count--;
            }
        }

        private void CountDownSticksMinMax(object sender, EventArgs e)
        {
            if (_count == 0)
            {
                console.Text = "Calibrating Sticks min and max position...\r\n";
                _countDown.Stop();
                StartGetSticksMinMaxData();
            }
            else
            {
                console.Text = "Please move the sticks in a circle when the calibration starts." + "\r\n";
                console.Text += "Calibration will start in " + _count + " seconds.\r\n";
                _count--;
            }
        }

        private void StartGetSticksMinMaxData()
        {
            Xs1.Clear();
            Ys1.Clear();
            Xs2.Clear();
            Ys2.Clear();
            _countDown = new Timer();
            _count = 5;
            CalibrateSticks = true;
            _countDown.Tick += CalcSticksMinMaxData;
            _countDown.Interval = 1000;
            _countDown.Enabled = true;
        }

        private void CalcSticksMinMaxData(object sender, EventArgs e)
        {
            if (_count == 0)
            {
                _countDown.Stop();
                CalibrateSticks = false;

                var j = Program.Mgr.Controllers.First();
                var serNum = j.SerialNumber;
                var serIndex = FindSerSticks(serNum);
                const int stickCaliSize = 6;
                var arr = new ushort[stickCaliSize * 2] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                if (serIndex == -1)
                {
                    CaliSticksData.Add(
                        new KeyValuePair<string, ushort[]>(
                            serNum,
                            arr
                        )
                    );
                }
                else
                {
                    arr = CaliSticksData[serIndex].Value;
                }

                arr[0] = (ushort)Math.Abs(Xs1.Max() - arr[2]);
                arr[1] = (ushort)Math.Abs(Ys1.Max() - arr[3]);
                arr[4] = (ushort)Math.Abs(arr[2] - Xs1.Min());
                arr[5] = (ushort)Math.Abs(arr[3] - Ys1.Min());
                arr[0 + stickCaliSize] = (ushort)Math.Abs(Xs2.Max() - arr[2 + stickCaliSize]);
                arr[1 + stickCaliSize] = (ushort)Math.Abs(Ys2.Max() - arr[3 + stickCaliSize]);
                arr[4 + stickCaliSize] = (ushort)Math.Abs(arr[2 + stickCaliSize] - Xs2.Min());
                arr[5 + stickCaliSize] = (ushort)Math.Abs(arr[3 + stickCaliSize] - Ys2.Min());
                console.Text += "Sticks min and max position Calibration completed!!!\r\n";
                Config.SaveCaliSticksData(CaliSticksData);
                j.GetActiveSticksData();
                AutoCalibrate.Enabled = true;
            }
            else
            {
                _count--;
            }
        }

        private double quickselect_median(List<int> l, Func<int, int> pivotFn)
        {
            if (l.Count == 0)
            {
                return 0;
            }

            var ll = l.Count;
            if (ll % 2 == 1)
            {
                return Quickselect(l, ll / 2, pivotFn);
            }

            return 0.5 * (Quickselect(l, ll / 2 - 1, pivotFn) + Quickselect(l, ll / 2, pivotFn));
        }

        private int Quickselect(List<int> l, int k, Func<int, int> pivotFn)
        {
            if (l.Count == 1 && k == 0)
            {
                return l[0];
            }

            var pivot = l[pivotFn(l.Count)];
            var lows = l.Where(x => x < pivot).ToList();
            var highs = l.Where(x => x > pivot).ToList();
            var pivots = l.Where(x => x == pivot).ToList();
            if (k < lows.Count)
            {
                return Quickselect(lows, k, pivotFn);
            }

            if (k < lows.Count + pivots.Count)
            {
                return pivots[0];
            }

            return Quickselect(highs, k - lows.Count - pivots.Count, pivotFn);
        }

        public short[] ActiveCaliIMUData(string serNum)
        {
            for (var i = 0; i < CaliIMUData.Count; i++)
            {
                if (CaliIMUData[i].Key == serNum)
                {
                    return CaliIMUData[i].Value;
                }
            }

            return CaliIMUData[0].Value;
        }

        public ushort[] ActiveCaliSticksData(string serNum)
        {
            for (var i = 0; i < CaliSticksData.Count; i++)
            {
                if (CaliSticksData[i].Key == serNum)
                {
                    return CaliSticksData[i].Value;
                }
            }

            return CaliSticksData[0].Value;
        }

        private int FindSerIMU(string serNum)
        {
            for (var i = 0; i < CaliIMUData.Count; i++)
            {
                if (CaliIMUData[i].Key == serNum)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindSerSticks(string serNum)
        {
            for (var i = 0; i < CaliSticksData.Count; i++)
            {
                if (CaliSticksData[i].Key == serNum)
                {
                    return i;
                }
            }

            return -1;
        }

        public void Tooltip(string msg)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Tooltip), msg);
                return;
            }

            notifyIcon.Visible = true;
            notifyIcon.BalloonTipText = msg;
            notifyIcon.ShowBalloonTip(0);
        }

        public void SetBatteryColor(Joycon j, int batteryLevel)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<Joycon, int>(SetBatteryColor), j, batteryLevel);
                return;
            }

            foreach (var b in _con)
            {
                if (b.Tag == j)
                {
                    switch (batteryLevel)
                    {
                        case 4:
                            b.BackColor = Color.FromArgb(0xAA, Color.Green);
                            break;
                        case 3:
                            b.BackColor = Color.FromArgb(0xAA, Color.Green);
                            break;
                        case 2:
                            b.BackColor = Color.FromArgb(0xAA, Color.GreenYellow);
                            break;
                        case 1:
                            b.BackColor = Color.FromArgb(0xAA, Color.Orange);
                            break;
                        default:
                            b.BackColor = Color.FromArgb(0xAA, Color.Red);
                            break;
                    }
                }
            }
        }

        public void AddController(Joycon j)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<Joycon>(AddController), j);
                return;
            }

            var i = 0;
            foreach (var b in _con)
            {
                if (!b.Enabled)
                {
                    Bitmap temp;
                    switch (j.Type)
                    {
                        case Joycon.ControllerType.Joycon:
                            if (j.IsLeft)
                            {
                                temp = Resources.jc_left_s;
                            }
                            else
                            {
                                temp = Resources.jc_right_s;
                            }

                            break;
                        case Joycon.ControllerType.Pro:
                            temp = Resources.pro;
                            break;
                        case Joycon.ControllerType.SNES:
                            temp = Resources.snes;
                            break;
                        default:
                            temp = Resources.cross;
                            break;
                    }

                    b.Tag = j; // assign controller to button
                    b.Enabled = true;
                    b.Click += ConBtnClick;
                    b.BackgroundImage = temp;

                    _loc[i].Tag = b;
                    _loc[i].Click += LocBtnClickAsync;

                    break;
                }

                i++;
            }
        }

        public void RemoveController(Joycon j)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<Joycon>(RemoveController), j);
                return;
            }

            foreach (var b in _con)
            {
                if (b.Enabled & (b.Tag == j))
                {
                    b.BackColor = Color.FromArgb(0x00, SystemColors.Control);
                    b.Enabled = false;
                    b.BackgroundImage = Resources.cross;
                    break;
                }
            }
        }

        public void JoinJoycon(Joycon j, Joycon other)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<Joycon, Joycon>(JoinJoycon), j, other);
                return;
            }

            foreach (var b in _con)
            {
                if (b.Tag == j || b.Tag == other)
                {
                    var currentJoycon = b.Tag == j ? j : other;
                    b.BackgroundImage = currentJoycon.IsLeft ? Resources.jc_left : Resources.jc_right;
                }
            }
        }
    }
}
