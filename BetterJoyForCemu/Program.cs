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
using Nefarius.ViGEm.Client.Exceptions;
using WindowsInput;
using WindowsInput.Events.Sources;
using static BetterJoyForCemu._3rdPartyControllers;
using static BetterJoyForCemu.HIDApi;
using Timer = System.Timers.Timer;

namespace BetterJoyForCemu
{
    public class JoyconManager
    {
        private const ushort VendorId = 0x57e;
        private const ushort ProductL = 0x2006;
        private const ushort ProductR = 0x2007;
        private const ushort ProductPro = 0x2009;
        private const ushort ProductSNES = 0x2017;

        private const double DefaultCheckInterval = 5000;
        private const double FastCheckInterval = 500;
        private readonly object _checkControllerLock = new();

        private Timer _controllerCheck;
        private bool _dropControllers;
        public readonly bool EnableIMU = true;
        public readonly bool EnableLocalize = false;

        public MainForm Form;
        public bool IsRunning;

        public ConcurrentList<Joycon> J { get; private set; } // Array of all connected Joy-Cons

        public static JoyconManager Instance { get; private set; }

        public void Awake()
        {
            Instance = this;
            J = new ConcurrentList<Joycon>();
            hid_init();
        }

        public void Start()
        {
            _controllerCheck = new Timer();
            _controllerCheck.Elapsed += CheckForNewControllersTime;
            _controllerCheck.AutoReset = false;

            Task.Run(
                () =>
                {
                    CheckForNewControllersTrigger(true);
                    IsRunning = true;
                }
            );
        }

        private bool ControllerAlreadyAdded(string path)
        {
            foreach (var v in J)
            {
                if (v.Path == path)
                {
                    return true;
                }
            }

            return false;
        }

        private void CleanUp()
        {
            // removes dropped controllers from list
            if (_dropControllers)
            {
                foreach (var v in J)
                {
                    v.Drop();
                }

                _dropControllers = false;
            }

            var rem = new List<Joycon>();
            foreach (var joycon in J)
            {
                if (joycon.State == Joycon.Status.Dropped)
                {
                    if (joycon.Other != null)
                    {
                        joycon.Other.Other = null; // The other of the other is the joycon itself
                    }

                    joycon.Detach();
                    rem.Add(joycon);

                    Form.RemoveController(joycon);
                    Form.AppendTextBox($"Removed dropped {joycon.GetControllerName()}. Can be reconnected.");
                }
            }

            foreach (var v in rem)
            {
                J.Remove(v);
            }
        }

        private void CheckForNewControllersTime(object source, ElapsedEventArgs e)
        {
            CheckForNewControllersTrigger();
        }

        private void CheckForNewControllersTrigger(bool forceScan = false)
        {
            if (!Monitor.TryEnter(_checkControllerLock))
            {
                return;
            }

            try
            {
                CleanUp();

                var checkInterval = DefaultCheckInterval;
                if (Config.IntValue("ProgressiveScan") == 1 || forceScan)
                {
                    checkInterval = CheckForNewControllers();
                }

                SetControllerCheckInterval(checkInterval);
                _controllerCheck.Start();
            }
            finally
            {
                Monitor.Exit(_checkControllerLock);
            }
        }

        private void SetControllerCheckInterval(double interval)
        {
            if (interval == _controllerCheck.Interval)
            {
                return;
            }

            // Avoid triggering the elapsed event when changing the interval (see note in https://learn.microsoft.com/en-us/dotnet/api/system.timers.timer.interval?view=net-7.0)
            var wasEnabled = _controllerCheck.Enabled;
            if (!wasEnabled)
            {
                _controllerCheck.Enabled = true;
            }

            _controllerCheck.Interval = interval;
            if (!wasEnabled)
            {
                _controllerCheck.Enabled = false;
            }
        }

        private ushort TypeToProdId(byte type)
        {
            switch (type)
            {
                case 1:
                    return ProductPro;
                case 2:
                    return ProductL;
                case 3:
                    return ProductR;
            }

            return 0;
        }

