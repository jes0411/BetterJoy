using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using BetterJoyForCemu.Collections;
using Nefarius.Drivers.HidHide;
using Nefarius.ViGEm.Client;
using static BetterJoyForCemu._3rdPartyControllers;
using static BetterJoyForCemu.HIDapi;

namespace BetterJoyForCemu {
    public class JoyconManager {
        public bool isRunning = false;
        public bool EnableIMU = true;
        public bool EnableLocalize = false;

        private const ushort vendor_id = 0x57e;
        private const ushort product_l = 0x2006;
        private const ushort product_r = 0x2007;
        private const ushort product_pro = 0x2009;
        private const ushort product_snes = 0x2017;

        private const double defaultCheckInterval = 5000;
        private const double fastCheckInterval = 500;

        public ConcurrentList<Joycon> j { get; private set; } // Array of all connected Joy-Cons
        static JoyconManager instance;

        public MainForm form;

        private System.Timers.Timer controllerCheck;
        private bool dropControllers = false;
        private readonly object checkControllerLock = new object();

        public static JoyconManager Instance {
            get { return instance; }
        }

        public void Awake() {
            instance = this;
            j = new ConcurrentList<Joycon>();
            HIDapi.hid_init();
        }

        public void Start() {
            controllerCheck = new System.Timers.Timer();
            controllerCheck.Elapsed += CheckForNewControllersTime;
            controllerCheck.AutoReset = false;

            Task.Run(() => {
                CheckForNewControllersTrigger(true);
                isRunning = true;
            });
        }

        bool ControllerAlreadyAdded(string path) {
            foreach (Joycon v in j)
                if (v.path == path)
                    return true;
            return false;
        }

        void CleanUp() { // removes dropped controllers from list
            if (dropControllers) {
                foreach (Joycon v in j)
                    v.Drop();
                dropControllers = false;
            }
            List<Joycon> rem = new List<Joycon>();
            foreach (Joycon joycon in j) {
                if (joycon.state == Joycon.state_.DROPPED) {
                    if (joycon.other != null)
                        joycon.other.other = null; // The other of the other is the joycon itself

                    joycon.Detach();
                    rem.Add(joycon);

                    form.removeController(joycon);
                    form.AppendTextBox($"Removed dropped {joycon.getControllerName()}. Can be reconnected.");
                }
            }

            foreach (Joycon v in rem)
                j.Remove(v);
        }

        private void CheckForNewControllersTime(Object source, ElapsedEventArgs e) {
            CheckForNewControllersTrigger();
        }

        private void CheckForNewControllersTrigger(bool forceScan = false) {
            if (!Monitor.TryEnter(checkControllerLock)) {
                return;
            }

            try {
                CleanUp();

                double checkInterval = defaultCheckInterval;
                if (Config.IntValue("ProgressiveScan") == 1 || forceScan) {
                    checkInterval = CheckForNewControllers();
                }

                setControllerCheckInterval(checkInterval);
                controllerCheck.Start();
            } finally {
                Monitor.Exit(checkControllerLock);
            }
        }

        private void setControllerCheckInterval(double interval) {
            if (interval == controllerCheck.Interval) {
                return;
            }
            // Avoid triggering the elapsed event when changing the interval (see note in https://learn.microsoft.com/en-us/dotnet/api/system.timers.timer.interval?view=net-7.0)
            bool wasEnabled = controllerCheck.Enabled;
            if (!wasEnabled) {
                controllerCheck.Enabled = true;
            }
            controllerCheck.Interval = interval;
            if (!wasEnabled) {
                controllerCheck.Enabled = false;
            }
        }

        private ushort TypeToProdId(byte type) {
            switch (type) {
                case 1:
                    return product_pro;
                case 2:
                    return product_l;
                case 3:
                    return product_r;
            }
            return 0;
        }

