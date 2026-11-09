using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Radios;

namespace QuickConnect
{
    /// <summary>
    /// All Bluetooth operations use Win32 bthprops.cpl P/Invokes.
    /// No NuGet packages; WinRT is used only for adapter radio state.
    /// </summary>
    internal static class BluetoothService
    {
        // ── Win32 imports ─────────────────────────────────────────────────── //

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern nint BluetoothFindFirstRadio(
            ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp, out nint phRadio);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindRadioClose(nint hFind);

        [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern nint BluetoothFindFirstDevice(
            ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool BluetoothFindNextDevice(
            nint hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindDeviceClose(nint hFind);

        [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint BluetoothGetDeviceInfo(nint hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool BluetoothSetServiceState(
            nint hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref Guid pGuidService, uint dwServiceFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(nint hObject);

        // ── Structs ───────────────────────────────────────────────────────── //

        [StructLayout(LayoutKind.Sequential)]
        private struct BLUETOOTH_FIND_RADIO_PARAMS { public uint dwSize; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BLUETOOTH_DEVICE_INFO
        {
            public uint dwSize;
            public ulong Address;
            public uint ulClassofDevice;
            public bool fConnected;
            public bool fRemembered;
            public bool fAuthenticated;
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
            public uint dwSize;
            public bool fReturnAuthenticated;
            public bool fReturnRemembered;
            public bool fReturnUnknown;
            public bool fReturnConnected;
            public bool fIssueInquiry;
            public byte cTimeoutMultiplier;
            public nint hRadio;
        }

        // ── Audio profile GUIDs ───────────────────────────────────────────── //

        private static readonly Guid AudioSinkGuid  = new("0000110B-0000-1000-8000-00805F9B34FB");
        private static readonly Guid HeadsetGuid    = new("00001108-0000-1000-8000-00805F9B34FB");
        private static readonly Guid HfpGuid        = new("0000111E-0000-1000-8000-00805F9B34FB");
        private static readonly Guid HfpAudioGuid   = new("0000111F-0000-1000-8000-00805F9B34FB");

        private const uint BTHNS_ENABLED  = 0x00000001u;
        private const uint BTHNS_DISABLED = 0x00000000u;

        // ── Adapter state ─────────────────────────────────────────────────── //

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

        // ── Enumerate paired devices ──────────────────────────────────────── //

        public static Task<List<BluetoothDeviceInfo>> GetPairedDevicesAsync()
            => Task.Run(EnumeratePairedDevices);

        private static List<BluetoothDeviceInfo> EnumeratePairedDevices()
        {
            var results = new List<BluetoothDeviceInfo>();

            var rp = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
            nint hFindRadio = BluetoothFindFirstRadio(ref rp, out nint hRadio);
            if (hFindRadio == nint.Zero) return results;

            try
            {
                var sp = new BLUETOOTH_DEVICE_SEARCH_PARAMS
                {
                    dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
                    fReturnAuthenticated = true,
                    fReturnRemembered    = true,
                    fReturnUnknown       = false,
                    fReturnConnected     = true,
                    fIssueInquiry        = false,
                    cTimeoutMultiplier   = 1,
                    hRadio               = hRadio
                };

                var di = new BLUETOOTH_DEVICE_INFO { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>() };
                nint hFind = BluetoothFindFirstDevice(ref sp, ref di);

                if (hFind != nint.Zero)
                {
                    try
                    {
                        do
                        {
                            if (!string.IsNullOrWhiteSpace(di.szName))
                            {
                                results.Add(new BluetoothDeviceInfo
                                {
                                    Address     = di.Address,
                                    Name        = di.szName.Trim('\0'),
                                    IsConnected = di.fConnected
                                });
                            }
                            di = new BLUETOOTH_DEVICE_INFO { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>() };
                        }
                        while (BluetoothFindNextDevice(hFind, ref di));
                    }
                    finally { BluetoothFindDeviceClose(hFind); }
                }
            }
            finally
            {
                CloseHandle(hRadio);
                BluetoothFindRadioClose(hFindRadio);
            }

            return results;
        }

        // ── Connect — polls until actually connected or times out ─────────── //

        /// <summary>
        /// Issues the Bluetooth connect command for all audio profiles, then polls
        /// BluetoothGetDeviceInfo every 600 ms for up to <paramref name="timeoutSeconds"/>
        /// seconds. Returns <see langword="true"/> only when the device reports
        /// fConnected == true. Returns <see langword="false"/> on timeout or
        /// throws <see cref="OperationCanceledException"/> on cancellation.
        /// </summary>
        public static async Task<bool> ConnectDeviceAsync(
            ulong address, CancellationToken ct, int timeoutSeconds = 15)
        {
            // Fire the service-state command on a thread-pool thread
            await Task.Run(() => SetAudioServiceState(address, enable: true), ct);

            // Poll the actual connection flag
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeoutSeconds)
            {
                ct.ThrowIfCancellationRequested();

                bool? connected = GetWin32ConnectionStatus(address);
                if (connected == true)
                    return true;

                await Task.Delay(600, ct);
            }

            // Timed out without seeing fConnected == true → genuine failure
            return false;
        }

        // ── Disconnect ────────────────────────────────────────────────────── //

        public static Task DisconnectDeviceAsync(ulong address)
            => Task.Run(() => SetAudioServiceState(address, enable: false));

        // ── Single-device status refresh ─────────────────────────────────── //

        public static Task<bool> GetConnectionStatusAsync(ulong address)
            => Task.Run(() => GetWin32ConnectionStatus(address) ?? false);

        // ── Internal helpers ──────────────────────────────────────────────── //

        /// <returns>
        /// <see langword="true"/> = connected, <see langword="false"/> = not connected,
        /// <see langword="null"/> = radio handle could not be opened.
        /// </returns>
        internal static bool? GetWin32ConnectionStatus(ulong address)
        {
            var rp = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
            nint hFindRadio = BluetoothFindFirstRadio(ref rp, out nint hRadio);
            if (hFindRadio == nint.Zero) return null;

            try
            {
                var di = new BLUETOOTH_DEVICE_INFO
                {
                    dwSize  = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(),
                    Address = address
                };
                uint err = BluetoothGetDeviceInfo(hRadio, ref di);
                return err == 0 ? di.fConnected : null;
            }
            finally
            {
                CloseHandle(hRadio);
                BluetoothFindRadioClose(hFindRadio);
            }
        }

        private static void SetAudioServiceState(ulong address, bool enable)
        {
            var rp = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
            nint hFindRadio = BluetoothFindFirstRadio(ref rp, out nint hRadio);
            if (hFindRadio == nint.Zero) return;

            try
            {
                var di = new BLUETOOTH_DEVICE_INFO
                {
                    dwSize  = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(),
                    Address = address
                };
                uint flag = enable ? BTHNS_ENABLED : BTHNS_DISABLED;

                foreach (var guid in new[] { AudioSinkGuid, HeadsetGuid, HfpGuid, HfpAudioGuid })
                {
                    var g = guid;
                    BluetoothSetServiceState(hRadio, ref di, ref g, flag);
                }
            }
            finally
            {
                CloseHandle(hRadio);
                BluetoothFindRadioClose(hFindRadio);
            }
        }
    }

    public record BluetoothDeviceInfo
    {
        public ulong  Address     { get; init; }
        public string Name        { get; init; } = string.Empty;
        public bool   IsConnected { get; init; }

        public string AddressString =>
            string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                (Address >> 40) & 0xFF,
                (Address >> 32) & 0xFF,
                (Address >> 24) & 0xFF,
                (Address >> 16) & 0xFF,
                (Address >>  8) & 0xFF,
                 Address        & 0xFF);
    }
}