        public double CheckForNewControllers()
        {
            // move all code for initializing devices here and well as the initial code from Start()
            var isLeft = false;
            var ptr = hid_enumerate(0x0, 0x0);
            var topPtr = ptr;

            var foundNew = false;
            for (HIDDeviceInfo enumerate; ptr != IntPtr.Zero; ptr = enumerate.Next)
            {
                enumerate = (HIDDeviceInfo)Marshal.PtrToStructure(ptr, typeof(HIDDeviceInfo));

                if (enumerate.SerialNumber == null)
                {
                    continue;
                }

                var validController = (enumerate.ProductId == ProductL || enumerate.ProductId == ProductR ||
                                       enumerate.ProductId == ProductPro || enumerate.ProductId == ProductSNES) &&
                                      enumerate.VendorId == VendorId;

                // check list of custom controllers specified
                SController thirdParty = null;
                foreach (var v in Program.ThirdPartyCons)
                {
                    if (enumerate.VendorId == v.VendorId && enumerate.ProductId == v.ProductId &&
                        enumerate.SerialNumber == v.SerialNumber)
                    {
                        validController = true;
                        thirdParty = v;
                        break;
                    }
                }

                var prodId = thirdParty == null ? enumerate.ProductId : TypeToProdId(thirdParty.Type);
                if (prodId == 0)
                {
                    ptr = enumerate.Next; // controller was not assigned a type, but advance ptr anyway
                    continue;
                }

                if (validController && !ControllerAlreadyAdded(enumerate.Path))
                {
                    switch (prodId)
                    {
                        case ProductL:
                            isLeft = true;
                            Form.AppendTextBox("Left Joy-Con connected.");
                            break;
                        case ProductR:
                            isLeft = false;
                            Form.AppendTextBox("Right Joy-Con connected.");
                            break;
                        case ProductPro:
                            isLeft = true;
                            Form.AppendTextBox("Pro controller connected.");
                            break;
                        case ProductSNES:
                            isLeft = true;
                            Form.AppendTextBox("SNES controller connected.");
                            break;
                        default:
                            Form.AppendTextBox("Non Joy-Con Nintendo input device skipped.");
                            break;
                    }

                    var handle = hid_open_path(enumerate.Path);
                    if (handle == IntPtr.Zero)
                    {
                        Form.AppendTextBox(
                            "Unable to open path to device - are you using the correct (64 vs 32-bit) version for your PC?"
                        );
                        break;
                    }

                    hid_set_nonblocking(handle, 1);

                    // Add controller to block-list for HidHide
                    Program.AddDeviceToBlocklist(handle);

                    var type = Joycon.ControllerType.Joycon;
                    if (prodId == ProductPro)
                    {
                        type = Joycon.ControllerType.Pro;
                    }
                    else if (prodId == ProductSNES)
                    {
                        type = Joycon.ControllerType.SNES;
                    }

                    var indexController = J.Count;
                    var isUSB = enumerate.BusType == BusType.USB;
                    var controller = new Joycon(
                        handle,
                        EnableIMU,
                        EnableLocalize && EnableIMU,
                        0.05f,
                        isLeft,
                        enumerate.Path,
                        enumerate.SerialNumber,
                        isUSB,
                        indexController,
                        type,
                        thirdParty != null
                    )
                    {
                        Form = Form
                    };

                    var mac = new byte[6];
                    try
                    {
                        for (var n = 0; n < 6; n++)
                        {
                            mac[n] = byte.Parse(enumerate.SerialNumber.AsSpan(n * 2, 2), NumberStyles.HexNumber);
                        }
                    }
                    catch (Exception /*e*/)
                    {
                        // could not parse mac address
                    }

                    controller.PadMacAddress = new PhysicalAddress(mac);

                    J.Add(controller);
                    if (indexController < 4)
                    {
                        Form.AddController(controller);
                    }

                    foundNew = true;
                }
            }

            hid_free_enumeration(topPtr);

            if (foundNew)
            {
                // attempt to auto join-up joycons on connection
                Joycon temp = null;
                foreach (var v in J)
                {
                    // Do not attach two controllers if they are either:
                    // - Not a Joycon
                    // - Already attached to another Joycon (that isn't itself)
                    if (v.IsPro || (v.Other != null && v.Other != v))
                    {
                        continue;
                    }

                    // Otherwise, iterate through and find the Joycon with the lowest
                    // id that has not been attached already (Does not include self)
                    if (temp == null)
                    {
                        temp = v;
                    }
                    else if (temp.IsLeft != v.IsLeft && v.Other == null)
                    {
                        temp.Other = v;
                        v.Other = temp;

                        temp.DisconnectViGEm();
                        Form.JoinJoycon(v, temp);

                        temp = null; // repeat
                    }
                }
            }

            var dropped = false;
            var on = bool.Parse(ConfigurationManager.AppSettings["HomeLEDOn"]);
            foreach (var jc in J)
            {
                // Connect device straight away
                if (jc.State == Joycon.Status.NotAttached)
                {
                    try
                    {
                        jc.ConnectViGEm();
                        jc.Attach();
                    }
                    catch (Exception e)
                    {
                        jc.Drop();
                        dropped = true;

                        Form.AppendTextBox($"Could not connect {jc.GetControllerName()} ({e.Message}). Dropped.");
                        continue;
                    }

                    jc.SetHomeLight(on);

                    if (Form.AllowCalibration)
                    {
                        jc.GetActiveIMUData();
                        jc.GetActiveSticksData();
                    }

                    jc.Begin();
                }
            }

            var checkInterval = dropped ? FastCheckInterval : DefaultCheckInterval;
            return checkInterval;
        }

