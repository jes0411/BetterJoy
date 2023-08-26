using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterJoy.Collections;
using Nefarius.Drivers.HidHide;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using WindowsInput;
using WindowsInput.Events.Sources;
using static BetterJoy._3rdPartyControllers;
using static BetterJoy.HIDApi;

namespace BetterJoy
{
    public class JoyconManager
    {
        private const ushort VendorId = 0x57e;
        private const ushort ProductL = 0x2006;
        private const ushort ProductR = 0x2007;
        private const ushort ProductPro = 0x2009;
        private const ushort ProductSNES = 0x2017;

        public readonly bool EnableIMU = true;
        public readonly bool EnableLocalize = false;

        private readonly MainForm _form;

        private bool _isRunning = false;
        private CancellationTokenSource _ctsDevicesNotifications;

        public ConcurrentList<Joycon> Controllers { get; } = new(); // connected controllers

        private readonly Channel<DeviceNotification> _channelDeviceNotifications;

        private int _hidCallbackHandle = 0;
        private Task _devicesNotificationTask;

        private class DeviceNotification
        {
            public enum Type
            {
                Unknown,
                Connected,
                Disconnected,
                Errored
            }

            public readonly Type Notification;
            public readonly object Data;

            public DeviceNotification(Type notification, object data) // data must be immutable
            {
                Notification = notification;
                Data = data;
            }
        }

        public JoyconManager(MainForm form)
        {
            _form = form;

            _channelDeviceNotifications = Channel.CreateUnbounded<DeviceNotification>(
                new UnboundedChannelOptions
                {
                    SingleWriter = false,
                    SingleReader = true,
                    AllowSynchronousContinuations = false
                }
            );
        }

        public bool Start()
        {
            if (_isRunning)
            {
                return true;
            }

            int ret = hid_init();
            if (ret != 0)
            {
                _form.AppendTextBox("Could not initialize hidapi");
                return false;
            }

            ret = hid_hotplug_register_callback(
                0x0,
                0x0,
                (int)(HotplugEvent.DeviceArrived | HotplugEvent.DeviceLeft),
                (int)HotplugFlag.Enumerate,
                OnDeviceNotification,
                _channelDeviceNotifications.Writer,
                out _hidCallbackHandle
            );

            if (ret != 0)
            {
                _form.AppendTextBox(("Could not register hidapi callback"));
                hid_exit();
                return false;
            }

            _ctsDevicesNotifications = new CancellationTokenSource();

            _devicesNotificationTask = Task.Run(
                async () =>
                {
                    try
                    {
                        await ProcessDevicesNotifications(_ctsDevicesNotifications.Token);
                    }
                    catch (OperationCanceledException e) when (_ctsDevicesNotifications.IsCancellationRequested) { }
                }
            );

            _isRunning = true;
            return true;
        }

        private static int OnDeviceNotification(int callbackHandle, HIDDeviceInfo deviceInfo, int ev, object pUserData)
        {
            var channelWriter = (ChannelWriter<DeviceNotification>)pUserData;
            var deviceEvent = (HotplugEvent)ev;

            var notification = DeviceNotification.Type.Unknown;
            switch (deviceEvent)
            {
                case HotplugEvent.DeviceArrived:
                    notification = DeviceNotification.Type.Connected;
                    break;
                case HotplugEvent.DeviceLeft:
                    notification = DeviceNotification.Type.Disconnected;
                    break;
            }

            var job = new DeviceNotification(notification, deviceInfo);

            while (!channelWriter.TryWrite(job)) { }

            return 0;
        }

        private async Task ProcessDevicesNotifications(CancellationToken token)
        {
            var channelReader = _channelDeviceNotifications.Reader;

            while (await channelReader.WaitToReadAsync(token))
            {
                while (channelReader.TryRead(out var job))
                {
                    switch (job.Notification)
                    {
                        case DeviceNotification.Type.Connected:
                        {
                            var deviceInfos = (HIDDeviceInfo)job.Data;
                            OnDeviceConnected(deviceInfos);
                            break;
                        }
                        case DeviceNotification.Type.Disconnected:
                        {
                            var deviceInfos = (HIDDeviceInfo)job.Data;
                            OnDeviceDisconnected(deviceInfos);
                            break;
                        }
                        case DeviceNotification.Type.Errored:
                        {
                            var controller = (Joycon)job.Data;
                            OnDeviceErrored(controller);
                            break;
                        }
                    }
                }
            }
        }