        public double CheckForNewControllers() {
            // move all code for initializing devices here and well as the initial code from Start()
            bool isLeft = false;
            IntPtr ptr = HIDapi.hid_enumerate(0x0, 0x0);
            IntPtr top_ptr = ptr;

            bool foundNew = false;
            for (hid_device_info enumerate; ptr != IntPtr.Zero; ptr = enumerate.next) {
                enumerate = (hid_device_info)Marshal.PtrToStructure(ptr, typeof(hid_device_info));

                if (enumerate.serial_number == null) {
                    continue;
                }

                bool validController = (enumerate.product_id == product_l || enumerate.product_id == product_r ||
                                        enumerate.product_id == product_pro || enumerate.product_id == product_snes) && enumerate.vendor_id == vendor_id;

                // check list of custom controllers specified
                SController thirdParty = null;
                foreach (SController v in Program.thirdPartyCons) {
                    if (enumerate.vendor_id == v.vendor_id && enumerate.product_id == v.product_id && enumerate.serial_number == v.serial_number) {
                        validController = true;
                        thirdParty = v;
                        break;
                    }
                }

                ushort prod_id = thirdParty == null ? enumerate.product_id : TypeToProdId(thirdParty.type);
                if (prod_id == 0) {
                    ptr = enumerate.next; // controller was not assigned a type, but advance ptr anyway
                    continue;
                }

                if (validController && !ControllerAlreadyAdded(enumerate.path)) {
                    switch (prod_id) {
                        case product_l:
                            isLeft = true;
                            form.AppendTextBox("Left Joy-Con connected."); break;
                        case product_r:
                            isLeft = false;
                            form.AppendTextBox("Right Joy-Con connected."); break;
                        case product_pro:
                            isLeft = true;
                            form.AppendTextBox("Pro controller connected."); break;
                        case product_snes:
                            isLeft = true;
                            form.AppendTextBox("SNES controller connected."); break;
                        default:
                            form.AppendTextBox("Non Joy-Con Nintendo input device skipped."); break;
                    }

                    IntPtr handle = HIDapi.hid_open_path(enumerate.path);
                    if (handle == IntPtr.Zero) {
                        form.AppendTextBox("Unable to open path to device - are you using the correct (64 vs 32-bit) version for your PC?");
                        break;
                    }

                    HIDapi.hid_set_nonblocking(handle, 1);

                    // Add controller to block-list for HidHide
                    Program.addDeviceToBlocklist(handle);

                    Joycon.ControllerType type = Joycon.ControllerType.JOYCON;
                    if (prod_id == product_pro) {
                        type = Joycon.ControllerType.PRO;
                    } else if (prod_id == product_snes) {
                        type = Joycon.ControllerType.SNES;
                    }

                    int indexController = j.Count;
                    bool isUSB = enumerate.bus_type == BusType.USB;
                    Joycon controller = new Joycon(handle, EnableIMU, EnableLocalize && EnableIMU, 0.05f, isLeft, enumerate.path, enumerate.serial_number, isUSB, indexController, type, thirdParty != null);
                    controller.form = form;
                    
                    byte[] mac = new byte[6];
                    try {
                        for (int n = 0; n < 6; n++)
                            mac[n] = byte.Parse(enumerate.serial_number.AsSpan(n * 2, 2), System.Globalization.NumberStyles.HexNumber);
                    } catch (Exception /*e*/) {
                        // could not parse mac address
                    }
                    controller.PadMacAddress = new PhysicalAddress(mac);

                    j.Add(controller);
                    if (indexController < 4) {
                        form.addController(controller);
                    }
                    foundNew = true;
                }
            }
            HIDapi.hid_free_enumeration(top_ptr);

            if (foundNew) { // attempt to auto join-up joycons on connection
                Joycon temp = null;
                foreach (Joycon v in j) {
                    // Do not attach two controllers if they are either:
                    // - Not a Joycon
                    // - Already attached to another Joycon (that isn't itself)
                    if (v.isPro || (v.other != null && v.other != v)) {
                        continue;
                    }

                    // Otherwise, iterate through and find the Joycon with the lowest
                    // id that has not been attached already (Does not include self)
                    if (temp == null)
                        temp = v;
                    else if (temp.isLeft != v.isLeft && v.other == null) {
                        temp.other = v;
                        v.other = temp;

                        temp.DisconnectViGEm();
                        form.joinJoycon(v, temp);

                        temp = null;    // repeat
                    }
                }
            }

            bool dropped = false;
            bool on = Boolean.Parse(ConfigurationManager.AppSettings["HomeLEDOn"]);
            foreach (Joycon jc in j) { // Connect device straight away
                if (jc.state == Joycon.state_.NOT_ATTACHED) {
                    try {
                        jc.ConnectViGEm();
                        jc.Attach();
                    } catch (Exception e) {
                        jc.Drop();
                        dropped = true;

                        form.AppendTextBox($"Could not connect {jc.getControllerName()} ({e.Message}). Dropped.");
                        continue;
                    }

                    jc.SetHomeLight(on);

                    jc.Begin();
                    if (form.allowCalibration) {
                        jc.getActiveIMUData();
                        jc.getActiveSticksData();
                    }
                }
            }
            double checkInterval = dropped ? fastCheckInterval : defaultCheckInterval;
            return checkInterval;
        }

