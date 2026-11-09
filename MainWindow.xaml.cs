using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace QuickConnect
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm = new();
        private readonly DispatcherTimer _pollTimer;
        private readonly Dictionary<string, CancellationTokenSource> _connectCts = new();

        // For the confirm overlay
        private Action? _pendingConfirm;
        // For the type-picker overlay
        private BluetoothDeviceViewModel? _typePickTarget;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;

            // Light status-only poll (DeviceWatcher handles most updates)
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _pollTimer.Tick += async (_, _) => await RefreshStatusesAsync(fullScan: false);

            // Hook real-time Bluetooth events
            BluetoothService.DeviceConnectionChanged += OnDeviceConnectionChanged;
            BluetoothService.DeviceBatteryChanged    += OnDeviceBatteryChanged;
            BluetoothService.StartWatcher();

            Loaded += async (_, _) =>
            {
                DeviceTypeService.Load();
                await RefreshStatusesAsync(fullScan: true);
                _pollTimer.Start();
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            _pollTimer.Stop();
            BluetoothService.StopWatcher();
            BluetoothService.DeviceConnectionChanged -= OnDeviceConnectionChanged;
            BluetoothService.DeviceBatteryChanged    -= OnDeviceBatteryChanged;
            foreach (var cts in _connectCts.Values) cts.Cancel();
            base.OnClosed(e);
        }

        // ── Real-time event callbacks (arrive on thread-pool) ─────────────── //

        private void OnDeviceConnectionChanged(ulong address, bool connected)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var vm = FindDevice(address);
                if (vm == null || vm.Status == DeviceStatus.Connecting) return;
                vm.Status = connected ? DeviceStatus.Connected : DeviceStatus.Disconnected;
            });
        }

        private void OnDeviceBatteryChanged(ulong address, int level)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var vm = FindDevice(address);
                if (vm == null) return;
                vm.BatteryLevel = level;
            });
        }

        private BluetoothDeviceViewModel? FindDevice(ulong address)
        {
            string addrStr = $"{(address >> 40) & 0xFF:X2}:{(address >> 32) & 0xFF:X2}:" +
                             $"{(address >> 24) & 0xFF:X2}:{(address >> 16) & 0xFF:X2}:" +
                             $"{(address >> 8) & 0xFF:X2}:{address & 0xFF:X2}";
            return _vm.Devices.FirstOrDefault(d => d.Address == addrStr);
        }

        private static bool TryParseAddress(string addressString, out ulong addr)
            => ulong.TryParse(addressString.Replace(":", ""),
                System.Globalization.NumberStyles.HexNumber, null, out addr);

        // ── Refresh ───────────────────────────────────────────────────────── //

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

                    // Remove devices no longer paired
                    var toRemove = _vm.Devices
                        .Where(d => !devices.Any(r => r.AddressString == d.Address))
                        .ToList();
                    foreach (var d in toRemove) _vm.Devices.Remove(d);

                    foreach (var info in devices)
                    {
                        var vm = _vm.Devices.FirstOrDefault(d => d.Address == info.AddressString);
                        if (vm == null)
                        {
                            vm = new BluetoothDeviceViewModel
                            {
                                Address       = info.AddressString,
                                Name          = info.Name,
                                ClassOfDevice = info.ClassOfDevice,
                            };
                            // Apply saved user override or auto-detect
                            vm.DeviceType = DeviceTypeService.GetType(info.AddressString, info.ClassOfDevice);
                            _vm.Devices.Add(vm);
                        }
                        else
                        {
                            vm.ClassOfDevice = info.ClassOfDevice;
                        }

                        if (vm.Status != DeviceStatus.Connecting)
                            vm.Status = info.IsConnected ? DeviceStatus.Connected : DeviceStatus.Disconnected;
                    }

                    _vm.StatusMessage = devices.Count == 0 ? "No paired devices found." : string.Empty;

                    // Fire battery refresh for connected devices
                    foreach (var vm in _vm.Devices.Where(d => d.Status == DeviceStatus.Connected))
                        _ = RefreshBatteryAsync(vm);
                }
                else
                {
                    // Light poll — just update connection state
                    foreach (var vm in _vm.Devices.ToList())
                    {
                        if (vm.Status == DeviceStatus.Connecting) continue;
                        if (!TryParseAddress(vm.Address, out ulong addr)) continue;

                        bool? connected = await BluetoothService.GetConnectionStatusAsync(addr);
                        if (connected == null) continue;

                        var newStatus = connected.Value ? DeviceStatus.Connected : DeviceStatus.Disconnected;
                        if (vm.Status != newStatus)
                        {
                            vm.Status = newStatus;
                            if (newStatus == DeviceStatus.Connected)
                                _ = RefreshBatteryAsync(vm);
                        }
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

        private async Task RefreshBatteryAsync(BluetoothDeviceViewModel vm)
        {
            if (!TryParseAddress(vm.Address, out ulong addr)) return;
            try
            {
                int? level = await BluetoothService.GetBatteryLevelAsync(addr);
                vm.BatteryLevel = level;
                // Individual L/R/Case: Windows APIs don't expose these for most devices.
                // Leave null → displays "Unavailable" rather than fabricated values.
            }
            catch { }
        }

        // ── Connect / Disconnect ──────────────────────────────────────────── //

        private async void OnToggleClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.DataContext is not BluetoothDeviceViewModel vm) return;
            if (!TryParseAddress(vm.Address, out ulong addr)) return;

            switch (vm.Status)
            {
                case DeviceStatus.Connected:
                    await DoDisconnectAsync(vm, addr);
                    break;
                case DeviceStatus.Connecting:
                    CancelConnect(vm);
                    break;
                default:
                    await DoConnectAsync(vm, addr);
                    break;
            }
        }

        private async Task DoConnectAsync(BluetoothDeviceViewModel vm, ulong addr)
        {
            // 60-second timeout enforced inside BluetoothService.ConnectAsync
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(65));
            _connectCts[vm.Address] = cts;

            vm.IsBusy = true;
            vm.Status = DeviceStatus.Connecting;
            vm.ErrorMessage = string.Empty;

            try
            {
                await BluetoothService.ConnectAsync(addr, cts.Token);
                // Success — but confirm via actual status
                vm.Status = DeviceStatus.Connected;
                _ = RefreshBatteryAsync(vm);
            }
            catch (OperationCanceledException)
            {
                vm.Status = DeviceStatus.Disconnected;
            }
            catch (ConnectException ex)
            {
                vm.Status = DeviceStatus.Error;
                vm.ErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                vm.Status = DeviceStatus.Error;
                vm.ErrorMessage = ex.Message.Length > 60
                    ? ex.Message[..60] + "…"
                    : ex.Message;
            }
            finally
            {
                vm.IsBusy = false;
                _connectCts.Remove(vm.Address);
            }
        }

        private async Task DoDisconnectAsync(BluetoothDeviceViewModel vm, ulong addr)
        {
            vm.IsBusy = true;
            try
            {
                await BluetoothService.DisconnectAsync(addr);
                vm.Status = DeviceStatus.Disconnected;
                vm.BatteryLevel = null;
            }
            catch (Exception ex)
            {
                vm.Status = DeviceStatus.Error;
                vm.ErrorMessage = ex.Message.Length > 60 ? ex.Message[..60] + "…" : ex.Message;
            }
            finally { vm.IsBusy = false; }
        }

        private void CancelConnect(BluetoothDeviceViewModel vm)
        {
            if (_connectCts.TryGetValue(vm.Address, out var cts))
                cts.Cancel();
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
            => await RefreshStatusesAsync(fullScan: true);

        // ── Options ⋯ menu ───────────────────────────────────────────────── //

        private void OnOptionsClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.DataContext is not BluetoothDeviceViewModel vm) return;

            var menu = new ContextMenu();

            var changeType = new MenuItem { Header = "Change device type…" };
            changeType.Click += (_, _) => ShowTypePicker(vm);
            menu.Items.Add(changeType);

            var forget = new MenuItem { Header = $"Forget \"{vm.Name}\"…" };
            forget.Click += (_, _) => ShowForgetConfirm(vm);
            menu.Items.Add(forget);

            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        }

        // ── Forget confirm overlay ────────────────────────────────────────── //

        private void ShowForgetConfirm(BluetoothDeviceViewModel vm)
        {
            OverlayTitle.Text = $"Forget \"{vm.Name}\"?";
            OverlayBody.Text  = "This will remove the Bluetooth pairing from Windows.";
            OverlayConfirmBtn.Content = "Forget";

            _pendingConfirm = async () =>
            {
                HideOverlay();
                if (!TryParseAddress(vm.Address, out ulong addr)) return;

                string? err = await BluetoothService.ForgetDeviceAsync(addr);
                if (err != null)
                {
                    MessageBox.Show(err, "Quick Connect",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Remove from device type store + UI list
                DeviceTypeService.RemoveOverride(vm.Address);
                _vm.Devices.Remove(vm);
                if (_vm.Devices.Count == 0)
                    _vm.StatusMessage = "No paired devices found.";
            };

            OverlayGrid.Visibility = Visibility.Visible;
        }

        private void OnOverlayConfirm(object sender, RoutedEventArgs e)
            => _pendingConfirm?.Invoke();

        private void OnOverlayCancel(object sender, RoutedEventArgs e)
            => HideOverlay();

        private void HideOverlay()
        {
            OverlayGrid.Visibility = Visibility.Collapsed;
            _pendingConfirm = null;
        }

        // ── Device type picker overlay ─────────────────────────────────────── //

        private void ShowTypePicker(BluetoothDeviceViewModel vm)
        {
            _typePickTarget = vm;
            TypeList.Items.Clear();

            foreach (DeviceType t in Enum.GetValues<DeviceType>())
            {
                TypeList.Items.Add(new ListBoxItem
                {
                    Content = DeviceTypeService.GetLabel(t),
                    Tag     = t,
                    IsSelected = t == vm.DeviceType,
                });
            }

            TypePickerGrid.Visibility = Visibility.Visible;
        }

        private void OnTypeSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;
            if (e.AddedItems[0] is not ListBoxItem item) return;
            if (_typePickTarget == null) return;

            _typePickTarget.SetUserDeviceType((DeviceType)item.Tag!);
            TypePickerGrid.Visibility = Visibility.Collapsed;
            _typePickTarget = null;
        }

        private void OnTypePickerCancel(object sender, RoutedEventArgs e)
        {
            TypePickerGrid.Visibility = Visibility.Collapsed;
            _typePickTarget = null;
        }

        // ── Drag window ──────────────────────────────────────────────────── //

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
