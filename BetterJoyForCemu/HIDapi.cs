using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterJoyForCemu {
	public class HIDapi {
#if LINUX
	    const string dll = "libhidapi.so";
#else
		const string dll = "hidapi.dll";
#endif
        const int maxStringLength = 255;

        public enum BusType {
            UNKNOWN = 0x00,
            USB = 0x01,
            BLUETOOTH = 0x02,
            I2C = 0x03,
            SPI = 0x04
        }

        public struct hid_device_info {
			[MarshalAs(UnmanagedType.LPStr)]
			public string path;
			public ushort vendor_id;
			public ushort product_id;
			[MarshalAs(UnmanagedType.LPWStr)]
			public string serial_number;
			public ushort release_number;
			[MarshalAs(UnmanagedType.LPWStr)]
			public string manufacturer_string;
			[MarshalAs(UnmanagedType.LPWStr)]
			public string product_string;
			public ushort usage_page;
			public ushort usage;
			public int interface_number;
			public IntPtr next;
            public BusType bus_type; // >= 0.13.0
        };

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern int hid_init();

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern int hid_exit();

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr hid_enumerate(ushort vendor_id, ushort product_id);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern void hid_free_enumeration(IntPtr phid_device_info);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr hid_open(ushort vendor_id, ushort product_id, [MarshalAs(UnmanagedType.LPWStr)] string serial_number);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr hid_open_path([MarshalAs(UnmanagedType.LPStr)] string path);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern int hid_write(IntPtr device, byte[] data, UIntPtr length);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern int hid_read_timeout(IntPtr dev, byte[] data, UIntPtr length, int milliseconds);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern int hid_read(IntPtr device, byte[] data, UIntPtr length);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern int hid_set_nonblocking(IntPtr device, int nonblock);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern int hid_send_feature_report(IntPtr device, byte[] data, UIntPtr length);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern int hid_get_feature_report(IntPtr device, byte[] data, UIntPtr length);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern void hid_close(IntPtr device);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern int hid_get_manufacturer_string(IntPtr device, [MarshalAs(UnmanagedType.LPWStr)] string string_, UIntPtr maxlen);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern int hid_get_product_string(IntPtr device, [MarshalAs(UnmanagedType.LPWStr)] string string_, UIntPtr maxlen);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern int hid_get_serial_number_string(IntPtr device, [MarshalAs(UnmanagedType.LPWStr)] string string_, UIntPtr maxlen);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		public static extern int hid_get_indexed_string(IntPtr device, int string_index, [MarshalAs(UnmanagedType.LPWStr)] string string_, UIntPtr maxlen);

		[DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
		[return: MarshalAs(UnmanagedType.LPWStr)]
		public static extern string hid_error(IntPtr device);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_winapi_get_container_id(IntPtr device, out Guid container_id);

        // Added in my fork of HIDapi at https://github.com/d3xMachina/hidapi (needed for HIDHide to work correctly)
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_winapi_get_instance_string(IntPtr device, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder string_, UIntPtr maxlen);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hid_winapi_get_parent_instance_string(IntPtr device, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder string_, UIntPtr maxlen);
        // END

        static void PrintEnumeration(IntPtr phid_device_info) {
			if (!phid_device_info.Equals(IntPtr.Zero)) {
				hid_device_info hdev = (hid_device_info)Marshal.PtrToStructure(phid_device_info, typeof(hid_device_info));

				Console.WriteLine(string.Format("path:       {0}", hdev.path));
				Console.WriteLine(string.Format("vendor id:  {0:X}", hdev.vendor_id));
				Console.WriteLine(string.Format("product id: {0:X}", hdev.product_id));
				Console.WriteLine(string.Format("usage page: {0:X}", hdev.usage_page));
				Console.WriteLine(string.Format("usage:      {0:X}", hdev.usage));
				Console.WriteLine("");

				PrintEnumeration(hdev.next);
			}
		}

		static string _getDevicePath(IntPtr phid_device_info, ushort usagePage, ushort usage) {
			if (!phid_device_info.Equals(IntPtr.Zero)) {
				hid_device_info hdev = (hid_device_info)Marshal.PtrToStructure(phid_device_info, typeof(hid_device_info));
				if (usagePage == hdev.usage_page && usage == hdev.usage)
					return hdev.path;
				else
					return _getDevicePath(hdev.next, usagePage, usage);
			}
			return null;
		}

		public static string GetDevicePath(ushort vendorId, ushort productId, ushort usagePage, ushort usage) {
			return _getDevicePath(hid_enumerate(vendorId, productId), usagePage, usage);
		}

        public static string GetInstance(IntPtr device) {
            StringBuilder bufferInstance = new StringBuilder(maxStringLength);

            int ret = hid_winapi_get_instance_string(device, bufferInstance, maxStringLength);
            if (ret < 0) {
                return "";
            }

            string instance = bufferInstance.ToString();
            if (string.IsNullOrEmpty(instance)) {
                return "";
            }
            return instance;
        }

        public static string GetParentInstance(IntPtr device) {
            StringBuilder bufferInstance = new StringBuilder(maxStringLength);

            int ret = hid_winapi_get_parent_instance_string(device, bufferInstance, maxStringLength);
            if (ret < 0) {
                return "";
            }

            string instance = bufferInstance.ToString();
            if (string.IsNullOrEmpty(instance)) {
                return "";
            }
            return instance;
        }
    }
}