        public void OnApplicationQuit() {
            lock (checkControllerLock) {
                controllerCheck?.Stop();
                controllerCheck?.Dispose();
                isRunning = false;
            }

            bool powerOff = Boolean.Parse(ConfigurationManager.AppSettings["AutoPowerOff"]);
            foreach (Joycon v in j) {
                if (powerOff)
                    v.PowerOff();

                v.Detach();
            }
            HIDapi.hid_exit();
        }
    }

    class Program {
        public static PhysicalAddress btMAC = new PhysicalAddress(new byte[] { 0, 0, 0, 0, 0, 0 });
        public static UdpServer server;

        public static ViGEmClient emClient;

        public static JoyconManager mgr;

        static MainForm form;

        public static ConcurrentList<SController> thirdPartyCons = new ConcurrentList<SController>();

        private static bool useHidHide = Boolean.Parse(ConfigurationManager.AppSettings["UseHidHide"]);

        private static WindowsInput.Events.Sources.IKeyboardEventSource keyboard;
        private static WindowsInput.Events.Sources.IMouseEventSource mouse;

        private static HashSet<string> blockedDeviceInstances = new HashSet<string>();

        private static bool isRunning = false;

        public static void Start() {
            useHidHide = startHidHide();

            if (Boolean.Parse(ConfigurationManager.AppSettings["ShowAsXInput"]) || Boolean.Parse(ConfigurationManager.AppSettings["ShowAsDS4"])) {
                try {
                    emClient = new ViGEmClient(); // Manages emulated XInput
                } catch (Nefarius.ViGEm.Client.Exceptions.VigemBusNotFoundException) {
                    form.AppendTextBox("Could not start VigemBus. Make sure drivers are installed correctly.");
                }
            }

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                // Get local BT host MAC
                if (nic.NetworkInterfaceType != NetworkInterfaceType.FastEthernetFx && nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) {
                    if (nic.Name.Split()[0] == "Bluetooth") {
                        btMAC = nic.GetPhysicalAddress();
                    }
                }
            }

            List<SController> controllers = _3rdPartyControllers.GetSaved3rdPartyControllers();
            update3rdPartyControllers(controllers);

            mgr = new JoyconManager();
            mgr.form = form;
            mgr.Awake();

            server = new UdpServer(mgr.j);
            server.Form = form;

            server.Start(IPAddress.Parse(ConfigurationManager.AppSettings["IP"]), Int32.Parse(ConfigurationManager.AppSettings["Port"]));