        public void OnApplicationQuit()
        {
            lock (_checkControllerLock)
            {
                _controllerCheck?.Stop();
                _controllerCheck?.Dispose();
                IsRunning = false;
            }

            var powerOff = bool.Parse(ConfigurationManager.AppSettings["AutoPowerOff"]);
            foreach (var v in J)
            {
                if (powerOff)
                {
                    v.PowerOff();
                }

                v.Detach();
            }

            hid_exit();
        }
    }

    internal class Program
    {
        public static PhysicalAddress BtMac = new(new byte[] { 0, 0, 0, 0, 0, 0 });
        public static UdpServer Server;

        public static ViGEmClient EmClient;

        public static JoyconManager Mgr;

        private static MainForm _form;

        public static readonly ConcurrentList<SController> ThirdPartyCons = new();

        private static bool _useHIDHide = bool.Parse(ConfigurationManager.AppSettings["UseHidHide"]);

        private static IKeyboardEventSource _keyboard;
        private static IMouseEventSource _mouse;

        private static readonly HashSet<string> BlockedDeviceInstances = new();

        private static bool _isRunning;

        private static readonly string AppGuid = "1bf709e9-c133-41df-933a-c9ff3f664c7b"; // randomly-generated
        private static Mutex _mutexInstance;

        public static void Start()
        {
            _useHIDHide = StartHIDHide();

            if (bool.Parse(ConfigurationManager.AppSettings["ShowAsXInput"]) ||
                bool.Parse(ConfigurationManager.AppSettings["ShowAsDS4"]))
            {
                try
                {
                    EmClient = new ViGEmClient(); // Manages emulated XInput
                }
                catch (VigemBusNotFoundException)
                {
                    _form.AppendTextBox("Could not start VigemBus. Make sure drivers are installed correctly.");
                }
            }

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Get local BT host MAC
                if (nic.NetworkInterfaceType != NetworkInterfaceType.FastEthernetFx &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                {
                    if (nic.Name.Split()[0] == "Bluetooth")
                    {
                        BtMac = nic.GetPhysicalAddress();
                    }
                }
            }

            var controllers = GetSaved3RdPartyControllers();
            Update3RdPartyControllers(controllers);

            Mgr = new JoyconManager
            {
                Form = _form
            };
            Mgr.Awake();

            Server = new UdpServer(Mgr.J)
            {
                Form = _form
            };

            Server.Start(
                IPAddress.Parse(ConfigurationManager.AppSettings["IP"]),
                int.Parse(ConfigurationManager.AppSettings["Port"])
            );

            // Capture keyboard + mouse events for binding's sake
            _keyboard = Capture.Global.KeyboardAsync();
            _keyboard.KeyEvent += Keyboard_KeyEvent;
            _mouse = Capture.Global.MouseAsync();
            _mouse.MouseEvent += Mouse_MouseEvent;

            _form.AppendTextBox("All systems go");
            Mgr.Start();
            _isRunning = true;
        }

