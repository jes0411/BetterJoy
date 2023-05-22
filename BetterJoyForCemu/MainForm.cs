using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BetterJoyForCemu {
    public partial class MainForm : Form {
        public bool useControllerStickCalibration;
        public bool nonOriginal;
        public bool allowCalibration = Boolean.Parse(ConfigurationManager.AppSettings["AllowCalibration"]);
        public List<Button> con, loc;
        public bool calibrateIMU;
        public bool calibrateSticks;
        public List<KeyValuePair<string, float[]>> caliIMUData;
        public List<KeyValuePair<string, ushort[]>> caliSticksData;
        private Timer countDown;
        private int count;
        public List<int> xG, yG, zG, xA, yA, zA;
        public List<ushort> xS1, yS1, xS2, yS2;
        public bool shakeInputEnabled = Boolean.Parse(ConfigurationManager.AppSettings["EnableShakeInput"]);
        public float shakeSesitivity = float.Parse(ConfigurationManager.AppSettings["ShakeInputSensitivity"]);
        public float shakeDelay = float.Parse(ConfigurationManager.AppSettings["ShakeInputDelay"]);

        public enum NonOriginalController : int {
            Disabled = 0,
            DefaultCalibration = 1,
            ControllerCalibration = 2,
        }

        public MainForm() {
            xG = new List<int>(); yG = new List<int>(); zG = new List<int>();
            xA = new List<int>(); yA = new List<int>(); zA = new List<int>();
            caliIMUData = new List<KeyValuePair<string, float[]>> {
                new KeyValuePair<string, float[]>("0", new float[6] {0,0,0,-710,0,0})
            };

            xS1 = new List<ushort>(); yS1 = new List<ushort>();
            xS2 = new List<ushort>(); yS2 = new List<ushort>();
            caliSticksData = new List<KeyValuePair<string, ushort[]>> {
                new KeyValuePair<string, ushort[]>("0", new ushort[12] {2048,2048,2048,2048,2048,2048,2048,2048,2048,2048,2048,2048})
            };
            SetNonOriginalControllerSettings();

            InitializeComponent();

            if (!allowCalibration)
                AutoCalibrate.Hide();

            con = new List<Button> { con1, con2, con3, con4 };
            loc = new List<Button> { loc1, loc2, loc3, loc4 };

            //list all options
            string[] myConfigs = ConfigurationManager.AppSettings.AllKeys;
            Size childSize = new Size(150, 20);
            for (int i = 0; i != myConfigs.Length; i++) {
                settingsTable.RowCount++;
                settingsTable.Controls.Add(new Label() { Text = myConfigs[i], TextAlign = ContentAlignment.BottomLeft, AutoEllipsis = true, Size = childSize }, 0, i);

                var value = ConfigurationManager.AppSettings[myConfigs[i]];
                Control childControl;
                if (value == "true" || value == "false") {
                    childControl = new CheckBox() { Checked = Boolean.Parse(value), Size = childSize };
                } else {
                    childControl = new TextBox() { Text = value, Size = childSize };
                }

                childControl.MouseClick += cbBox_Changed;
                settingsTable.Controls.Add(childControl, 1, i);
            }
        }

        private void SetNonOriginalControllerSettings() {
            Enum.TryParse(ConfigurationManager.AppSettings["NonOriginalController"], true, out NonOriginalController nonOriginalController);
            switch (nonOriginalController) {
                case NonOriginalController.Disabled:
                    nonOriginal = false;
                    break;
                case NonOriginalController.DefaultCalibration:
                case NonOriginalController.ControllerCalibration:
                    nonOriginal = true;
                    break;
            }
            switch (nonOriginalController) {
                case NonOriginalController.Disabled:
                case NonOriginalController.ControllerCalibration:
                    useControllerStickCalibration = true;
                    break;
                case NonOriginalController.DefaultCalibration:
                    useControllerStickCalibration = false;
                    break;
            }
        }

        private void HideToTray() {
            this.WindowState = FormWindowState.Minimized;
            notifyIcon.Visible = true;
            notifyIcon.BalloonTipText = "Double click the tray icon to maximise!";
            notifyIcon.ShowBalloonTip(0);
            this.ShowInTaskbar = false;
            this.Hide();
        }

        private void ShowFromTray() {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.Icon = Properties.Resources.betterjoyforcemu_icon;
            notifyIcon.Visible = false;
        }

        private void MainForm_Resize(object sender, EventArgs e) {
            if (this.WindowState == FormWindowState.Minimized) {
                HideToTray();
            }
        }

        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e) {
            ShowFromTray();
        }

        private void MainForm_Load(object sender, EventArgs e) {
            Config.Init(caliIMUData, caliSticksData);

            passiveScanBox.Checked = Config.IntValue("ProgressiveScan") == 1;
            startInTrayBox.Checked = Config.IntValue("StartInTray") == 1;

            if (Config.IntValue("StartInTray") == 1) {
                HideToTray();
            } else {
                ShowFromTray();
            }
            SystemEvents.PowerModeChanged += OnPowerChange;
            Program.Start();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e) {
            try {
                Program.Stop();
                SystemEvents.PowerModeChanged -= OnPowerChange;
                Environment.Exit(0);
            } catch { }
        }

        private void OnPowerChange(object s, PowerModeChangedEventArgs e) {
            switch (e.Mode) {
                case PowerModes.Resume:
                    AppendTextBox("Resume session.\r\n");
                    Task.Run(() => {
                        Program.mgr.onResume();
                    });
                    break;
                case PowerModes.Suspend:
                    AppendTextBox("Suspend session.\r\n");
                    break;
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e) { // this does not work, for some reason. Fix before release
            try {
                Program.Stop();
                Close();
                Environment.Exit(0);
            } catch { }
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
            donationLink.LinkVisited = true;
            System.Diagnostics.Process.Start("http://paypal.me/DavidKhachaturov/5");
        }

        private void passiveScanBox_CheckedChanged(object sender, EventArgs e) {
            Config.SetValue("ProgressiveScan", passiveScanBox.Checked ? "1" : "0");
            Config.Save();
        }

        public void AppendTextBox(string value) { // https://stackoverflow.com/questions/519233/writing-to-a-textbox-from-another-thread
            if (InvokeRequired) {
                BeginInvoke(new Action<string>(AppendTextBox), new object[] { value });
                return;
            }
            console.AppendText(value);
        }

        bool toRumble = Boolean.Parse(ConfigurationManager.AppSettings["EnableRumble"]);
        bool showAsXInput = Boolean.Parse(ConfigurationManager.AppSettings["ShowAsXInput"]);
        bool showAsDS4 = Boolean.Parse(ConfigurationManager.AppSettings["ShowAsDS4"]);

        public async void locBtnClickAsync(object sender, EventArgs e) {
            Button bb = sender as Button;

            if (bb.Tag.GetType() == typeof(Button)) {
                Button button = bb.Tag as Button;

                if (button.Tag.GetType() == typeof(Joycon)) {
                    Joycon v = (Joycon)button.Tag;
                    v.SetRumble(160.0f, 320.0f, 1.0f);
                    await Task.Delay(300);
                    v.SetRumble(160.0f, 320.0f, 0);
                }
            }
        }

        bool doNotRejoin = Boolean.Parse(ConfigurationManager.AppSettings["DoNotRejoinJoycons"]);

        public void conBtnClick(object sender, EventArgs e) {
            Button button = sender as Button;

            if (button.Tag.GetType() == typeof(Joycon)) {
                Joycon v = (Joycon)button.Tag;

                if (v.other == null && !v.isPro) { // needs connecting to other joycon (so messy omg)
                    bool succ = false;

                    if (Program.mgr.j.Count == 1 || doNotRejoin) { // when want to have a single joycon in vertical mode
                        v.other = v; // hacky; implement check in Joycon.cs to account for this
                        succ = true;
                    } else {
                        foreach (Joycon jc in Program.mgr.j) {
                            if (!jc.isPro && jc.isLeft != v.isLeft && jc != v && jc.other == null) {
                                v.other = jc;
                                jc.other = v;

                                v.DisconnectViGEm();

                                // setting the other joycon's button image
                                foreach (Button b in con) {
                                    if (b.Tag == jc) {
                                        b.BackgroundImage = jc.isLeft ? Properties.Resources.jc_left : Properties.Resources.jc_right;
                                    }
                                }
                                succ = true;
                                break;
                            }
                        }
                    }

                    if (succ) {
                        foreach (Button b in con) {
                            if (b.Tag == v) {
                                b.BackgroundImage = v.isLeft ? Properties.Resources.jc_left : Properties.Resources.jc_right;
                            }
                        }
                    }
                } else if (v.other != null && !v.isPro) { // needs disconnecting from other joycon
                    ReenableViGEm(v);
                    ReenableViGEm(v.other);

                    button.BackgroundImage = v.isLeft ? Properties.Resources.jc_left_s : Properties.Resources.jc_right_s;

                    foreach (Button b in con) {
                        if (b.Tag == v.other) {
                            b.BackgroundImage = v.other.isLeft ? Properties.Resources.jc_left_s : Properties.Resources.jc_right_s;
                        }
                    }
                    v.other.other = null;
                    v.other = null;
                }
            }
        }

        private void startInTrayBox_CheckedChanged(object sender, EventArgs e) {
            Config.SetValue("StartInTray", startInTrayBox.Checked ? "1" : "0");
            Config.Save();
        }

        private void btn_open3rdP_Click(object sender, EventArgs e) {
            _3rdPartyControllers partyForm = new _3rdPartyControllers();
            partyForm.ShowDialog();
        }

        private void settingsApply_Click(object sender, EventArgs e) {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;

            for (int row = 0; row < ConfigurationManager.AppSettings.AllKeys.Length; row++) {
                var valCtl = settingsTable.GetControlFromPosition(1, row);
                var KeyCtl = settingsTable.GetControlFromPosition(0, row).Text;

                if (valCtl.GetType() == typeof(CheckBox) && settings[KeyCtl] != null) {
                    settings[KeyCtl].Value = ((CheckBox)valCtl).Checked.ToString().ToLower();
                } else if (valCtl.GetType() == typeof(TextBox) && settings[KeyCtl] != null) {
                    settings[KeyCtl].Value = ((TextBox)valCtl).Text.ToLower();
                }
            }

            try {
                configFile.Save(ConfigurationSaveMode.Modified);
            } catch (ConfigurationErrorsException) {
                AppendTextBox("Error writing app settings.\r\n");
            }

            ConfigurationManager.AppSettings["AutoPowerOff"] = "false";  // Prevent joycons poweroff when applying settings
            Program.Stop();
            Restart();
        }

        private void Restart() {
            ProcessStartInfo Info = new ProcessStartInfo();
            Info.Arguments = "/c ping 127.0.0.1 -n 2 && \"" + Application.ExecutablePath + "\"";
            Info.WorkingDirectory = Environment.CurrentDirectory;
            Info.WindowStyle = ProcessWindowStyle.Hidden;
            Info.CreateNoWindow = true;
            Info.FileName = "cmd.exe";
            Process.Start(Info);
            Application.Exit();
        }

        private void ReenableViGEm(Joycon v) {
            if (showAsXInput && v.out_xbox == null) {
                v.out_xbox = new Controller.OutputControllerXbox360();

                if (toRumble)
                    v.out_xbox.FeedbackReceived += v.ReceiveRumble;
                v.out_xbox.Connect();
            }

            if (showAsDS4 && v.out_ds4 == null) {
                v.out_ds4 = new Controller.OutputControllerDualShock4();

                if (toRumble)
                    v.out_ds4.FeedbackReceived += v.Ds4_FeedbackReceived;
                v.out_ds4.Connect();
            }
        }

        private void foldLbl_Click(object sender, EventArgs e) {
            rightPanel.Visible = !rightPanel.Visible;
            foldLbl.Text = rightPanel.Visible ? "<" : ">";
        }

        private void cbBox_Changed(object sender, EventArgs e) {
            var coord = settingsTable.GetPositionFromControl(sender as Control);

            var valCtl = settingsTable.GetControlFromPosition(coord.Column, coord.Row);
            var KeyCtl = settingsTable.GetControlFromPosition(coord.Column - 1, coord.Row).Text;

            try {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.AppSettings.Settings;
                if (valCtl.GetType() == typeof(CheckBox) && settings[KeyCtl] != null) {
                    settings[KeyCtl].Value = ((CheckBox)valCtl).Checked.ToString().ToLower();
                } else if (valCtl.GetType() == typeof(TextBox) && settings[KeyCtl] != null) {
                    settings[KeyCtl].Value = ((TextBox)valCtl).Text.ToLower();
                }

                if (KeyCtl == "HomeLEDOn") {
                    bool on = settings[KeyCtl].Value.ToLower() == "true";
                    foreach (Joycon j in Program.mgr.j) {
                        j.SetHomeLight(on);
                    }
                }

                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            } catch (ConfigurationErrorsException) {
                AppendTextBox("Error writing app settings\r\n");
                Trace.WriteLine(String.Format("rw {0}, column {1}, {2}, {3}", coord.Row, coord.Column, sender.GetType(), KeyCtl));
            }
        }
        private void StartCalibrate(object sender, EventArgs e) {
            if (Program.mgr.j.Count == 0) {
                this.console.Text = "Please connect a single pro controller.";
                return;
            }
            if (Program.mgr.j.Count > 1) {
                this.console.Text = "Please calibrate one controller at a time (disconnect others).";
                return;
            }
            this.AutoCalibrate.Enabled = false;
            countDown = new Timer();
            this.count = 4;
            this.CountDownIMU(null, null);
            countDown.Tick += new EventHandler(CountDownIMU);
            countDown.Interval = 1000;
            countDown.Enabled = true;
        }

        private void StartGetIMUData() {
            this.xG.Clear(); this.yG.Clear(); this.zG.Clear();
            this.xA.Clear(); this.yA.Clear(); this.zA.Clear();
            countDown = new Timer();
            this.count = 3;
            this.calibrateIMU = true;
            countDown.Tick += new EventHandler(CalcIMUData);
            countDown.Interval = 1000;
            countDown.Enabled = true;
        }

        private void btn_reassign_open_Click(object sender, EventArgs e) {
            Reassign mapForm = new Reassign();
            mapForm.ShowDialog();
        }

        private void CountDownIMU(object sender, EventArgs e) {
            if (this.count == 0) {
                this.console.Text = "Calibrating IMU...";
                countDown.Stop();
                this.StartGetIMUData();
            } else {
                this.console.Text = "Please keep the controller flat." + "\r\n";
                this.console.Text += "Calibration will start in " + this.count + " seconds.";
                this.count--;
            }
        }
        private void CalcIMUData(object sender, EventArgs e) {
            if (this.count == 0) {
                countDown.Stop();
                this.calibrateIMU = false;
                string serNum = Program.mgr.j.First().serial_number;
                int serIndex = this.findSerIMU(serNum);
                float[] Arr = new float[6] { 0, 0, 0, 0, 0, 0 };
                if (serIndex == -1) {
                    this.caliIMUData.Add(new KeyValuePair<string, float[]>(
                         serNum,
                         Arr
                    ));
                } else {
                    Arr = this.caliIMUData[serIndex].Value;
                }
                Random rnd = new Random();
                Arr[0] = (float)quickselect_median(this.xG, rnd.Next);
                Arr[1] = (float)quickselect_median(this.yG, rnd.Next);
                Arr[2] = (float)quickselect_median(this.zG, rnd.Next);
                Arr[3] = (float)quickselect_median(this.xA, rnd.Next);
                Arr[4] = (float)quickselect_median(this.yA, rnd.Next);
                Arr[5] = (float)quickselect_median(this.zA, rnd.Next) - 4010; //Joycon.cs acc_sen 16384
                this.console.Text += "IMU Calibration completed!!!" + "\r\n";
                Config.SaveCaliIMUData(this.caliIMUData);
                Program.mgr.j.First().getActiveIMUData();

                countDown = new Timer();
                this.count = 5;
                this.CountDownSticksCenter(null, null);
                countDown.Tick += new EventHandler(CountDownSticksCenter);
                countDown.Interval = 1000;
                countDown.Enabled = true;
            } else {
                this.count--;
            }

        }
        private void CountDownSticksCenter(object sender, EventArgs e) {
            if (this.count == 0) {
                this.console.Text = "Calibrating Sticks center position...";
                countDown.Stop();
                this.StartGetSticksCenterData();
            } else {
                this.console.Text = "Please keep the sticks at the center position." + "\r\n";
                this.console.Text += "Calibration will start in " + this.count + " seconds.";
                this.count--;
            }
        }
        private void StartGetSticksCenterData() {
            this.xS1.Clear(); this.yS1.Clear();
            this.xS2.Clear(); this.yS2.Clear();
            countDown = new Timer();
            this.count = 3;
            this.calibrateSticks = true;
            countDown.Tick += new EventHandler(CalcSticksCenterData);
            countDown.Interval = 1000;
            countDown.Enabled = true;
        }
        private void CalcSticksCenterData(object sender, EventArgs e) {
            if (this.count == 0) {
                countDown.Stop();
                this.calibrateSticks = false;
                string serNum = Program.mgr.j.First().serial_number;
                int serIndex = this.findSerSticks(serNum);
                const int stickCaliSize = 6;
                ushort[] Arr = new ushort[stickCaliSize * 2] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                if (serIndex == -1) {
                    this.caliSticksData.Add(new KeyValuePair<string, ushort[]>(
                         serNum,
                         Arr
                    ));
                } else {
                    Arr = this.caliSticksData[serIndex].Value;
                }
                Random rnd = new Random();
                Arr[2] = (ushort)Math.Round(quickselect_median(this.xS1.ConvertAll(x => (int) x), rnd.Next));
                Arr[3] = (ushort)Math.Round(quickselect_median(this.yS1.ConvertAll(x => (int) x), rnd.Next));
                Arr[2 + stickCaliSize] = (ushort)Math.Round(quickselect_median(this.xS2.ConvertAll(x => (int) x), rnd.Next));
                Arr[3 + stickCaliSize] = (ushort)Math.Round(quickselect_median(this.yS2.ConvertAll(x => (int) x), rnd.Next));
                this.console.Text += "Sticks center position Calibration completed!!!" + "\r\n";

                countDown = new Timer();
                this.count = 8;
                this.CountDownSticksMinMax(null, null);
                countDown.Tick += new EventHandler(CountDownSticksMinMax);
                countDown.Interval = 1000;
                countDown.Enabled = true;
            } else {
                this.count--;
            }
        }
        private void CountDownSticksMinMax(object sender, EventArgs e) {
            if (this.count == 0) {
                this.console.Text = "Calibrating Sticks min and max position...";
                countDown.Stop();
                this.StartGetSticksMinMaxData();
            } else {
                this.console.Text = "Please move the sticks in a circle when the calibration starts." + "\r\n";
                this.console.Text += "Calibration will start in " + this.count + " seconds.";
                this.count--;
            }
        }
        private void StartGetSticksMinMaxData() {
            this.xS1.Clear(); this.yS1.Clear();
            this.xS2.Clear(); this.yS2.Clear();
            countDown = new Timer();
            this.count = 5;
            this.calibrateSticks = true;
            countDown.Tick += new EventHandler(CalcSticksMinMaxData);
            countDown.Interval = 1000;
            countDown.Enabled = true;
        }
        private void CalcSticksMinMaxData(object sender, EventArgs e) {
            if (this.count == 0) {
                countDown.Stop();
                this.calibrateSticks = false;
                string serNum = Program.mgr.j.First().serial_number;
                int serIndex = this.findSerSticks(serNum);
                const int stickCaliSize = 6;
                ushort[] Arr = new ushort[stickCaliSize * 2] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                if (serIndex == -1) {
                    this.caliSticksData.Add(new KeyValuePair<string, ushort[]>(
                         serNum,
                         Arr
                    ));
                } else {
                    Arr = this.caliSticksData[serIndex].Value;
                }

                Arr[0] = (ushort) Math.Abs(this.xS1.Max() - Arr[2]);
                Arr[1] = (ushort) Math.Abs(this.yS1.Max() - Arr[3]);
                Arr[4] = (ushort) Math.Abs(Arr[2] - this.xS1.Min());
                Arr[5] = (ushort) Math.Abs(Arr[3] - this.yS1.Min());
                Arr[0 + stickCaliSize] = (ushort) Math.Abs(this.xS2.Max() - Arr[2 + stickCaliSize]);
                Arr[1 + stickCaliSize] = (ushort) Math.Abs(this.yS2.Max() - Arr[3 + stickCaliSize]);
                Arr[4 + stickCaliSize] = (ushort) Math.Abs(Arr[2 + stickCaliSize] - this.xS2.Min());
                Arr[5 + stickCaliSize] = (ushort) Math.Abs(Arr[3 + stickCaliSize] - this.yS2.Min());
                this.console.Text += "Sticks min and max position Calibration completed!!!" + "\r\n";
                Config.SaveCaliSticksData(this.caliSticksData);
                Program.mgr.j.First().getActiveSticksData();
                this.AutoCalibrate.Enabled = true;
            } else {
                this.count--;
            }
        }
        private double quickselect_median(List<int> l, Func<int, int> pivot_fn) {
            int ll = l.Count;
            if (ll % 2 == 1) {
                return this.quickselect(l, ll / 2, pivot_fn);
            } else {
                return 0.5 * (quickselect(l, ll / 2 - 1, pivot_fn) + quickselect(l, ll / 2, pivot_fn));
            }
        }

        private int quickselect(List<int> l, int k, Func<int, int> pivot_fn) {
            if (l.Count == 1 && k == 0) {
                return l[0];
            }
            int pivot = l[pivot_fn(l.Count)];
            List<int> lows = l.Where(x => x < pivot).ToList();
            List<int> highs = l.Where(x => x > pivot).ToList();
            List<int> pivots = l.Where(x => x == pivot).ToList();
            if (k < lows.Count) {
                return quickselect(lows, k, pivot_fn);
            } else if (k < (lows.Count + pivots.Count)) {
                return pivots[0];
            } else {
                return quickselect(highs, k - lows.Count - pivots.Count, pivot_fn);
            }
        }

        public float[] activeCaliIMUData(string serNum) {
            for (int i = 0; i < this.caliIMUData.Count; i++) {
                if (this.caliIMUData[i].Key == serNum) {
                    return this.caliIMUData[i].Value;
                }
            }
            return this.caliIMUData[0].Value;
        }
        public ushort[] activeCaliSticksData(string serNum) {
            for (int i = 0; i < this.caliSticksData.Count; i++) {
                if (this.caliSticksData[i].Key == serNum) {
                    return this.caliSticksData[i].Value;
                }
            }
            return this.caliSticksData[0].Value;
        }
        private int findSerIMU(string serNum) {
            for (int i = 0; i < this.caliIMUData.Count; i++) {
                if (this.caliIMUData[i].Key == serNum) {
                    return i;
                }
            }
            return -1;
        }
        private int findSerSticks(string serNum) {
            for (int i = 0; i < this.caliSticksData.Count; i++) {
                if (this.caliSticksData[i].Key == serNum) {
                    return i;
                }
            }
            return -1;
        }
    }
}