        private void OnDeviceConnected(HIDDeviceInfo info)
        {
            if (info.SerialNumber == null || GetControllerByPath(info.Path) != null)
            {
                return;
            }

            var validController = (info.ProductId == ProductL || info.ProductId == ProductR ||
                                   info.ProductId == ProductPro || info.ProductId == ProductSNES) &&
                                  info.VendorId == VendorId;

            // check if it's a custom controller
            SController thirdParty = null;
            foreach (var v in Program.ThirdpartyCons)
            {
                if (info.VendorId == v.VendorId &&
                    info.ProductId == v.ProductId &&
                    info.SerialNumber == v.SerialNumber)
                {
                    validController = true;
                    thirdParty = v;
                    break;
                }
            }

            if (!validController)
            {
                return;
            }

            var prodId = thirdParty == null ? info.ProductId : TypeToProdId(thirdParty.Type);
            if (prodId == 0)
            {
                // controller was not assigned a type
                return;
            }

            bool isUSB = info.BusType == BusType.USB;
            var isLeft = false;
            var type = Joycon.ControllerType.Joycon;

            switch (prodId)
            {
                case ProductL:
                    isLeft = true;
                    break;
                case ProductR:
                    break;
                case ProductPro:
                    isLeft = true;
                    type = Joycon.ControllerType.Pro;
                    break;
                case ProductSNES:
                    isLeft = true;
                    type = Joycon.ControllerType.SNES;
                    break;
            }

            OnDeviceConnected(info.Path, info.SerialNumber, type, isLeft, isUSB, thirdParty != null);
        }

        private void OnDeviceConnected(string path, string serial, Joycon.ControllerType type, bool isLeft, bool isUSB, bool isThirdparty, bool reconnect = false)
        {
            var handle = hid_open_path(path);
            if (handle == IntPtr.Zero)
            {
                // don't show an error message when the controller was dropped without hidapi callback notification (after standby by example)
                if (!reconnect)
                {
                    _form.AppendTextBox(
                        "Unable to open path to device - device disconnected or incorrect hidapi version (32 bits vs 64 bits)"
                    );
                }

                return;
            }

            hid_set_nonblocking(handle, 1);

            switch (type)
            {
                case Joycon.ControllerType.Joycon:
                    _form.AppendTextBox(isLeft ? "Left joycon connected." : "Right joycon connected.");
                    break;
                case Joycon.ControllerType.Pro:
                    _form.AppendTextBox("Pro controller connected.");
                    break;
                case Joycon.ControllerType.SNES:
                    _form.AppendTextBox("SNES controller connected.");
                    break;
            }

            // Add controller to block-list for HidHide
            Program.AddDeviceToBlocklist(handle);

            var indexController = Controllers.Count;
            var controller = new Joycon(
                _form,
                handle,
                EnableIMU,
                EnableLocalize && EnableIMU,
                0.05f,
                isLeft,
                path,
                serial,
                isUSB,
                indexController,
                type,
                isThirdparty
            );
            controller.StateChanged += OnControllerStateChanged;

            var mac = new byte[6];
            try
            {
                for (var n = 0; n < 6 && n < serial.Length; n++)
                {
                    mac[n] = byte.Parse(serial.AsSpan(n * 2, 2), NumberStyles.HexNumber);
                }
            }
            catch (Exception)
            {
                // could not parse mac address
            }

            controller.PadMacAddress = new PhysicalAddress(mac);

            // Connect device straight away
            try
            {
                controller.ConnectViGEm();
                controller.Attach();
            }
            catch (Exception e)
            {
                controller.Drop(true);

                _form.AppendTextBox($"Could not connect {controller.GetControllerName()} ({e.Message}). Dropped.");
                return;
            }

            if (!controller.IsPro)
            {
                // attempt to auto join-up joycons on connection
                foreach (var otherController in Controllers)
                {
                    if (otherController.IsPro || // not a joycon
                        otherController.Other != null || // already associated
                        controller.IsLeft == otherController.IsLeft)
                    {
                        continue;
                    }

                    controller.Other = otherController;
                    otherController.Other = controller;
                    break;
                }
            }

            Controllers.Add(controller);
            if (indexController < 4)
            {
                _form.AddController(controller);
            }

            if (controller.Other != null)
            {
                controller.Other.DisconnectViGEm();
                _form.JoinJoycon(controller, controller.Other);
            }

            if (_form.AllowCalibration)
            {
                controller.GetActiveIMUData();
                controller.GetActiveSticksData();
            }

            var ledOn = bool.Parse(ConfigurationManager.AppSettings["HomeLEDOn"]);
            controller.SetHomeLight(ledOn);

            controller.Begin();
        }

