using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterJoyForCemu.Controller;
using BetterJoyForCemu.Properties;
using Microsoft.Win32;

namespace BetterJoyForCemu
{
    public partial class MainForm : Form
    {
        public enum NonOriginalController
        {
            Disabled = 0,
            DefaultCalibration = 1,
            ControllerCalibration = 2
        }

        private readonly List<Button> con;

        private readonly bool doNotRejoin = bool.Parse(ConfigurationManager.AppSettings["DoNotRejoinJoycons"]);
        private readonly List<Button> loc;
        private readonly bool showAsDS4 = bool.Parse(ConfigurationManager.AppSettings["ShowAsDS4"]);
        private readonly bool showAsXInput = bool.Parse(ConfigurationManager.AppSettings["ShowAsXInput"]);

        private readonly bool toRumble = bool.Parse(ConfigurationManager.AppSettings["EnableRumble"]);

        public bool allowCalibration = bool.Parse(ConfigurationManager.AppSettings["AllowCalibration"]);
        public bool calibrateIMU;
        public bool calibrateSticks;
        public List<KeyValuePair<string, float[]>> caliIMUData;
        public List<KeyValuePair<string, ushort[]>> caliSticksData;
        private int count;
        private Timer countDown;
        public bool nonOriginal;
        public float shakeDelay = float.Parse(ConfigurationManager.AppSettings["ShakeInputDelay"]);
        public bool shakeInputEnabled = bool.Parse(ConfigurationManager.AppSettings["EnableShakeInput"]);
        public float shakeSesitivity = float.Parse(ConfigurationManager.AppSettings["ShakeInputSensitivity"]);
        public bool useControllerStickCalibration;
        public List<int> xG, yG, zG, xA, yA, zA;
        public List<ushort> xS1, yS1, xS2, yS2;