            // Capture keyboard + mouse events for binding's sake
            keyboard = WindowsInput.Capture.Global.KeyboardAsync();
            keyboard.KeyEvent += Keyboard_KeyEvent;
            mouse = WindowsInput.Capture.Global.MouseAsync();
            mouse.MouseEvent += Mouse_MouseEvent;

            form.AppendTextBox("All systems go");
            mgr.Start();
            isRunning = true;
        }

        private static bool startHidHide() {
            if (!useHidHide) {
                return false;
            }

            HidHideControlService hidHideService = new HidHideControlService();
            if (!hidHideService.IsInstalled) {
                form.AppendTextBox("HidHide is not installed.");
                return false;
            }

            try {
                hidHideService.IsAppListInverted = false;
            } catch (Exception /*e*/) {
                form.AppendTextBox("Unable to set HidHide in whitelist mode.");
                return false;
            }

            //if (Boolean.Parse(ConfigurationManager.AppSettings["PurgeAffectedDevices"])) {
            //    try {
            //        hidHideService.ClearBlockedInstancesList();
            //    } catch (Exception /*e*/) {
            //        form.AppendTextBox("Unable to purge blacklisted devices.");
            //        return false;
            //    }
            //}

            try {
                if (Boolean.Parse(ConfigurationManager.AppSettings["PurgeWhitelist"])) {
                    hidHideService.ClearApplicationsList();
                }
                hidHideService.AddApplicationPath(Environment.ProcessPath);
            } catch (Exception /*e*/) {
                form.AppendTextBox("Unable to add program to whitelist.");
                return false;
            }

            try {
                hidHideService.IsActive = true;
            } catch (Exception /*e*/) {
                form.AppendTextBox("Unable to hide devices.");
                return false;
            }

            form.AppendTextBox("HidHide is enabled.");
            return true;
        }

        public static void addDeviceToBlocklist(IntPtr handle) {
            if (!Program.useHidHide) {
                return;
            }
            try {
                List<string> devices = new List<string>();

                string instance = HIDapi.GetInstance(handle);
                if (instance.Length == 0) {
                    form.AppendTextBox("Unable to get device instance.");
                } else {
                    devices.Add(instance);
                }

                string parentInstance = HIDapi.GetParentInstance(handle);
                if (parentInstance.Length == 0) {
                    form.AppendTextBox("Unable to get device parent instance.");
                } else {
                    devices.Add(parentInstance);
                }

                if (devices.Count == 0) {
                    throw new Exception("hidapi error");
                }

                blockDeviceInstances(devices);
            } catch (Exception e) {
                form.AppendTextBox($"Unable to add controller to block-list ({e.Message}).");
            }
        }

        public static void blockDeviceInstances(IList<string> instances) {
            HidHideControlService hidHideService = new HidHideControlService();
            foreach (var instance in instances) {
                hidHideService.AddBlockedInstanceId(instance);
                blockedDeviceInstances.Add(instance);
            }
        }

        private static void Mouse_MouseEvent(object sender, WindowsInput.Events.Sources.EventSourceEventArgs<WindowsInput.Events.Sources.MouseEvent> e) {
            if (e.Data.ButtonDown != null) {
                string res_val = Config.Value("reset_mouse");
                if (res_val.StartsWith("mse_"))
                    if ((int)e.Data.ButtonDown.Button == Int32.Parse(res_val.AsSpan(4)))
                        WindowsInput.Simulate.Events().MoveTo(Screen.PrimaryScreen.Bounds.Width / 2, Screen.PrimaryScreen.Bounds.Height / 2).Invoke();

                res_val = Config.Value("active_gyro");
                if (res_val.StartsWith("mse_"))
                    if ((int)e.Data.ButtonDown.Button == Int32.Parse(res_val.AsSpan(4)))
                        foreach (var i in mgr.j)
                            i.active_gyro = true;
            }

            if (e.Data.ButtonUp != null) {
                string res_val = Config.Value("active_gyro");
                if (res_val.StartsWith("mse_"))
                    if ((int)e.Data.ButtonUp.Button == Int32.Parse(res_val.AsSpan(4)))
                        foreach (var i in mgr.j)
                            i.active_gyro = false;
            }
        }