        private static bool StartHIDHide()
        {
            if (!_useHIDHide)
            {
                return false;
            }

            var hidHideService = new HidHideControlService();
            if (!hidHideService.IsInstalled)
            {
                _form.AppendTextBox("HIDHide is not installed.");
                return false;
            }

            try
            {
                hidHideService.IsAppListInverted = false;
            }
            catch (Exception /*e*/)
            {
                _form.AppendTextBox("Unable to set HIDHide in whitelist mode.");
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

            try
            {
                if (bool.Parse(ConfigurationManager.AppSettings["PurgeWhitelist"]))
                {
                    hidHideService.ClearApplicationsList();
                }

                hidHideService.AddApplicationPath(Environment.ProcessPath);
            }
            catch (Exception /*e*/)
            {
                _form.AppendTextBox("Unable to add program to whitelist.");
                return false;
            }

            try
            {
                hidHideService.IsActive = true;
            }
            catch (Exception /*e*/)
            {
                _form.AppendTextBox("Unable to hide devices.");
                return false;
            }

            _form.AppendTextBox("HIDHide is enabled.");
            return true;
        }

        public static void AddDeviceToBlocklist(IntPtr handle)
        {
            if (!_useHIDHide)
            {
                return;
            }

            try
            {
                var devices = new List<string>();

                var instance = GetInstance(handle);
                if (instance.Length == 0)
                {
                    _form.AppendTextBox("Unable to get device instance.");
                }
                else
                {
                    devices.Add(instance);
                }

                var parentInstance = GetParentInstance(handle);
                if (parentInstance.Length == 0)
                {
                    _form.AppendTextBox("Unable to get device parent instance.");
                }
                else
                {
                    devices.Add(parentInstance);
                }

                if (devices.Count == 0)
                {
                    throw new Exception("hidapi error");
                }

                BlockDeviceInstances(devices);
            }
            catch (Exception e)
            {
                _form.AppendTextBox($"Unable to add controller to block-list ({e.Message}).");
            }
        }

        public static void BlockDeviceInstances(IList<string> instances)
        {
            var hidHideService = new HidHideControlService();
            foreach (var instance in instances)
            {
                hidHideService.AddBlockedInstanceId(instance);
                BlockedDeviceInstances.Add(instance);
            }
        }

        private static void Mouse_MouseEvent(object sender, EventSourceEventArgs<MouseEvent> e)
        {
            if (e.Data.ButtonDown != null)
            {
                var resVal = Config.Value("reset_mouse");
                if (resVal.StartsWith("mse_"))
                {
                    if ((int)e.Data.ButtonDown.Button == int.Parse(resVal.AsSpan(4)))
                    {
                        Simulate.Events()
                                .MoveTo(Screen.PrimaryScreen.Bounds.Width / 2, Screen.PrimaryScreen.Bounds.Height / 2)
                                .Invoke();
                    }
                }

                resVal = Config.Value("active_gyro");
                if (resVal.StartsWith("mse_"))
                {
                    if ((int)e.Data.ButtonDown.Button == int.Parse(resVal.AsSpan(4)))
                    {
                        foreach (var i in Mgr.J)
                        {
                            i.ActiveGyro = true;
                        }
                    }
                }
            }

            if (e.Data.ButtonUp != null)
            {
                var resVal = Config.Value("active_gyro");
                if (resVal.StartsWith("mse_"))
                {
                    if ((int)e.Data.ButtonUp.Button == int.Parse(resVal.AsSpan(4)))
                    {
                        foreach (var i in Mgr.J)
                        {
                            i.ActiveGyro = false;
                        }
                    }
                }
            }
        }

        private static void Keyboard_KeyEvent(object sender, EventSourceEventArgs<KeyboardEvent> e)
        {
            if (e.Data.KeyDown != null)
            {
                var resVal = Config.Value("reset_mouse");
                if (resVal.StartsWith("key_"))
                {
                    if ((int)e.Data.KeyDown.Key == int.Parse(resVal.AsSpan(4)))
                    {
                        Simulate.Events()
                                .MoveTo(Screen.PrimaryScreen.Bounds.Width / 2, Screen.PrimaryScreen.Bounds.Height / 2)
                                .Invoke();
                    }
                }

                resVal = Config.Value("active_gyro");
                if (resVal.StartsWith("key_"))
                {
                    if ((int)e.Data.KeyDown.Key == int.Parse(resVal.AsSpan(4)))
                    {
                        foreach (var i in Mgr.J)
                        {
                            i.ActiveGyro = true;
                        }
                    }
                }
            }

            if (e.Data.KeyUp != null)
            {
                var resVal = Config.Value("active_gyro");
                if (resVal.StartsWith("key_"))
                {
                    if ((int)e.Data.KeyUp.Key == int.Parse(resVal.AsSpan(4)))
                    {
                        foreach (var i in Mgr.J)
                        {
                            i.ActiveGyro = false;
                        }
                    }
                }
            }
        }

        public static void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;

            StopHIDHide();

            _keyboard?.Dispose();
            _mouse?.Dispose();
            Mgr?.OnApplicationQuit();
            Server?.Stop();
        }

