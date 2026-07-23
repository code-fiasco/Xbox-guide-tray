namespace XboxGuideTray.Models;

public sealed class AppConfig
{
    public ControllerConfig Controller { get; set; } = new();

    public string ApplicationPath { get; set; } = string.Empty;

    public string ApplicationArguments { get; set; } = string.Empty;

    public bool RunAtStartup { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Controller.DeviceInstanceId) ||
        !string.IsNullOrWhiteSpace(Controller.BluetoothAddress);
}

public sealed class ControllerConfig
{
    public string DeviceInstanceId { get; set; } = string.Empty;

    public string BluetoothAddress { get; set; } = string.Empty;

    public string FriendlyName { get; set; } = string.Empty;
}

public sealed class XboxControllerInfo
{
    public required string DeviceInstanceId { get; init; }

    public string BluetoothAddress { get; init; } = string.Empty;

    public required string FriendlyName { get; init; }

    public bool IsConnected { get; init; }

    public override string ToString()
    {
        string address = string.IsNullOrWhiteSpace(BluetoothAddress) ? "No address" : BluetoothAddress;
        return $"{FriendlyName} ({address})";
    }
}

public enum ControllerConnectionState
{
    Unknown,
    Disconnected,
    /// <summary>BLE watcher active after unpair; waiting for a connectable advertisement.</summary>
    Searching,
    /// <summary>Advertisement seen; re-pair and GATT connect in progress.</summary>
    Pairing,
    Connected,
}
