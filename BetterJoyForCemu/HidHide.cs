using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;
using PInvoke;

namespace BetterJoyForCemu {
    /// <summary>
    ///     String manipulation helper methods.
    /// </summary>
    internal static class StringHelperUtil
    {
        /// <summary>
        ///     Converts an array of <see cref="string" /> into a double-null-terminated multi-byte character memory block.
        /// </summary>
        /// <param name="instances">Source array of strings.</param>
        /// <param name="length">The length of the resulting byte array.</param>
        /// <returns>The allocated memory buffer.</returns>
        public static IntPtr StringArrayToMultiSzPointer(this IEnumerable<string> instances, out int length)
        {
            // Temporary byte array
            IEnumerable<byte> multiSz = new List<byte>();

            // Convert each string into wide multi-byte and add NULL-terminator in between
            multiSz = instances.Aggregate(multiSz,
                (current, entry) => current.Concat(Encoding.Unicode.GetBytes(entry))
                    .Concat(Encoding.Unicode.GetBytes(new[] {char.MinValue})));

            // Add another NULL-terminator to signal end of the list
            multiSz = multiSz.Concat(Encoding.Unicode.GetBytes(new[] {char.MinValue}));

            // Convert expression to array
            var multiSzArray = multiSz.ToArray();

            // Convert array to managed native buffer
            var buffer = Marshal.AllocHGlobal(multiSzArray.Length);
            Marshal.Copy(multiSzArray, 0, buffer, multiSzArray.Length);

            length = multiSzArray.Length;

            // Return usable buffer, don't forget to free!
            return buffer;
        }

        /// <summary>
        ///     Converts a double-null-terminated multi-byte character memory block into a string array.
        /// </summary>
        /// <param name="buffer">The memory buffer.</param>
        /// <param name="length">The size in bytes of the memory buffer.</param>
        /// <returns>The extracted string array.</returns>
        public static IEnumerable<string> MultiSzPointerToStringArray(this IntPtr buffer, int length)
        {
            // Temporary byte array
            var rawBuffer = new byte[length];

            // Grab data from buffer
            Marshal.Copy(buffer, rawBuffer, 0, length);

            // Trims away potential redundant NULL-characters and splits at NULL-terminator
            return Encoding.Unicode.GetString(rawBuffer).TrimEnd(char.MinValue).Split(char.MinValue);
        }
    }

