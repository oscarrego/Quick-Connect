using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace QuickConnect
{
    public enum DeviceStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Error
    }

    public class BluetoothDeviceViewModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private DeviceStatus _status = DeviceStatus.Disconnected;
        private string _errorMessage = string.Empty;
        private bool _isBusy;
        private string _address = string.Empty;

        public string Address
        {
            get => _address;
            set => SetField(ref _address, value);
        }

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public DeviceStatus Status
        {
            get => _status;
            set
            {
                if (SetField(ref _status, value))
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
            set
            {
                if (SetField(ref _isBusy, value))
                    OnPropertyChanged(nameof(CanToggle));
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetField(ref _errorMessage, value);
        }

        public string StatusText => Status switch
        {
            DeviceStatus.Connected    => "Connected",
            DeviceStatus.Connecting   => "Connecting...",
            DeviceStatus.Error        => string.IsNullOrEmpty(ErrorMessage) ? "Error" : ErrorMessage,
            _                         => "Disconnected"
        };

        public Brush StatusBrush => Status switch
        {
            DeviceStatus.Connected    => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
            DeviceStatus.Connecting   => new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)),
            DeviceStatus.Error        => new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
            _                         => new SolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75))
        };

        public string ButtonLabel => Status switch
        {
            DeviceStatus.Connected    => "Disconnect",
            DeviceStatus.Connecting   => "Cancel",
            _                         => "Connect"
        };

        public bool CanToggle => !IsBusy || Status == DeviceStatus.Connecting;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private string _bluetoothStatus = "Checking...";
        private Brush _bluetoothStatusBrush = Brushes.Gray;
        private bool _isRefreshing;
        private string _statusMessage = string.Empty;

        public ObservableCollection<BluetoothDeviceViewModel> Devices { get; } = new();

        public string BluetoothStatus
        {
            get => _bluetoothStatus;
            set => SetField(ref _bluetoothStatus, value);
        }

        public Brush BluetoothStatusBrush
        {
            get => _bluetoothStatusBrush;
            set => SetField(ref _bluetoothStatusBrush, value);
        }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set => SetField(ref _isRefreshing, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetField(ref _statusMessage, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