        public static void AllowAnotherInstance()
        {
            _mutexInstance?.Close();
        }

        public static void StopHIDHide()
        {
            if (!_useHIDHide)
            {
                return;
            }

            var hidHideService = new HidHideControlService();

            try
            {
                hidHideService.RemoveApplicationPath(Environment.ProcessPath);
            }
            catch (Exception /*e*/)
            {
                _form.AppendTextBox("Unable to remove program from whitelist.");
            }

            if (bool.Parse(ConfigurationManager.AppSettings["PurgeAffectedDevices"]))
            {
                try
                {
                    foreach (var instance in BlockedDeviceInstances)
                    {
                        hidHideService.RemoveBlockedInstanceId(instance);
                    }
                }
                catch (Exception /*e*/)
                {
                    _form.AppendTextBox("Unable to purge blacklisted devices.");
                }
            }

            try
            {
                hidHideService.IsActive = false;
            }
            catch (Exception /*e*/)
            {
                _form.AppendTextBox("Unable to disable HIDHide.");
            }
        }

        public static void Update3RdPartyControllers(List<SController> controllers)
        {
            ThirdPartyCons.Set(controllers);
        }

        private static void Main(string[] args)
        {
            // Setting the culturesettings so float gets parsed correctly
            CultureInfo.CurrentCulture = new CultureInfo("en-US", false);

            // Set the correct DLL for the current OS
            SetupDlls();

            using (_mutexInstance = new Mutex(false, "Global\\" + AppGuid))
            {
                if (!_mutexInstance.WaitOne(0, false))
                {
                    MessageBox.Show("Instance already running.", "BetterJoy");
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                _form = new MainForm();
                Application.Run(_form);
            }
        }

        private static void SetupDlls()
        {
            var archPath = $"{AppDomain.CurrentDomain.BaseDirectory}{(Environment.Is64BitProcess ? "x64" : "x86")}\\";
            var pathVariable = Environment.GetEnvironmentVariable("PATH");
            pathVariable = $"{archPath};{pathVariable}";
            Environment.SetEnvironmentVariable("PATH", pathVariable);
        }
    }
}
