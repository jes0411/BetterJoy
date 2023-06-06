using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterJoy
{
    public class HIDApi
    {
#if LINUX
	    private const string Dll = "libhidapi.so";
#else
        private const string Dll = "hidapi.dll";
#endif
        private const int MaxStringLength = 255;

        public enum BusType
        {
            Unknown = 0x00,
            USB = 0x01,
            Bluetooth = 0x02,
            I2C = 0x03,
            SPI = 0x04
        }
        
        // Added in the official hidapi callback branch
        #region HIDAPI_CALLBACK

        public struct HIDDeviceInfo
        {
            [MarshalAs(UnmanagedType.LPStr)] public string Path;
            public ushort VendorId;
            public ushort ProductId;
            [MarshalAs(UnmanagedType.LPWStr)] public string SerialNumber;
            public ushort ReleaseNumber;
            [MarshalAs(UnmanagedType.LPWStr)] public string ManufacturerString;
            [MarshalAs(UnmanagedType.LPWStr)] public string ProductString;
            public ushort UsagePage;
            public ushort Usage;
            public int InterfaceNumber;
            public IntPtr Next;
            public BusType BusType; // >= 0.13.0
        }

        [Flags]
        public enum HotplugEvent
        {
            DeviceArrived = (1 << 0),
            DeviceLeft = (1 << 1)
        }

        [Flags]
        public enum HotplugFlag
        {
            None = 0,
            Enumerate = (1 << 0)
        }

        public delegate int HotPlugCallbackFunction(
            int callbackHandle,
            [MarshalAs(UnmanagedType.Struct)] HIDDeviceInfo deviceInfo,
            int events,
            [MarshalAs(UnmanagedType.IUnknown)] object userData
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_hotplug_register_callback(
            ushort vendorId,
            ushort productId,
            int events,
            int flags,
            [MarshalAs(UnmanagedType.FunctionPtr)] HotPlugCallbackFunction callback,
            [MarshalAs(UnmanagedType.IUnknown)] object userData,
            out int callbackHandle
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_hotplug_deregister_callback(int callbackHandle);
        
        #endregion

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_init();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_exit();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr hid_enumerate(ushort vendorId, ushort productId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void hid_free_enumeration(IntPtr phidDeviceInfo);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr hid_open(
            ushort vendorId,
            ushort productId,
            [MarshalAs(UnmanagedType.LPWStr)] string serialNumber
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr hid_open_path([MarshalAs(UnmanagedType.LPStr)] string path);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_write(IntPtr device, byte[] data, UIntPtr length);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_read_timeout(IntPtr dev, byte[] data, UIntPtr length, int milliseconds);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_read(IntPtr device, byte[] data, UIntPtr length);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_set_nonblocking(IntPtr device, int nonblock);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_send_feature_report(IntPtr device, byte[] data, UIntPtr length);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_get_feature_report(IntPtr device, byte[] data, UIntPtr length);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void hid_close(IntPtr device);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_get_manufacturer_string(
            IntPtr device,
            [MarshalAs(UnmanagedType.LPWStr)] string @string,
            UIntPtr maxlen
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_get_product_string(
            IntPtr device,
            [MarshalAs(UnmanagedType.LPWStr)] string @string,
            UIntPtr maxlen
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_get_serial_number_string(
            IntPtr device,
            [MarshalAs(UnmanagedType.LPWStr)] string @string,
            UIntPtr maxlen
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_get_indexed_string(
            IntPtr device,
            int stringIndex,
            [MarshalAs(UnmanagedType.LPWStr)] string @string,
            UIntPtr maxlen
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.LPWStr)]
        public static extern string hid_error(IntPtr device);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_winapi_get_container_id(IntPtr device, out Guid containerId);

        // Added in my fork of HIDapi at https://github.com/d3xMachina/hidapi (needed for HIDHide to work correctly)
        #region HIDAPI_MYFORK

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_winapi_get_instance_string(
            IntPtr device,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder @string,
            UIntPtr maxlen
        );

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_winapi_get_parent_instance_string(
            IntPtr device,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder @string,
            UIntPtr maxlen
        );

        #endregion

        public static string GetInstance(IntPtr device)
        {
            var bufferInstance = new StringBuilder(MaxStringLength);

            var ret = hid_winapi_get_instance_string(device, bufferInstance, MaxStringLength);
            if (ret < 0)
            {
                return "";
            }

            var instance = bufferInstance.ToString();
            if (string.IsNullOrEmpty(instance))
            {
                return "";
            }

            return instance;
        }

        public static string GetParentInstance(IntPtr device)
        {
            var bufferInstance = new StringBuilder(MaxStringLength);

            var ret = hid_winapi_get_parent_instance_string(device, bufferInstance, MaxStringLength);
            if (ret < 0)
            {
                return "";
            }

            var instance = bufferInstance.ToString();
            if (string.IsNullOrEmpty(instance))
            {
                return "";
            }

            return instance;
        }
    }
}
