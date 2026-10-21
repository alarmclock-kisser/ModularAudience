using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ModularAudience.Forms.Hotkeys
{
    public sealed class KeyboardDeviceTracker : IDisposable
    {
        private const ushort HidUsagePageGeneric = 0x01;
        private const ushort HidUsageKeyboard = 0x06;

        private const uint RidInput = 0x10000003;
        private const uint RidiDeviceName = 0x20000007;
        private const uint RimTypeKeyboard = 1;
        private const uint RidevRemove = 0x00000001;
        private const uint RidevInputSink = 0x00000100;
        private const uint RidevDevNotify = 0x00002000;
        private const uint RidKeyBreak = 0x01;
        private const uint MapVkScToVkEx = 3;

        private const int WmInput = 0x00FF;
        private const int WmInputDeviceChange = 0x00FE;

        private static readonly IntPtr HwndMessage = new(-3);

        private readonly object _sync = new();
        private readonly Dictionary<Keys, RawKeyEvent> _pendingKeyDowns = [];
        private readonly Dictionary<IntPtr, KeyboardDeviceInfo> _deviceCache = [];
        private readonly RawInputWindow _window;
        private bool _disposed;

        public event Action? KeyboardsChanged;

        public KeyboardDeviceTracker()
        {
            this._window = new RawInputWindow(this);
            try
            {
                this._window.CreateHandle(new CreateParams
                {
                    Parent = HwndMessage,
                    Caption = "ModularAudience Raw Input"
                });

                RAWINPUTDEVICE[] devices =
                [
                    new RAWINPUTDEVICE
                    {
                        UsagePage = HidUsagePageGeneric,
                        Usage = HidUsageKeyboard,
                        Flags = RidevInputSink | RidevDevNotify,
                        Target = this._window.Handle
                    }
                ];

                if (!RegisterRawInputDevices(
                    devices,
                    (uint)devices.Length,
                    (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                {
                    System.Diagnostics.Debug.WriteLine(
                        "Hotkeys: raw keyboard input registration failed.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Hotkeys: could not initialize raw keyboard input: {ex.Message}");
            }
        }

        public IReadOnlyList<HotkeyKeyboardProfile> GetConnectedKeyboards()
        {
            List<HotkeyKeyboardProfile> profiles = [];
            uint count = 0;
            uint entrySize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();

            if (GetRawInputDeviceList(IntPtr.Zero, ref count, entrySize) == uint.MaxValue || count == 0)
            {
                return profiles;
            }

            IntPtr buffer = Marshal.AllocHGlobal(checked((int)(count * entrySize)));
            try
            {
                uint result = GetRawInputDeviceList(buffer, ref count, entrySize);
                if (result == uint.MaxValue)
                {
                    return profiles;
                }

                for (int index = 0; index < count; index++)
                {
                    IntPtr entryPointer = IntPtr.Add(buffer, index * (int)entrySize);
                    RAWINPUTDEVICELIST entry = Marshal.PtrToStructure<RAWINPUTDEVICELIST>(entryPointer);
                    if (entry.Type != RimTypeKeyboard)
                    {
                        continue;
                    }

                    KeyboardDeviceInfo? device = this.GetDeviceInfo(entry.Device);
                    if (device == null)
                    {
                        continue;
                    }

                    profiles.Add(new HotkeyKeyboardProfile
                    {
                        HardwareId = device.HardwareId,
                        DisplayName = device.DisplayName,
                        IsConnected = true
                    });
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return profiles
                .GroupBy(profile => profile.HardwareId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => profile.HardwareId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string? TakeKeyboardId(Keys key)
        {
            key &= Keys.KeyCode;
            lock (this._sync)
            {
                if (!this._pendingKeyDowns.TryGetValue(key, out RawKeyEvent rawEvent))
                {
                    return null;
                }

                this._pendingKeyDowns.Remove(key);
                return (DateTime.UtcNow - rawEvent.TimestampUtc).TotalMilliseconds <= 750
                    ? rawEvent.HardwareId
                    : null;
            }
        }

        public void ClearPendingKeyEvents()
        {
            lock (this._sync)
            {
                this._pendingKeyDowns.Clear();
            }
        }

        private void HandleRawInput(IntPtr rawInputHandle)
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
            uint result = GetRawInputData(
                rawInputHandle,
                RidInput,
                IntPtr.Zero,
                ref size,
                headerSize);

            if (result == uint.MaxValue || size == 0)
            {
                return;
            }

            IntPtr buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                if (GetRawInputData(rawInputHandle, RidInput, buffer, ref size, headerSize) == uint.MaxValue)
                {
                    return;
                }

                RAWINPUT input = Marshal.PtrToStructure<RAWINPUT>(buffer);
                if (input.Header.Type != RimTypeKeyboard)
                {
                    return;
                }

                if ((input.Keyboard.Flags & RidKeyBreak) != 0)
                {
                    return;
                }

                Keys key = (Keys)input.Keyboard.VKey;
                if (key == Keys.None || input.Keyboard.VKey == 0xFF)
                {
                    uint mapped = MapVirtualKey(input.Keyboard.MakeCode, MapVkScToVkEx);
                    if (mapped != 0)
                    {
                        key = (Keys)mapped;
                    }
                }

                key = HotkeyFormatter.NormalizeKey(key);
                if (key == Keys.None)
                {
                    return;
                }

                KeyboardDeviceInfo? device = this.GetDeviceInfo(input.Header.Device);
                if (device == null)
                {
                    return;
                }

                lock (this._sync)
                {
                    this._pendingKeyDowns[key] = new RawKeyEvent(device.HardwareId, DateTime.UtcNow);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void HandleDeviceChange()
        {
            lock (this._sync)
            {
                this._deviceCache.Clear();
            }

            this.KeyboardsChanged?.Invoke();
        }

        private KeyboardDeviceInfo? GetDeviceInfo(IntPtr deviceHandle)
        {
            if (deviceHandle == IntPtr.Zero)
            {
                return null;
            }

            lock (this._sync)
            {
                if (this._deviceCache.TryGetValue(deviceHandle, out KeyboardDeviceInfo? cached))
                {
                    return cached;
                }
            }

            string devicePath = GetDevicePath(deviceHandle);
            string hardwareId = NormalizeDevicePath(devicePath);
            if (hardwareId.Length == 0)
            {
                return null;
            }

            KeyboardDeviceInfo info = new()
            {
                HardwareId = hardwareId,
                DisplayName = GetDisplayName(hardwareId)
            };

            lock (this._sync)
            {
                this._deviceCache[deviceHandle] = info;
            }

            return info;
        }

        private static string GetDevicePath(IntPtr deviceHandle)
        {
            uint size = 0;
            GetRawInputDeviceInfo(deviceHandle, RidiDeviceName, IntPtr.Zero, ref size);
            if (size == 0)
            {
                return string.Empty;
            }

            IntPtr buffer = Marshal.AllocHGlobal(checked((int)size * sizeof(char)));
            try
            {
                uint result = GetRawInputDeviceInfo(deviceHandle, RidiDeviceName, buffer, ref size);
                return result == uint.MaxValue
                    ? string.Empty
                    : Marshal.PtrToStringUni(buffer) ?? string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string NormalizeDevicePath(string? devicePath)
        {
            if (string.IsNullOrWhiteSpace(devicePath))
            {
                return string.Empty;
            }

            string value = devicePath.Trim();
            if (value.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                value = value[4..];
            }

            int classGuidIndex = value.LastIndexOf("#{", StringComparison.Ordinal);
            if (classGuidIndex > 0)
            {
                string suffix = value[classGuidIndex..];
                if (suffix.Length == 39 && suffix.EndsWith('}'))
                {
                    value = value[..classGuidIndex];
                }
            }

            return HotkeyKeyboardIds.Normalize(value);
        }

        private static string GetDisplayName(string hardwareId)
        {
            string? friendlyName = ReadRegistryDescription(hardwareId);
            if (!string.IsNullOrWhiteSpace(friendlyName))
            {
                return friendlyName;
            }

            string? ids = ExtractVidPid(hardwareId);
            return ids == null
                ? "Keyboard " + HotkeyKeyboardIds.GetShortDisplay(hardwareId)
                : "Keyboard " + ids;
        }

        private static string? ReadRegistryDescription(string hardwareId)
        {
            try
            {
                string registryPath = @"SYSTEM\CurrentControlSet\Enum\" + hardwareId.Replace('#', '\\');
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(registryPath);
                if (key == null)
                {
                    return null;
                }

                return CleanRegistryDescription(key.GetValue("FriendlyName") as string)
                    ?? CleanRegistryDescription(key.GetValue("DeviceDesc") as string);
            }
            catch
            {
                return null;
            }
        }

        private static string? CleanRegistryDescription(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            int separator = value.LastIndexOf(';');
            string cleaned = separator >= 0 ? value[(separator + 1)..] : value;
            cleaned = cleaned.Trim();
            return cleaned.Length == 0 ? null : cleaned;
        }

        private static string? ExtractVidPid(string hardwareId)
        {
            int vendorIndex = hardwareId.IndexOf("VID_", StringComparison.Ordinal);
            if (vendorIndex < 0 || vendorIndex + 8 > hardwareId.Length)
            {
                return null;
            }

            int productIndex = hardwareId.IndexOf("PID_", vendorIndex + 8, StringComparison.Ordinal);
            if (productIndex < 0 || productIndex + 8 > hardwareId.Length)
            {
                return null;
            }

            string vendor = hardwareId.Substring(vendorIndex + 4, 4);
            string product = hardwareId.Substring(productIndex + 4, 4);
            return $"VID {vendor} PID {product}";
        }

        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;

            try
            {
                RAWINPUTDEVICE[] devices =
                [
                    new RAWINPUTDEVICE
                    {
                        UsagePage = HidUsagePageGeneric,
                        Usage = HidUsageKeyboard,
                        Flags = RidevRemove,
                        Target = IntPtr.Zero
                    }
                ];

                RegisterRawInputDevices(
                    devices,
                    (uint)devices.Length,
                    (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            }
            catch
            {
            }

            try
            {
                if (this._window.Handle != IntPtr.Zero)
                {
                    this._window.DestroyHandle();
                }
            }
            catch
            {
            }

            lock (this._sync)
            {
                this._pendingKeyDowns.Clear();
                this._deviceCache.Clear();
            }
        }

        private readonly record struct RawKeyEvent(string HardwareId, DateTime TimestampUtc);

        private sealed class KeyboardDeviceInfo
        {
            public string HardwareId { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
        }

        private sealed class RawInputWindow : NativeWindow
        {
            private readonly KeyboardDeviceTracker _tracker;

            public RawInputWindow(KeyboardDeviceTracker tracker)
            {
                this._tracker = tracker;
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == WmInput)
                {
                    this._tracker.HandleRawInput(message.LParam);
                    message.Result = IntPtr.Zero;
                    return;
                }

                if (message.Msg == WmInputDeviceChange)
                {
                    this._tracker.HandleDeviceChange();
                }

                base.WndProc(ref message);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICELIST
        {
            public IntPtr Device;
            public uint Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWKEYBOARD
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUT
        {
            public RAWINPUTHEADER Header;
            public RAWKEYBOARD Keyboard;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(
            [In] RAWINPUTDEVICE[] devices,
            uint deviceCount,
            uint structSize);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(
            IntPtr rawInputHandle,
            uint command,
            IntPtr data,
            ref uint size,
            uint headerSize);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputDeviceList(
            IntPtr deviceList,
            ref uint deviceCount,
            uint structSize);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfo(
            IntPtr deviceHandle,
            uint command,
            IntPtr data,
            ref uint size);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);
    }
}