    /// <summary>
    ///     Path manipulation and volume helper methods.
    /// </summary>
    internal static class VolumeHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumePathNamesForVolumeNameW(
            [MarshalAs(UnmanagedType.LPWStr)] string lpszVolumeName,
            [MarshalAs(UnmanagedType.LPWStr)] [Out]
            StringBuilder lpszVolumeNamePaths, uint cchBuferLength,
            ref uint lpcchReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr FindFirstVolume([Out] StringBuilder lpszVolumeName,
            uint cchBufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindNextVolume(IntPtr hFindVolume, [Out] StringBuilder lpszVolumeName,
            uint cchBufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

        private class VolumeMeta
        {
            public string DriveLetter { get; set; }

            public string VolumeName { get; set; }

            public string DevicePath { get; set; }
        }

        /// <summary>
        ///     Curates and returns a collection of volume to path mappings.
        /// </summary>
        /// <returns>A collection of <see cref="VolumeMeta"/>.</returns>
        private static IEnumerable<VolumeMeta> GetVolumeMappings()
        {
            var volumeName = new StringBuilder(ushort.MaxValue);
            var pathName = new StringBuilder(ushort.MaxValue);
            var mountPoint = new StringBuilder(ushort.MaxValue);
            uint returnLength = 0;

            var volumeHandle = FindFirstVolume(volumeName, ushort.MaxValue);

            do
            {
                var volume = volumeName.ToString();

                if (!GetVolumePathNamesForVolumeNameW(volume, mountPoint, ushort.MaxValue, ref returnLength))
                    continue;

                // Extract volume name for use with QueryDosDevice
                var deviceName = volume.Substring(4, volume.Length - 1 - 4);

                // Grab device path
                returnLength = QueryDosDevice(deviceName, pathName, ushort.MaxValue);

                if (returnLength <= 0)
                    continue;

                yield return new VolumeMeta
                {
                    DriveLetter = mountPoint.ToString(),
                    VolumeName = volume,
                    DevicePath = pathName.ToString()
                };
            } while (FindNextVolume(volumeHandle, volumeName, ushort.MaxValue));
        }

        /// <summary>
        ///     Checks if a path is a junction point.
        /// </summary>
        /// <param name="di">A <see cref="FileSystemInfo"/> instance.</param>
        /// <returns>True if it's a junction, false otherwise.</returns>
        private static bool IsPathReparsePoint(FileSystemInfo di)
        {
            return di.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }

        /// <summary>
        ///     Helper to make paths comparable.
        /// </summary>
        /// <param name="path">The source path.</param>
        /// <returns>The normalized path.</returns>
        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(new Uri(path).LocalPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }

        /// <summary>
        ///     Translates a user-land file path to "DOS device" path.
        /// </summary>
        /// <param name="path">The file path in normal namespace format.</param>
        /// <returns>The device namespace path (DOS device).</returns>
        public static string PathToDosDevicePath(string path)
        {
            if (!File.Exists(path))
                throw new ArgumentException("The supplied file path doesn't exist", nameof(path));

            var filePart = Path.GetFileName(path);
            var pathPart = Path.GetDirectoryName(path);

            if (string.IsNullOrEmpty(pathPart))
                throw new IOException("Couldn't resolve directory");

            var pathNoRoot = string.Empty;
            var devicePath = string.Empty;

            // Walk up the directory tree to get the "deepest" potential junction
            for (var current = new DirectoryInfo(pathPart);
                current != null && current.Exists;
                current = Directory.GetParent(current.FullName))
            {
                if (!IsPathReparsePoint(current)) continue;

                devicePath = GetVolumeMappings().FirstOrDefault(m =>
                        !string.IsNullOrEmpty(m.DriveLetter) &&
                        NormalizePath(m.DriveLetter) == NormalizePath(current.FullName))
                    ?.DevicePath;

                pathNoRoot = pathPart.Substring(current.FullName.Length);

                break;
            }

            // No junctions found, translate original path
            if (string.IsNullOrEmpty(devicePath))
            {
                var driveLetter = Path.GetPathRoot(pathPart);
                devicePath = GetVolumeMappings().FirstOrDefault(m =>
                    m.DriveLetter.Equals(driveLetter, StringComparison.InvariantCultureIgnoreCase))?.DevicePath;
                pathNoRoot = pathPart.Substring(Path.GetPathRoot(pathPart).Length);
            }

            if (string.IsNullOrEmpty(devicePath))
                throw new IOException("Couldn't resolve device path");

            var fullDevicePath = new StringBuilder();

            // Build new DOS Device path
            fullDevicePath.AppendFormat("{0}{1}", devicePath, Path.DirectorySeparatorChar);
            fullDevicePath.Append(Path.Combine(pathNoRoot, filePart).TrimStart(Path.DirectorySeparatorChar));

            return fullDevicePath.ToString();
        }
    }

    static class HidHide
    {
        private const uint IOCTL_GET_WHITELIST = 0x80016000;
        private const uint IOCTL_SET_WHITELIST = 0x80016004;
        private const uint IOCTL_GET_BLACKLIST = 0x80016008;
        private const uint IOCTL_SET_BLACKLIST = 0x8001600C;
        private const uint IOCTL_GET_ACTIVE = 0x80016010;
        private const uint IOCTL_SET_ACTIVE = 0x80016014;

        public static void setStatus(bool hide) {
            using (var handle = Kernel32.CreateFile("\\\\.\\HidHide",
                Kernel32.ACCESS_MASK.GenericRight.GENERIC_READ,
                Kernel32.FileShare.FILE_SHARE_READ | Kernel32.FileShare.FILE_SHARE_WRITE,
                IntPtr.Zero, Kernel32.CreationDisposition.OPEN_EXISTING,
                Kernel32.CreateFileFlags.FILE_ATTRIBUTE_NORMAL,
                Kernel32.SafeObjectHandle.Null
            ))
            {
                var buffer = Marshal.AllocHGlobal(sizeof(bool));

                // Enable blocking logic, if not enabled already
                try
                {
                    byte byteHide = 1;
                    if (!hide) {
                        byteHide = 0;
                    }
                    Marshal.WriteByte(buffer, byteHide);

                    bool ok = Kernel32.DeviceIoControl(
                        handle,
                        unchecked((int) IOCTL_SET_ACTIVE),
                        buffer,
                        sizeof(bool),
                        IntPtr.Zero,
                        0,
                        out _,
                        IntPtr.Zero
                    );
                    if (!ok)
                        throw new Exception("Couldn't set the status");
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        public static void blacklistDevices(IList<string> devices, bool keepExisting = true) {
            using (var handle = Kernel32.CreateFile("\\\\.\\HidHide",
                Kernel32.ACCESS_MASK.GenericRight.GENERIC_READ,
                Kernel32.FileShare.FILE_SHARE_READ | Kernel32.FileShare.FILE_SHARE_WRITE,
                IntPtr.Zero, Kernel32.CreationDisposition.OPEN_EXISTING,
                Kernel32.CreateFileFlags.FILE_ATTRIBUTE_NORMAL,
                Kernel32.SafeObjectHandle.Null
            ))
            {
                var buffer = IntPtr.Zero;

                // List of blocked instances
                IList<string> instances = new List<string>();

                // Get existing list of blocked instances
                // This is important to not discard entries other processes potentially made
                // Always get the current list before altering/submitting it
                if (keepExisting)
                {
                    try
                    {
                        // Get required buffer size
                        bool ok = Kernel32.DeviceIoControl(
                            handle,
                            unchecked((int) IOCTL_GET_BLACKLIST),
                            IntPtr.Zero,
                            0,
                            IntPtr.Zero,
                            0,
                            out var required,
                            IntPtr.Zero
                        );
                        if (!ok)
                            throw new Exception("Couldn't get blacklisted devices buffer size");

                        buffer = Marshal.AllocHGlobal(required);

                        // Get actual buffer content
                        ok = Kernel32.DeviceIoControl(
                            handle,
                            unchecked((int) IOCTL_GET_BLACKLIST),
                            IntPtr.Zero,
                            0,
                            buffer,
                            required,
                            out _,
                            IntPtr.Zero
                        );
                        if (!ok)
                            throw new Exception("Couldn't get blacklisted devices buffer");

                        // Store existing block-list in a more manageable "C#" fashion
                        instances = buffer.MultiSzPointerToStringArray(required).ToList();
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }

                // Manipulate block-list and submit it
                try
                {
                    buffer = instances
                        .Concat(devices) // Add our own instance paths to the existing list
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct() // Remove duplicates, if any
                        .StringArrayToMultiSzPointer(out var length); // Convert to usable buffer

                    // Submit new list
                    bool ok = Kernel32.DeviceIoControl(
                        handle,
                        unchecked((int) IOCTL_SET_BLACKLIST),
                        buffer,
                        length,
                        IntPtr.Zero,
                        0,
                        out _,
                        IntPtr.Zero
                    );
                    if (!ok)
                        throw new Exception("Couldn't set blacklisted devices");
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        public static void whitelistApplications(IList<string> paths, bool keepExisting = true) {
            using (var handle = Kernel32.CreateFile("\\\\.\\HidHide",
            Kernel32.ACCESS_MASK.GenericRight.GENERIC_READ,
            Kernel32.FileShare.FILE_SHARE_READ | Kernel32.FileShare.FILE_SHARE_WRITE,
            IntPtr.Zero, Kernel32.CreationDisposition.OPEN_EXISTING,
            Kernel32.CreateFileFlags.FILE_ATTRIBUTE_NORMAL,
            Kernel32.SafeObjectHandle.Null
            ))
            {
                var buffer = IntPtr.Zero;

                // List of allowed application paths
                IList<string> pathsAllowed = new List<string>();

                // Get existing list of allowed applications
                // This is important to not discard entries other processes potentially made
                // Always get the current list before altering/submitting it
                if (keepExisting)
                {
                    try
                    {
                        // Get required buffer size
                        bool ok = Kernel32.DeviceIoControl(
                            handle,
                            unchecked((int) IOCTL_GET_WHITELIST),
                            IntPtr.Zero,
                            0,
                            IntPtr.Zero,
                            0,
                            out var required,
                            IntPtr.Zero
                        );
                        if (!ok)
                            throw new Exception("Couldn't get whitelisted applications buffer size");

                        buffer = Marshal.AllocHGlobal(required);

                        // Get actual buffer content
                        ok = Kernel32.DeviceIoControl(
                            handle,
                            unchecked((int) IOCTL_GET_WHITELIST),
                            IntPtr.Zero,
                            0,
                            buffer,
                            required,
                            out _,
                            IntPtr.Zero
                        );
                        if (!ok)
                            throw new Exception("Couldn't get whitelisted applications buffer content");

                        // Store existing allow-list in a more manageable "C#" fashion
                        pathsAllowed = buffer.MultiSzPointerToStringArray(required).ToList();
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }

                // Manipulate allow-list and submit it
                try
                {
                    for (int i = 0; i < paths.Count; ++i) {
                        paths[i] = VolumeHelper.PathToDosDevicePath(paths[i]);
                    }
                    buffer = pathsAllowed
                        .Concat(paths) // Add our own instance paths to the existing list
                        .Distinct() // Remove duplicates, if any
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .StringArrayToMultiSzPointer(out var length); // Convert to usable buffer

                    // Submit new list
                    bool ok = Kernel32.DeviceIoControl(
                        handle,
                        unchecked((int) IOCTL_SET_WHITELIST),
                        buffer,
                        length,
                        IntPtr.Zero,
                        0,
                        out _,
                        IntPtr.Zero
                    );
                    if (!ok)
                        throw new Exception("Couldn't set whitelisted applications");
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
    }
}