        public MainForm()
        {
            xG = new List<int>();
            yG = new List<int>();
            zG = new List<int>();
            xA = new List<int>();
            yA = new List<int>();
            zA = new List<int>();
            caliIMUData = new List<KeyValuePair<string, float[]>>
            {
                new("0", new float[6] { 0, 0, 0, -710, 0, 0 })
            };

            xS1 = new List<ushort>();
            yS1 = new List<ushort>();
            xS2 = new List<ushort>();
            yS2 = new List<ushort>();
            caliSticksData = new List<KeyValuePair<string, ushort[]>>
            {
                new("0", new ushort[12] { 2048, 2048, 2048, 2048, 2048, 2048, 2048, 2048, 2048, 2048, 2048, 2048 })
            };
            SetNonOriginalControllerSettings();

            InitializeComponent();

            if (!allowCalibration)
            {
                AutoCalibrate.Hide();
            }

            con = new List<Button> { con1, con2, con3, con4 };
            loc = new List<Button> { loc1, loc2, loc3, loc4 };

            //list all options
            var myConfigs = ConfigurationManager.AppSettings.AllKeys;
            var childSize = new Size(150, 20);
            for (var i = 0; i != myConfigs.Length; i++)
            {
                settingsTable.RowCount++;
                settingsTable.Controls.Add(
                    new Label
                    {
                        Text = myConfigs[i], TextAlign = ContentAlignment.BottomLeft, AutoEllipsis = true,
                        Size = childSize
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
                    nonOriginal = false;
                    break;
                case NonOriginalController.DefaultCalibration:
                case NonOriginalController.ControllerCalibration:
                    nonOriginal = true;
                    break;
            }

            switch (nonOriginalController)
            {
                case NonOriginalController.Disabled:
                case NonOriginalController.ControllerCalibration:
                    useControllerStickCalibration = true;
                    break;
                case NonOriginalController.DefaultCalibration:
                    useControllerStickCalibration = false;
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
            Icon = Resources.betterjoyforcemu_icon;
            notifyIcon.Visible = false;
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
            Config.Init(caliIMUData, caliSticksData);

            passiveScanBox.Checked = Config.IntValue("ProgressiveScan") == 1;
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

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                Program.Stop();
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
            Process.Start("http://paypal.me/DavidKhachaturov/5");
        }

        private void passiveScanBox_CheckedChanged(object sender, EventArgs e)
        {
            Config.SetValue("ProgressiveScan", passiveScanBox.Checked ? "1" : "0");
            Config.Save();
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

        private async void locBtnClickAsync(object sender, EventArgs e)
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

        public void conBtnClick(int padId)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<int>(conBtnClick), padId);
            }

            var button = con[padId];
            conBtnClick(button, EventArgs.Empty);
        }

        private void conBtnClick(object sender, EventArgs e)
        {
            var button = sender as Button;

            if (button.Tag.GetType() == typeof(Joycon))
            {
                var v = (Joycon)button.Tag;

                if (v.other == null && !v.isPro)
                {
                    // needs connecting to other joycon (so messy omg)
                    var succ = false;

                    if (Program.mgr.j.Count == 1 || doNotRejoin)
                    {
                        // when want to have a single joycon in vertical mode
                        v.other = v; // hacky; implement check in Joycon.cs to account for this
                        succ = true;
                    }
                    else
                    {
                        foreach (var jc in Program.mgr.j)
                        {
                            if (!jc.isPro && jc.isLeft != v.isLeft && jc != v && jc.other == null)
                            {
                                v.other = jc;
                                jc.other = v;

                                v.DisconnectViGEm();

                                // setting the other joycon's button image
                                foreach (var b in con)
                                {
                                    if (b.Tag == jc)
                                    {
                                        b.BackgroundImage = jc.isLeft ? Resources.jc_left : Resources.jc_right;
                                    }
                                }

                                succ = true;
                                break;
                            }
                        }
                    }

                    if (succ)
                    {
                        foreach (var b in con)
                        {
                            if (b.Tag == v)
                            {
                                b.BackgroundImage = v.isLeft ? Resources.jc_left : Resources.jc_right;
                            }
                        }
                    }
                }
                else if (v.other != null && !v.isPro)
                {
                    // needs disconnecting from other joycon
                    ReenableViGEm(v);
                    ReenableViGEm(v.other);

                    button.BackgroundImage = v.isLeft ? Resources.jc_left_s : Resources.jc_right_s;

                    foreach (var b in con)
                    {
                        if (b.Tag == v.other)
                        {
                            b.BackgroundImage = v.other.isLeft ? Resources.jc_left_s : Resources.jc_right_s;
                        }
                    }

                    v.other.other = null;
                    v.other = null;
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

        private void settingsApply_Click(object sender, EventArgs e)
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;

            for (var row = 0; row < ConfigurationManager.AppSettings.AllKeys.Length; row++)
            {
                var valCtl = settingsTable.GetControlFromPosition(1, row);
                var KeyCtl = settingsTable.GetControlFromPosition(0, row).Text;

                if (valCtl.GetType() == typeof(CheckBox) && settings[KeyCtl] != null)
                {
                    settings[KeyCtl].Value = ((CheckBox)valCtl).Checked.ToString().ToLower();
                }
                else if (valCtl.GetType() == typeof(TextBox) && settings[KeyCtl] != null)
                {
                    settings[KeyCtl].Value = ((TextBox)valCtl).Text.ToLower();
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

            ConfigurationManager.AppSettings["AutoPowerOff"] =
                    "false"; // Prevent joycons poweroff when applying settings
            Program.Stop();
            SystemEvents.PowerModeChanged -= OnPowerChange;
            Program.allowAnotherInstance();
            Restart();
        }

        private void Restart()
        {
            var Info = new ProcessStartInfo();
            Info.Arguments = "";
            Info.WorkingDirectory = Environment.CurrentDirectory;
            Info.FileName = Application.ExecutablePath;
            Process.Start(Info);
            Application.Exit();
        }

        private void ReenableViGEm(Joycon v)
        {
            if (showAsXInput && v.out_xbox == null)
            {
                v.out_xbox = new OutputControllerXbox360();

                if (toRumble)
                {
                    v.out_xbox.FeedbackReceived += v.ReceiveRumble;
                }

                v.out_xbox.Connect();
            }

            if (showAsDS4 && v.out_ds4 == null)
            {
                v.out_ds4 = new OutputControllerDualShock4();

                if (toRumble)
                {
                    v.out_ds4.FeedbackReceived += v.Ds4_FeedbackReceived;
                }

                v.out_ds4.Connect();
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
            var KeyCtl = settingsTable.GetControlFromPosition(coord.Column - 1, coord.Row).Text;

            try
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.AppSettings.Settings;
                if (valCtl.GetType() == typeof(CheckBox) && settings[KeyCtl] != null)
                {
                    settings[KeyCtl].Value = ((CheckBox)valCtl).Checked.ToString().ToLower();
                }
                else if (valCtl.GetType() == typeof(TextBox) && settings[KeyCtl] != null)
                {
                    settings[KeyCtl].Value = ((TextBox)valCtl).Text.ToLower();
                }

                if (KeyCtl == "HomeLEDOn")
                {
                    var on = settings[KeyCtl].Value.ToLower() == "true";
                    foreach (var j in Program.mgr.j)
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
                Trace.WriteLine($"rw {coord.Row}, column {coord.Column}, {sender.GetType()}, {KeyCtl}");
            }
        }

        private void StartCalibrate(object sender, EventArgs e)
        {
            var nbControllers = Program.mgr.j.Count;
            if (nbControllers == 0)
            {
                console.Text = "Please connect a single pro controller.\r\n";
                return;
            }

            if (nbControllers > 1)
            {
                console.Text = "Please calibrate one controller at a time (disconnect others).\r\n";
                return;
            }

            AutoCalibrate.Enabled = false;
            countDown = new Timer();
            count = 4;
            CountDownIMU(null, null);
            countDown.Tick += CountDownIMU;
            countDown.Interval = 1000;
            countDown.Enabled = true;
        }

        private void StartGetIMUData()
        {
            xG.Clear();
            yG.Clear();
            zG.Clear();
            xA.Clear();
            yA.Clear();
            zA.Clear();
            countDown = new Timer();
            count = 3;
            calibrateIMU = true;
            countDown.Tick += CalcIMUData;
            countDown.Interval = 1000;
            countDown.Enabled = true;
        }

        private void btn_reassign_open_Click(object sender, EventArgs e)
        {
            using var mapForm = new Reassign();
            mapForm.ShowDialog();
        }

        private void CountDownIMU(object sender, EventArgs e)
        {
            if (count == 0)
            {
                console.Text = "Calibrating IMU...\r\n";
                countDown.Stop();
                StartGetIMUData();
            }
            else
            {
                console.Text = "Please keep the controller flat.\r\n";
                console.Text += "Calibration will start in " + count + " seconds.\r\n";
                count--;
            }
        }

        private void CalcIMUData(object sender, EventArgs e)
        {
            if (count == 0)
            {
                countDown.Stop();
                calibrateIMU = false;

                var j = Program.mgr.j.First();
                var serNum = j.serial_number;
                var serIndex = findSerIMU(serNum);
                var Arr = new float[6] { 0, 0, 0, 0, 0, 0 };
                if (serIndex == -1)
                {
                    caliIMUData.Add(
                        new KeyValuePair<string, float[]>(
                            serNum,
                            Arr
                        )
                    );
                }
                else
                {
                    Arr = caliIMUData[serIndex].Value;
                }

                var rnd = new Random();
                Arr[0] = (float)quickselect_median(xG, rnd.Next);
                Arr[1] = (float)quickselect_median(yG, rnd.Next);
                Arr[2] = (float)quickselect_median(zG, rnd.Next);
                Arr[3] = (float)quickselect_median(xA, rnd.Next);
                Arr[4] = (float)quickselect_median(yA, rnd.Next);
                Arr[5] = (float)quickselect_median(zA, rnd.Next) - 4010; //Joycon.cs acc_sen 16384
                console.Text += "IMU Calibration completed!!!\r\n";
                Config.SaveCaliIMUData(caliIMUData);
                j.getActiveIMUData();

                countDown = new Timer();
                count = 5;
                CountDownSticksCenter(null, null);
                countDown.Tick += CountDownSticksCenter;
                countDown.Interval = 1000;
                countDown.Enabled = true;
            }
            else
            {
                count--;
            }
        }

        private void CountDownSticksCenter(object sender, EventArgs e)
        {
            if (count == 0)
            {
                console.Text = "Calibrating Sticks center position...\r\n";
                countDown.Stop();
                StartGetSticksCenterData();
            }
            else
            {
                console.Text = "Please keep the sticks at the center position.\r\n";
                console.Text += "Calibration will start in " + count + " seconds.\r\n";
                count--;
            }
        }

        private void StartGetSticksCenterData()
        {
            xS1.Clear();
            yS1.Clear();
            xS2.Clear();
            yS2.Clear();
            countDown = new Timer();
            count = 3;
            calibrateSticks = true;
            countDown.Tick += CalcSticksCenterData;
            countDown.Interval = 1000;
            countDown.Enabled = true;
        }

        private void CalcSticksCenterData(object sender, EventArgs e)
        {
            if (count == 0)
            {
                countDown.Stop();
                calibrateSticks = false;

                var j = Program.mgr.j.First();
                var serNum = j.serial_number;
                var serIndex = findSerSticks(serNum);
                const int stickCaliSize = 6;
                var Arr = new ushort[stickCaliSize * 2] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                if (serIndex == -1)
                {
                    caliSticksData.Add(
                        new KeyValuePair<string, ushort[]>(
                            serNum,
                            Arr
                        )
                    );
                }
                else
                {
                    Arr = caliSticksData[serIndex].Value;
                }

                var rnd = new Random();
                Arr[2] = (ushort)Math.Round(quickselect_median(xS1.ConvertAll(x => (int)x), rnd.Next));
                Arr[3] = (ushort)Math.Round(quickselect_median(yS1.ConvertAll(x => (int)x), rnd.Next));
                Arr[2 + stickCaliSize] = (ushort)Math.Round(quickselect_median(xS2.ConvertAll(x => (int)x), rnd.Next));
                Arr[3 + stickCaliSize] = (ushort)Math.Round(quickselect_median(yS2.ConvertAll(x => (int)x), rnd.Next));
                console.Text += "Sticks center position Calibration completed!!!\r\n";

                countDown = new Timer();
                count = 8;
                CountDownSticksMinMax(null, null);
                countDown.Tick += CountDownSticksMinMax;
                countDown.Interval = 1000;
                countDown.Enabled = true;
            }
            else
            {
                count--;
            }
        }

        private void CountDownSticksMinMax(object sender, EventArgs e)
        {
            if (count == 0)
            {
                console.Text = "Calibrating Sticks min and max position...\r\n";
                countDown.Stop();
                StartGetSticksMinMaxData();
            }
            else
            {
                console.Text = "Please move the sticks in a circle when the calibration starts." + "\r\n";
                console.Text += "Calibration will start in " + count + " seconds.\r\n";
                count--;
            }
        }

        private void StartGetSticksMinMaxData()
        {
            xS1.Clear();
            yS1.Clear();
            xS2.Clear();
            yS2.Clear();
            countDown = new Timer();
            count = 5;
            calibrateSticks = true;
            countDown.Tick += CalcSticksMinMaxData;
            countDown.Interval = 1000;
            countDown.Enabled = true;
        }

        private void CalcSticksMinMaxData(object sender, EventArgs e)
        {
            if (count == 0)
            {
                countDown.Stop();
                calibrateSticks = false;

                var j = Program.mgr.j.First();
                var serNum = j.serial_number;
                var serIndex = findSerSticks(serNum);
                const int stickCaliSize = 6;
                var Arr = new ushort[stickCaliSize * 2] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                if (serIndex == -1)
                {
                    caliSticksData.Add(
                        new KeyValuePair<string, ushort[]>(
                            serNum,
                            Arr
                        )
                    );
                }
                else
                {
                    Arr = caliSticksData[serIndex].Value;
                }

                Arr[0] = (ushort)Math.Abs(xS1.Max() - Arr[2]);
                Arr[1] = (ushort)Math.Abs(yS1.Max() - Arr[3]);
                Arr[4] = (ushort)Math.Abs(Arr[2] - xS1.Min());
                Arr[5] = (ushort)Math.Abs(Arr[3] - yS1.Min());
                Arr[0 + stickCaliSize] = (ushort)Math.Abs(xS2.Max() - Arr[2 + stickCaliSize]);
                Arr[1 + stickCaliSize] = (ushort)Math.Abs(yS2.Max() - Arr[3 + stickCaliSize]);
                Arr[4 + stickCaliSize] = (ushort)Math.Abs(Arr[2 + stickCaliSize] - xS2.Min());
                Arr[5 + stickCaliSize] = (ushort)Math.Abs(Arr[3 + stickCaliSize] - yS2.Min());
                console.Text += "Sticks min and max position Calibration completed!!!\r\n";
                Config.SaveCaliSticksData(caliSticksData);
                j.getActiveSticksData();
                AutoCalibrate.Enabled = true;
            }
            else
            {
                count--;
            }
        }

        private double quickselect_median(List<int> l, Func<int, int> pivot_fn)
        {
            var ll = l.Count;
            if (ll % 2 == 1)
            {
                return quickselect(l, ll / 2, pivot_fn);
            }

            return 0.5 * (quickselect(l, ll / 2 - 1, pivot_fn) + quickselect(l, ll / 2, pivot_fn));
        }

        private int quickselect(List<int> l, int k, Func<int, int> pivot_fn)
        {
            if (l.Count == 1 && k == 0)
            {
                return l[0];
            }

            var pivot = l[pivot_fn(l.Count)];
            var lows = l.Where(x => x < pivot).ToList();
            var highs = l.Where(x => x > pivot).ToList();
            var pivots = l.Where(x => x == pivot).ToList();
            if (k < lows.Count)
            {
                return quickselect(lows, k, pivot_fn);
            }

            if (k < lows.Count + pivots.Count)
            {
                return pivots[0];
            }

            return quickselect(highs, k - lows.Count - pivots.Count, pivot_fn);
        }

        public float[] activeCaliIMUData(string serNum)
        {
            for (var i = 0; i < caliIMUData.Count; i++)
            {
                if (caliIMUData[i].Key == serNum)
                {
                    return caliIMUData[i].Value;
                }
            }

            return caliIMUData[0].Value;
        }

        public ushort[] activeCaliSticksData(string serNum)
        {
            for (var i = 0; i < caliSticksData.Count; i++)
            {
                if (caliSticksData[i].Key == serNum)
                {
                    return caliSticksData[i].Value;
                }
            }

            return caliSticksData[0].Value;
        }

        private int findSerIMU(string serNum)
        {
            for (var i = 0; i < caliIMUData.Count; i++)
            {
                if (caliIMUData[i].Key == serNum)
                {
                    return i;
                }
            }

            return -1;
        }

        private int findSerSticks(string serNum)
        {
            for (var i = 0; i < caliSticksData.Count; i++)
            {
                if (caliSticksData[i].Key == serNum)
                {
                    return i;
                }
            }

            return -1;
        }

        public void tooltip(string msg)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(tooltip), msg);
                return;
            }

            notifyIcon.Visible = true;
            notifyIcon.BalloonTipText = msg;
            notifyIcon.ShowBalloonTip(0);
        }

        public void setBatteryColor(Joycon j, int batteryLevel)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<Joycon, int>(setBatteryColor), j, batteryLevel);
                return;
            }

            foreach (var b in con)
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

        public void addController(Joycon j)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<Joycon>(addController), j);
                return;
            }

            var i = 0;
            foreach (var b in con)
            {
                if (!b.Enabled)
                {
                    Bitmap temp;
                    switch (j.type)
                    {
                        case Joycon.ControllerType.JOYCON:
                            if (j.isLeft)
                            {
                                temp = Resources.jc_left_s;
                            }
                            else
                            {
                                temp = Resources.jc_right_s;
                            }

                            break;
                        case Joycon.ControllerType.PRO:
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
                    b.Click += conBtnClick;
                    b.BackgroundImage = temp;

                    loc[i].Tag = b;
                    loc[i].Click += locBtnClickAsync;

                    break;
                }

                i++;
            }
        }

        public void removeController(Joycon j)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<Joycon>(removeController), j);
                return;
            }

            foreach (var b in con)
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

        public void joinJoycon(Joycon j, Joycon other)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<Joycon, Joycon>(joinJoycon), j, other);
                return;
            }

            foreach (var b in con)
            {
                if (b.Tag == j || b.Tag == other)
                {
                    var currentJoycon = b.Tag == j ? j : other;
                    b.BackgroundImage = currentJoycon.isLeft ? Resources.jc_left : Resources.jc_right;
                }
            }
        }
    }
}