        private void OnDeviceDisconnected(HIDDeviceInfo info)
        {
            var controller = GetControllerByPath(info.Path);

            OnDeviceDisconnected(controller);
        }

        private void OnDeviceDisconnected(Joycon controller)
        {
            if (controller == null)
            {
                return;
            }

            controller.StateChanged -= OnControllerStateChanged;
            controller.Detach();

            if (controller.Other != null)
            {
                controller.Other.Other = null; // The other of the other is the joycon itself
                try
                {
                    controller.Other.ConnectViGEm();
                }
                catch (Exception)
                {
                    _form.AppendTextBox("Could not connect the fake controller for the unjoined joycon.");
                }
            }

            Controllers.Remove(controller);
            _form.RemoveController(controller);

            _form.AppendTextBox($"{controller.GetControllerName()} disconnected.");
        }

        private void OnDeviceErrored(Joycon controller)
        {
            if (controller.State > Joycon.Status.Dropped)
            {
                // device not in error anymore (after a reset or a reconnection from the system)
                return;
            }
            OnDeviceDisconnected(controller);
            OnDeviceConnected(controller.Path, controller.SerialNumber, controller.Type, controller.IsLeft, controller.IsUSB, controller.IsThirdParty, true);
        }

        private void OnControllerStateChanged(object sender, Joycon.StateChangedEventArgs e)
        {
            if (sender == null)
            {
                return;
            }

            var controller = (Joycon)sender;
            var writer = _channelDeviceNotifications.Writer;

            switch (e.State)
            {
                case Joycon.Status.Errored:
                    var notification = new DeviceNotification(DeviceNotification.Type.Errored, controller);
                    while (!writer.TryWrite(notification)) { }
                    break;
            }
        }

        private Joycon GetControllerByPath(string path)
        {
            foreach (var controller in Controllers)
            {
                if (controller.Path == path)
                {
                    return controller;
                }
            }

            return null;
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

        public async Task Stop()
        {
            if (!_isRunning)
            {
                return;
            }
            _isRunning = false;

            _ctsDevicesNotifications.Cancel();

            if (_hidCallbackHandle != 0)
            {
                hid_hotplug_deregister_callback(_hidCallbackHandle);
            }
            
            await _devicesNotificationTask;

            _ctsDevicesNotifications.Dispose();

            var powerOff = bool.Parse(ConfigurationManager.AppSettings["AutoPowerOff"]);
            foreach (var controller in Controllers)
            {
                controller.StateChanged -= OnControllerStateChanged;

                if (powerOff)
                {
                    controller.PowerOff();
                }

                controller.Detach();
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

        public static readonly ConcurrentList<SController> ThirdpartyCons = new();

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

            var controllers = GetSavedThirdpartyControllers();
            UpdateThirdpartyControllers(controllers);

            Mgr = new JoyconManager(_form);
            Server = new UdpServer(_form, Mgr.Controllers);

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
                        foreach (var i in Mgr.Controllers)
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
                        foreach (var i in Mgr.Controllers)
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
                        foreach (var i in Mgr.Controllers)
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
                        foreach (var i in Mgr.Controllers)
                        {
                            i.ActiveGyro = false;
                        }
                    }
                }
            }
        }

        public static async Task Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;

            StopHIDHide();

            _keyboard?.Dispose();
            _mouse?.Dispose();
            if (Mgr != null)
            {
                await Mgr.Stop();
            }

            if (Server != null)
            {
                await Server.Stop();
                Server.Dispose();
            }
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

        public static void UpdateThirdpartyControllers(List<SController> controllers)
        {
            ThirdpartyCons.Set(controllers);
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
