using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace QuickConnect
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm = new();
        private readonly DispatcherTimer _refreshTimer;
        private readonly Dictionary<string, CancellationTokenSource> _connectCts = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(8)
            };
            _refreshTimer.Tick += async (_, _) => await RefreshStatusesAsync(fullScan: false);

            Loaded += async (_, _) =>
            {
                await RefreshStatusesAsync(fullScan: true);
                _refreshTimer.Start();
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            _refreshTimer.Stop();
            foreach (var cts in _connectCts.Values) cts.Cancel();
            base.OnClosed(e);
        }

        // ── Refresh ─────────────────────────────────────────────────────── //

        private async Task RefreshStatusesAsync(bool fullScan)
        {
            if (_vm.IsRefreshing) return;
            _vm.IsRefreshing = true;

            try
            {
                bool btOn = await BluetoothService.IsBluetoothEnabledAsync();
                _vm.BluetoothStatus = btOn ? "On" : "Off";
                _vm.BluetoothStatusBrush = btOn
                    ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
                    : new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

                if (!btOn)
                {
                    _vm.Devices.Clear();
                    _vm.StatusMessage = "Bluetooth is turned off.";
                    return;
                }

                if (fullScan)
                {
                    var devices = await BluetoothService.GetPairedDevicesAsync();

                    var toRemove = _vm.Devices
                        .Where(d => !devices.Any(r => r.AddressString == d.Address))
                        .ToList();
                    foreach (var d in toRemove) _vm.Devices.Remove(d);

                    foreach (var info in devices)
                    {
                        var existing = _vm.Devices.FirstOrDefault(d => d.Address == info.AddressString);
                        if (existing == null)
                        {
                            existing = new BluetoothDeviceViewModel
                            {
                                Address = info.AddressString,
                                Name = info.Name
                            };
                            _vm.Devices.Add(existing);
                        }

                        if (existing.Status != DeviceStatus.Connecting)
                            existing.Status = info.IsConnected ? DeviceStatus.Connected : DeviceStatus.Disconnected;
                    }

                    _vm.StatusMessage = devices.Count == 0 ? "No paired devices found." : string.Empty;
                }
                else
                {
                    foreach (var device in _vm.Devices.ToList())
                    {
                        if (device.Status == DeviceStatus.Connecting) continue;
                        if (!ulong.TryParse(device.Address.Replace(":", ""),
                            System.Globalization.NumberStyles.HexNumber, null, out ulong addr)) continue;

                        bool connected = await BluetoothService.GetConnectionStatusAsync(addr);
                        device.Status = connected ? DeviceStatus.Connected : DeviceStatus.Disconnected;
                    }
                }
            }
            catch (Exception ex)
            {
                _vm.StatusMessage = $"Refresh error: {ex.Message}";
            }
            finally
            {
                _vm.IsRefreshing = false;
            }
        }

        // ── Button handlers ─────────────────────────────────────────────── //

        private async void OnToggleClick(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            if (btn.DataContext is not BluetoothDeviceViewModel device) return;
            if (!ulong.TryParse(device.Address.Replace(":", ""),
                System.Globalization.NumberStyles.HexNumber, null, out ulong addr)) return;

            switch (device.Status)
            {
                case DeviceStatus.Connected:
                    await DisconnectAsync(device, addr);
                    break;
                case DeviceStatus.Connecting:
                    CancelConnect(device);
                    break;
                default:
                    await ConnectAsync(device, addr);
                    break;
            }
        }

        private async Task ConnectAsync(BluetoothDeviceViewModel device, ulong addr)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _connectCts[device.Address] = cts;

            device.IsBusy = true;
            device.Status = DeviceStatus.Connecting;
            device.ErrorMessage = string.Empty;

            try
            {
                bool ok = await BluetoothService.ConnectDeviceAsync(addr, cts.Token);

                if (cts.Token.IsCancellationRequested)
                {
                    device.Status = DeviceStatus.Disconnected;
                }
                else if (ok)
                {
                    device.Status = DeviceStatus.Connected;
                }
                else
                {
                    // Timed out without fConnected == true — definitive failure
                    device.Status = DeviceStatus.Error;
                    device.ErrorMessage = "Failed to connect";
                }
            }
            catch (OperationCanceledException)
            {
                device.Status = DeviceStatus.Disconnected;
            }
            catch (Exception ex)
            {
                device.Status = DeviceStatus.Error;
                device.ErrorMessage = ex.Message.Length > 45 ? ex.Message[..45] + "…" : ex.Message;
            }
            finally
            {
                device.IsBusy = false;
                _connectCts.Remove(device.Address);
            }
        }

        private async Task DisconnectAsync(BluetoothDeviceViewModel device, ulong addr)
        {
            device.IsBusy = true;
            try
            {
                await BluetoothService.DisconnectDeviceAsync(addr);
                device.Status = DeviceStatus.Disconnected;
            }
            catch (Exception ex)
            {
                device.Status = DeviceStatus.Error;
                device.ErrorMessage = ex.Message.Length > 45 ? ex.Message[..45] + "…" : ex.Message;
            }
            finally
            {
                device.IsBusy = false;
            }
        }

        private void CancelConnect(BluetoothDeviceViewModel device)
        {
            if (_connectCts.TryGetValue(device.Address, out var cts))
                cts.Cancel();
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
            => await RefreshStatusesAsync(fullScan: true);

        // Allow dragging by the title bar
        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