        private static void Keyboard_KeyEvent(object sender, WindowsInput.Events.Sources.EventSourceEventArgs<WindowsInput.Events.Sources.KeyboardEvent> e) {
            if (e.Data.KeyDown != null) {
                string res_val = Config.Value("reset_mouse");
                if (res_val.StartsWith("key_"))
                    if ((int)e.Data.KeyDown.Key == Int32.Parse(res_val.AsSpan(4)))
                        WindowsInput.Simulate.Events().MoveTo(Screen.PrimaryScreen.Bounds.Width / 2, Screen.PrimaryScreen.Bounds.Height / 2).Invoke();

                res_val = Config.Value("active_gyro");
                if (res_val.StartsWith("key_"))
                    if ((int)e.Data.KeyDown.Key == Int32.Parse(res_val.AsSpan(4)))
                        foreach (var i in mgr.j)
                            i.active_gyro = true;
            }

            if (e.Data.KeyUp != null) {
                string res_val = Config.Value("active_gyro");
                if (res_val.StartsWith("key_"))
                    if ((int)e.Data.KeyUp.Key == Int32.Parse(res_val.AsSpan(4)))
                        foreach (var i in mgr.j)
                            i.active_gyro = false;
            }
        }

        public static void Stop() {
            if (!isRunning) {
                return;
            }
            isRunning = false;

            stopHidHide();

            keyboard?.Dispose();
            mouse?.Dispose();
            mgr?.OnApplicationQuit();
            server?.Stop();
        }

        public static void allowAnotherInstance() {
            mutexInstance?.Close(); 
        }

        public static void stopHidHide() {
            if (!useHidHide) {
                return;
            }

            HidHideControlService hidHideService = new HidHideControlService();

            try {
                hidHideService.RemoveApplicationPath(Environment.ProcessPath);
            } catch (Exception /*e*/) {
                form.AppendTextBox("Unable to remove program from whitelist.");
            }

            if (Boolean.Parse(ConfigurationManager.AppSettings["PurgeAffectedDevices"])) {
                try {
                    foreach (var instance in blockedDeviceInstances) {
                        hidHideService.RemoveBlockedInstanceId(instance);
                    }
                } catch (Exception /*e*/) {
                    form.AppendTextBox("Unable to purge blacklisted devices.");
                }
            }

            try {
                hidHideService.IsActive = false;
            } catch (Exception /*e*/) {
                form.AppendTextBox("Unable to disable HidHide.");
            }
        }

        public static void update3rdPartyControllers(List<SController> controllers) {
            thirdPartyCons.Set(controllers);
        }

        private static string appGuid = "1bf709e9-c133-41df-933a-c9ff3f664c7b"; // randomly-generated
        private static Mutex mutexInstance = null;

        static void Main(string[] args) {

            // Setting the culturesettings so float gets parsed correctly
            CultureInfo.CurrentCulture = new CultureInfo("en-US", false);

            // Set the correct DLL for the current OS
            SetupDlls();

            using (mutexInstance = new Mutex(false, "Global\\" + appGuid)) {
                if (!mutexInstance.WaitOne(0, false)) {
                    MessageBox.Show("Instance already running.", "BetterJoy");
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                form = new MainForm();
                Application.Run(form);
            }
        }

        static void SetupDlls() {
            string archPath = $"{AppDomain.CurrentDomain.BaseDirectory}{(Environment.Is64BitProcess ? "x64" : "x86")}\\";
            string pathVariable = Environment.GetEnvironmentVariable("PATH");
            pathVariable = $"{archPath};{pathVariable}";
            Environment.SetEnvironmentVariable("PATH", pathVariable);
        }
    }
}
