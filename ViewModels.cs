using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace QuickConnect
{
    // ════════════════════════════════════════════════════════════════════════════
    //  DeviceStatus
    // ════════════════════════════════════════════════════════════════════════════
    public enum DeviceStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Error,
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  BluetoothDeviceViewModel
    // ════════════════════════════════════════════════════════════════════════════
    public class BluetoothDeviceViewModel : NotifyBase
    {
        // ── Identity ────────────────────────────────────────────────────────── //
        private string _address = string.Empty;
        private string _name    = string.Empty;
        private uint   _classOfDevice;

        public string Address       { get => _address;       set => Set(ref _address, value); }
        public string Name          { get => _name;          set => Set(ref _name, value); }
        public uint   ClassOfDevice { get => _classOfDevice;
            set
            {
                if (Set(ref _classOfDevice, value))
                {
                    // Auto-assign type only if no user override
                    if (!_userOverrideType)
                        DeviceType = DeviceTypeService.GetType(_address, value);
                }
            }
        }

        // ── Device type (auto-detected or user-set) ──────────────────────── //
        private DeviceType _deviceType = DeviceType.Unknown;
        private bool       _userOverrideType;

        public DeviceType DeviceType
        {
            get => _deviceType;
            set
            {
                if (Set(ref _deviceType, value))
                {
                    OnPropertyChanged(nameof(DeviceIcon));
                    OnPropertyChanged(nameof(DeviceTypeLabel));
                    OnPropertyChanged(nameof(ShowEarbudBattery));
                    OnPropertyChanged(nameof(ShowPhoneBattery));
                }
            }
        }

        public string DeviceIcon      => DeviceTypeService.GetGlyph(_deviceType);
        public string DeviceTypeLabel => DeviceTypeService.GetLabel(_deviceType);
        public bool   ShowEarbudBattery => _deviceType is DeviceType.Earbuds or DeviceType.Headphones;
        public bool   ShowPhoneBattery  => _deviceType == DeviceType.Phone;

        public void SetUserDeviceType(DeviceType type)
        {
            _userOverrideType = true;
            DeviceType = type;
            DeviceTypeService.SetOverride(_address, type);
        }

        // ── Connection status ────────────────────────────────────────────── //
        private DeviceStatus _status = DeviceStatus.Disconnected;
        private string       _errorMessage = string.Empty;
        private bool         _isBusy;

        public DeviceStatus Status
        {
            get => _status;
            set
            {
                if (Set(ref _status, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusBrush));
                    OnPropertyChanged(nameof(ButtonLabel));
                    OnPropertyChanged(nameof(CanToggle));
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { if (Set(ref _isBusy, value)) OnPropertyChanged(nameof(CanToggle)); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (Set(ref _errorMessage, value))
                    OnPropertyChanged(nameof(StatusText));
            }
        }

        public string StatusText => Status switch
        {
            DeviceStatus.Connected  => "Connected",
            DeviceStatus.Connecting => "Connecting...",
            DeviceStatus.Error      => string.IsNullOrEmpty(ErrorMessage)
                                           ? "Connection could not be completed"
                                           : ErrorMessage,
            _                       => "Disconnected",
        };

        public Brush StatusBrush => Status switch
        {
            DeviceStatus.Connected  => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
            DeviceStatus.Connecting => new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)),
            DeviceStatus.Error      => new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
            _                       => new SolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75)),
        };

        public string ButtonLabel => Status switch
        {
            DeviceStatus.Connected  => "Disconnect",
            DeviceStatus.Connecting => "Cancel",
            _                       => "Connect",
        };

        public bool CanToggle => !IsBusy || Status == DeviceStatus.Connecting;

        // ── Battery ──────────────────────────────────────────────────────── //
        private int? _batteryLevel;      // combined / phone
        private int? _batteryLeft;
        private int? _batteryRight;
        private int? _batteryCase;

        public int? BatteryLevel { get => _batteryLevel; set { if(Set(ref _batteryLevel, value)) { OnPropertyChanged(nameof(BatteryText)); OnPropertyChanged(nameof(BatteryLow)); } } }
        public int? BatteryLeft  { get => _batteryLeft;  set { if(Set(ref _batteryLeft,  value)) OnPropertyChanged(nameof(EarbudBatteryText)); } }
        public int? BatteryRight { get => _batteryRight; set { if(Set(ref _batteryRight, value)) OnPropertyChanged(nameof(EarbudBatteryText)); } }
        public int? BatteryCase  { get => _batteryCase;  set { if(Set(ref _batteryCase,  value)) OnPropertyChanged(nameof(EarbudBatteryText)); } }

        public bool BatteryLow => _batteryLevel is > 0 and <= 20;

        /// <summary>Single-line battery text for phone/combined level.</summary>
        public string BatteryText => _batteryLevel.HasValue
            ? $"Battery: {_batteryLevel}%"
            : "Battery: Unavailable";

        /// <summary>Compact L/R/Case battery line for earbuds.</summary>
        public string EarbudBatteryText
        {
            get
            {
                string l    = _batteryLeft.HasValue  ? $"{_batteryLeft}%"  : "—";
                string r    = _batteryRight.HasValue ? $"{_batteryRight}%" : "—";
                string c    = _batteryCase.HasValue  ? $"{_batteryCase}%"  : "—";
                // If we have at least a combined level, use that for display
                if (!_batteryLeft.HasValue && !_batteryRight.HasValue && _batteryLevel.HasValue)
                    return $"Battery: {_batteryLevel}%";
                if (!_batteryLeft.HasValue && !_batteryRight.HasValue && !_batteryCase.HasValue)
                    return "Battery: Unavailable";
                return $"L: {l}  R: {r}  Case: {c}";
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  MainViewModel
    // ════════════════════════════════════════════════════════════════════════════
    public class MainViewModel : NotifyBase
    {
        private string _bluetoothStatus      = "Checking...";
        private Brush  _bluetoothStatusBrush = Brushes.Gray;
        private bool   _isRefreshing;
        private string _statusMessage        = string.Empty;

        public ObservableCollection<BluetoothDeviceViewModel> Devices { get; } = [];

        public string BluetoothStatus      { get => _bluetoothStatus;      set => Set(ref _bluetoothStatus,      value); }
        public Brush  BluetoothStatusBrush { get => _bluetoothStatusBrush; set => Set(ref _bluetoothStatusBrush, value); }
        public bool   IsRefreshing         { get => _isRefreshing;         set => Set(ref _isRefreshing,         value); }
        public string StatusMessage        { get => _statusMessage;        set => Set(ref _statusMessage,        value); }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  NotifyBase — shared INotifyPropertyChanged helper
    // ════════════════════════════════════════════════════════════════════════════
    public abstract class NotifyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
