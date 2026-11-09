using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace QuickConnect
{
    // ════════════════════════════════════════════════════════════════════════════
    //  BluetoothService
    //  All Bluetooth I/O.  Win32 bthprops.cpl + WinRT DeviceWatcher.
    // ════════════════════════════════════════════════════════════════════════════
    internal static class BluetoothService
    {
        // ── Win32 P/Invokes ───────────────────────────────────────────────── //

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern nint BluetoothFindFirstRadio(
            ref BLUETOOTH_FIND_RADIO_PARAMS p, out nint phRadio);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindRadioClose(nint hFind);

        [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern nint BluetoothFindFirstDevice(
            ref BLUETOOTH_DEVICE_SEARCH_PARAMS sp, ref BLUETOOTH_DEVICE_INFO di);

        [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool BluetoothFindNextDevice(
            nint hFind, ref BLUETOOTH_DEVICE_INFO di);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindDeviceClose(nint hFind);

        [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint BluetoothGetDeviceInfo(
            nint hRadio, ref BLUETOOTH_DEVICE_INFO di);

        [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool BluetoothSetServiceState(
            nint hRadio, ref BLUETOOTH_DEVICE_INFO di, ref Guid service, uint flags);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern uint BluetoothRemoveDevice(ref ulong pAddress);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(nint h);

        // ── Structs ───────────────────────────────────────────────────────── //

        [StructLayout(LayoutKind.Sequential)]
        private struct BLUETOOTH_FIND_RADIO_PARAMS { public uint dwSize; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BLUETOOTH_DEVICE_INFO
        {
            public uint   dwSize;
            public ulong  Address;
            public uint   ulClassofDevice;
            public bool   fConnected;
            public bool   fRemembered;
            public bool   fAuthenticated;
            public SYSTEMTIME stLastSeen;
            public SYSTEMTIME stLastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
            public string szName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear, wMonth, wDayOfWeek, wDay,
                          wHour, wMinute, wSecond, wMilliseconds;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public uint  dwSize;
            public bool  fReturnAuthenticated;
            public bool  fReturnRemembered;
            public bool  fReturnUnknown;
            public bool  fReturnConnected;
            public bool  fIssueInquiry;
            public byte  cTimeoutMultiplier;
            public nint  hRadio;
        }

        // ── Audio-profile service GUIDs ───────────────────────────────────── //

        private static readonly Guid[] AudioGuids =
        [
            new("0000110B-0000-1000-8000-00805F9B34FB"),  // A2DP Audio Sink
            new("00001108-0000-1000-8000-00805F9B34FB"),  // Headset
            new("0000111E-0000-1000-8000-00805F9B34FB"),  // HFP
            new("0000111F-0000-1000-8000-00805F9B34FB"),  // HFP Audio Gateway
        ];

        private const uint BTHNS_ENABLED  = 1u;
        private const uint BTHNS_DISABLED = 0u;

        // ── Real-time DeviceWatcher ───────────────────────────────────────── //

        // Classic Bluetooth AEP protocol GUID
        private const string BT_AEP_PROTOCOL =
            "System.Devices.Aep.ProtocolId:=\"{E0CBF06C-CD8B-4647-BB8A-263B43F0F974}\"";

        private static readonly string[] WatchedProperties =
        [
            "System.Devices.Aep.IsConnected",
            "System.Devices.Aep.DeviceAddress",
            "System.Devices.BatteryLife",
        ];

        private static DeviceWatcher? _watcher;

        /// <summary>Fired on the thread-pool when Windows reports a connection/disconnection.</summary>
        public static event Action<ulong, bool>?  DeviceConnectionChanged;
        /// <summary>Fired when Windows reports a battery-level change for a device.</summary>
        public static event Action<ulong, int>?   DeviceBatteryChanged;

        public static void StartWatcher()
        {
            if (_watcher != null) return;

            _watcher = DeviceInformation.CreateWatcher(
                BT_AEP_PROTOCOL, WatchedProperties,
                DeviceInformationKind.AssociationEndpoint);

            _watcher.Updated += OnWatcherUpdated;
            _watcher.Start();
        }

        public static void StopWatcher()
        {
            if (_watcher == null) return;
            _watcher.Updated -= OnWatcherUpdated;
            try { _watcher.Stop(); } catch { }
            _watcher = null;
        }

        private static void OnWatcherUpdated(DeviceWatcher _, DeviceInformationUpdate args)
        {
            ulong addr = AepIdToAddress(args.Id);
            if (addr == 0) return;

            if (args.Properties.TryGetValue("System.Devices.Aep.IsConnected", out object? conn)
                && conn is bool isConnected)
            {
                DeviceConnectionChanged?.Invoke(addr, isConnected);
            }

            if (args.Properties.TryGetValue("System.Devices.BatteryLife", out object? bat)
                && bat is int level)
            {
                DeviceBatteryChanged?.Invoke(addr, level);
            }
        }

        /// <summary>Converts a Bluetooth AEP device ID like
        /// "Bluetooth#Bluetooth00:00:00:00:00:00-aa:bb:cc:dd:ee:ff"
        /// to the 6-byte address integer.</summary>
        private static ulong AepIdToAddress(string id)
        {
            try
            {
                // e.g. "Bluetooth#BluetoothXX:XX:XX:XX:XX:XX-AA:BB:CC:DD:EE:FF"
                int dash = id.LastIndexOf('-');
                if (dash < 0) return 0;
                string addrPart = id[(dash + 1)..].Replace(":", "");
                return Convert.ToUInt64(addrPart, 16);
            }
            catch { return 0; }
        }

        // ── Bluetooth adapter state ───────────────────────────────────────── //

        public static async Task<bool> IsBluetoothEnabledAsync()
        {
            try
            {
                var adapter = await BluetoothAdapter.GetDefaultAsync();
                if (adapter == null) return false;
                var radio = await adapter.GetRadioAsync();
                return radio?.State == RadioState.On;
            }
            catch { return false; }
        }

        // ── Enumerate paired devices (Win32) ──────────────────────────────── //

        public static Task<List<BtDeviceInfo>> GetPairedDevicesAsync()
            => Task.Run(EnumeratePaired);

        private static List<BtDeviceInfo> EnumeratePaired()
        {
            var results = new List<BtDeviceInfo>();

            var rp = new BLUETOOTH_FIND_RADIO_PARAMS
                { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
            nint hFindRadio = BluetoothFindFirstRadio(ref rp, out nint hRadio);
            if (hFindRadio == nint.Zero) return results;

            try
            {
                var sp = new BLUETOOTH_DEVICE_SEARCH_PARAMS
                {
                    dwSize               = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
                    fReturnAuthenticated = true,
                    fReturnRemembered    = true,
                    fReturnUnknown       = false,
                    fReturnConnected     = true,
                    fIssueInquiry        = false,
                    cTimeoutMultiplier   = 1,
                    hRadio               = hRadio
                };

                var di = BlankDi();
                nint hFind = BluetoothFindFirstDevice(ref sp, ref di);
                if (hFind == nint.Zero) return results;

                try
                {
                    do
                    {
                        if (!string.IsNullOrWhiteSpace(di.szName))
                        {
                            results.Add(new BtDeviceInfo
                            {
                                Address          = di.Address,
                                Name             = di.szName.Trim('\0'),
                                IsConnected      = di.fConnected,
                                ClassOfDevice    = di.ulClassofDevice
                            });
                        }
                        di = BlankDi();
                    }
                    while (BluetoothFindNextDevice(hFind, ref di));
                }
                finally { BluetoothFindDeviceClose(hFind); }
            }
            finally
            {
                CloseHandle(hRadio);
                BluetoothFindRadioClose(hFindRadio);
            }

            return results;
        }

        // ── Connection state query ────────────────────────────────────────── //

        /// <returns>true=connected, false=disconnected, null=radio unavailable</returns>
        public static bool? GetConnectionStatus(ulong address)
        {
            var rp = new BLUETOOTH_FIND_RADIO_PARAMS
                { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
            nint hFindRadio = BluetoothFindFirstRadio(ref rp, out nint hRadio);
            if (hFindRadio == nint.Zero) return null;

            try
            {
                var di = BlankDi();
                di.Address = address;
                uint err = BluetoothGetDeviceInfo(hRadio, ref di);
                return err == 0 ? di.fConnected : null;
            }
            finally
            {
                CloseHandle(hRadio);
                BluetoothFindRadioClose(hFindRadio);
            }
        }

        public static Task<bool?> GetConnectionStatusAsync(ulong address)
            => Task.Run(() => GetConnectionStatus(address));

        // ── Connect ───────────────────────────────────────────────────────── //

        /// <summary>
        /// Triggers Windows to connect the paired device by enabling its audio
        /// service profiles, then polls for actual connection confirmation.
        /// Throws <see cref="ConnectException"/> with a human-readable reason on failure.
        /// </summary>
        public static async Task ConnectAsync(ulong address, CancellationToken ct)
        {
            // Check adapter is on
            if (!await IsBluetoothEnabledAsync())
                throw new ConnectException("Failed to connect: Bluetooth is turned off.");

            // Check the device is known
            bool? initialState = await Task.Run(() => GetConnectionStatus(address), ct);
            if (initialState == null)
                throw new ConnectException("Failed to connect: Bluetooth adapter is unavailable.");

            if (initialState == true) return; // already connected

            // Issue the service-enable command
            bool commanded = await Task.Run(() => IssueConnect(address), ct);
            if (!commanded)
                throw new ConnectException("Failed to connect: Device rejected the connection.");

            // Poll until actually connected (up to 60 s)
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 60)
            {
                ct.ThrowIfCancellationRequested();

                bool? status = await Task.Run(() => GetConnectionStatus(address), ct);
                if (status == true) return;         // 
                if (status == null)
                    throw new ConnectException("Failed to connect: Bluetooth adapter is unavailable.");

                await Task.Delay(700, ct);
            }

            throw new ConnectException("Failed to connect: Connection timed out.");
        }

        /// <summary>
        /// Issues BluetoothSetServiceState for each audio profile.
        /// THE FIX: calls BluetoothGetDeviceInfo first to populate fAuthenticated.
        /// </summary>
        private static bool IssueConnect(ulong address)
        {
            var rp = new BLUETOOTH_FIND_RADIO_PARAMS
                { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
            nint hFindRadio = BluetoothFindFirstRadio(ref rp, out nint hRadio);
            if (hFindRadio == nint.Zero) return false;

            bool anyOk = false;
            try
            {
                // ─────────────────────────────────────────────────────────────
                // CRITICAL FIX: Get the FULL device info first.
                // This fills fAuthenticated = true for paired devices.
                // BluetoothSetServiceState requires fAuthenticated = true to
                // trigger a connection; without it, the call silently fails.
                // ─────────────────────────────────────────────────────────────
                var di = BlankDi();
                di.Address = address;
                uint infoErr = BluetoothGetDeviceInfo(hRadio, ref di);
                if (infoErr != 0) return false;  // device not found in radio

                foreach (var guid in AudioGuids)
                {
                    var g = guid;
                    if (BluetoothSetServiceState(hRadio, ref di, ref g, BTHNS_ENABLED))
                        anyOk = true;
                }
            }
            finally
            {
                CloseHandle(hRadio);
                BluetoothFindRadioClose(hFindRadio);
            }

            return anyOk;
        }

        // ── Disconnect ────────────────────────────────────────────────────── //

        public static Task DisconnectAsync(ulong address)
            => Task.Run(() => IssueDisconnect(address));

        private static void IssueDisconnect(ulong address)
        {
            var rp = new BLUETOOTH_FIND_RADIO_PARAMS
                { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
            nint hFindRadio = BluetoothFindFirstRadio(ref rp, out nint hRadio);
            if (hFindRadio == nint.Zero) return;

            try
            {
                var di = BlankDi();
                di.Address = address;
                BluetoothGetDeviceInfo(hRadio, ref di); // populate fAuthenticated

                foreach (var guid in AudioGuids)
                {
                    var g = guid;
                    BluetoothSetServiceState(hRadio, ref di, ref g, BTHNS_DISABLED);
                }
            }
            finally
            {
                CloseHandle(hRadio);
                BluetoothFindRadioClose(hFindRadio);
            }
        }

        // ── Forget device ─────────────────────────────────────────────────── //

        /// <summary>
        /// Removes the Bluetooth pairing from Windows.
        /// Uses Win32 BluetoothRemoveDevice.
        /// </summary>
        public static async Task<string?> ForgetDeviceAsync(ulong address)
        {
            return await Task.Run(() =>
            {
                ulong addr = address;
                uint err = BluetoothRemoveDevice(ref addr);
                if (err == 0) return null;  // success
                return err switch
                {
                    2    => "Device not found in Windows pairing list.",
                    5    => "Access denied. Try running as administrator.",
                    1168 => "Device not found.",
                    _    => $"Failed to remove device (error {err})."
                };
            });
        }

        // ── Battery ───────────────────────────────────────────────────────── //

        /// <summary>
        /// Attempts to read the battery level for a Bluetooth device.
        /// Tries (in order):
        ///   1. Windows device property "System.Devices.BatteryLife" (classic + LE)
        ///   2. GATT Battery Service 0x180F / characteristic 0x2A19 (LE only)
        /// Returns null if unavailable.
        /// </summary>
        public static async Task<int?> GetBatteryLevelAsync(ulong address)
        {
            // ── Try Windows AEP device property ──────────────────────────── //
            try
            {
                // Build the AEP selector for this specific device
                string addrStr = $"{(address >> 40) & 0xFF:X2}:{(address >> 32) & 0xFF:X2}" +
                                 $":{(address >> 24) & 0xFF:X2}:{(address >> 16) & 0xFF:X2}" +
                                 $":{(address >> 8) & 0xFF:X2}:{address & 0xFF:X2}";

                // Find by Bluetooth address in AEP
                string selector = $"System.Devices.Aep.ProtocolId:=" +
                                  $"\"{{E0CBF06C-CD8B-4647-BB8A-263B43F0F974}}\"";
                var devices = await DeviceInformation.FindAllAsync(selector,
                    new[] { "System.Devices.Aep.DeviceAddress", "System.Devices.BatteryLife" });

                foreach (var dev in devices)
                {
                    if (dev.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out object? devAddr)
                        && devAddr is string addrVal
                        && addrVal.Replace(":", "").Equals(addrStr.Replace(":", ""),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (dev.Properties.TryGetValue("System.Devices.BatteryLife", out object? bat)
                            && bat is int level && level >= 0 && level <= 100)
                        {
                            return level;
                        }
                    }
                }
            }
            catch { }

            // ── Try GATT Battery Service (BLE devices) ────────────────────── //
            try
            {
                var leDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
                if (leDevice != null)
                {
                    var svcResult = await leDevice.GetGattServicesForUuidAsync(
                        GattServiceUuids.Battery,
                        BluetoothCacheMode.Uncached);

                    if (svcResult.Status == GattCommunicationStatus.Success
                        && svcResult.Services.Count > 0)
                    {
                        var svc = svcResult.Services[0];
                        var charResult = await svc.GetCharacteristicsForUuidAsync(
                            GattCharacteristicUuids.BatteryLevel,
                            BluetoothCacheMode.Uncached);

                        if (charResult.Status == GattCommunicationStatus.Success
                            && charResult.Characteristics.Count > 0)
                        {
                            var readResult = await charResult.Characteristics[0]
                                .ReadValueAsync(BluetoothCacheMode.Uncached);

                            if (readResult.Status == GattCommunicationStatus.Success)
                            {
                                var reader = Windows.Storage.Streams.DataReader
                                    .FromBuffer(readResult.Value);
                                byte level = reader.ReadByte();
                                return level;
                            }
                        }
                    }
                }
            }
            catch { }

            return null; // unavailable
        }

        // ── Helpers ───────────────────────────────────────────────────────── //

        private static BLUETOOTH_DEVICE_INFO BlankDi() =>
            new() { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>() };
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Custom exception for connection failures with user-readable messages
    // ════════════════════════════════════════════════════════════════════════════
    public class ConnectException(string message) : Exception(message) { }

    // ════════════════════════════════════════════════════════════════════════════
    //  BtDeviceInfo — raw data from Win32 enumeration
    // ════════════════════════════════════════════════════════════════════════════
    public record BtDeviceInfo
    {
        public ulong  Address       { get; init; }
        public string Name          { get; init; } = string.Empty;
        public bool   IsConnected   { get; init; }
        public uint   ClassOfDevice { get; init; }

        public string AddressString =>
            $"{(Address >> 40) & 0xFF:X2}:{(Address >> 32) & 0xFF:X2}:" +
            $"{(Address >> 24) & 0xFF:X2}:{(Address >> 16) & 0xFF:X2}:" +
            $"{(Address >> 8) & 0xFF:X2}:{Address & 0xFF:X2}";
    }
}
