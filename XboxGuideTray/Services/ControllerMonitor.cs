using XboxGuideTray.Models;

namespace XboxGuideTray.Services;

/// <summary>
/// Forwards <see cref="BluetoothService"/> connection state changes and periodically refreshes status.
/// </summary>
public sealed class ControllerMonitor : IDisposable
{
    private readonly BluetoothService _bluetoothService;
    private readonly System.Windows.Forms.Timer _timer;
    private ControllerConnectionState _lastState = ControllerConnectionState.Unknown;

    public event EventHandler<ControllerConnectionState>? StateChanged;

    public ControllerConnectionState CurrentState => _bluetoothService.CurrentState;

    public ControllerMonitor(BluetoothService bluetoothService)
    {
        _bluetoothService = bluetoothService;
        _bluetoothService.StateChanged += OnBluetoothStateChanged;

        _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        _timer.Tick += async (_, _) => await _bluetoothService.RefreshStateAsync().ConfigureAwait(false);
    }

    public void Start()
    {
        _timer.Start();
        _ = _bluetoothService.RefreshStateAsync();
    }

    private void OnBluetoothStateChanged(object? sender, ControllerConnectionState state)
    {
        if (_lastState == state)
        {
            return;
        }

        _lastState = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _bluetoothService.StateChanged -= OnBluetoothStateChanged;
    }
}
