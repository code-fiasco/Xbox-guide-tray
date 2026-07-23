using Nefarius.Drivers.HidHide;

namespace XboxGuideTray.Services;

/// <summary>
/// Temporarily hides the configured controller from other applications via HidHide while the
/// power menu is open. This app is whitelisted so XInput and Guide (VK 0x07) still work here.
/// </summary>
public sealed class ControllerInputBlocker : IDisposable
{
    private readonly HidHideControlService? _hidHide;
    private readonly List<string> _addedInstanceIds = new();
    private bool? _previousActive;
    private int _blockDepth;

    public ControllerInputBlocker()
    {
        try
        {
            _hidHide = new HidHideControlService();
            if (!_hidHide.IsInstalled)
            {
                UnavailableReason = "HidHide driver is not installed.";
                return;
            }

            if (!_hidHide.IsOperational)
            {
                UnavailableReason = "HidHide driver is installed but not operational.";
                return;
            }

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = ex.Message;
            AppLogger.Warn($"HidHide is unavailable: {ex.Message}");
        }
    }

    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    public void BeginBlock(ulong bluetoothAddress)
    {
        if (!IsAvailable || _hidHide == null || bluetoothAddress == 0)
        {
            return;
        }

        if (_blockDepth++ > 0)
        {
            return;
        }

        try
        {
            EnsureAppWhitelisted(Application.ExecutablePath);

            _previousActive = _hidHide.IsActive;
            _addedInstanceIds.Clear();

            IReadOnlyList<string> instanceIds = ControllerHidEnumerator.GetInputPathInstanceIds(bluetoothAddress);
            if (instanceIds.Count == 0)
            {
                AppLogger.Warn("No controller HID paths found for input blocking.");
                _blockDepth = 0;
                return;
            }

            HashSet<string> existing = new(_hidHide.BlockedInstanceIds, StringComparer.OrdinalIgnoreCase);
            foreach (string instanceId in instanceIds)
            {
                if (existing.Contains(instanceId))
                {
                    continue;
                }

                _hidHide.AddBlockedInstanceId(instanceId);
                _addedInstanceIds.Add(instanceId);
            }

            _hidHide.IsActive = true;
            AppLogger.Info(
                $"Blocked controller input from other apps ({instanceIds.Count} HID path(s), " +
                $"{_addedInstanceIds.Count} newly added to HidHide).");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to block controller input.");
            EndBlockInternal();
        }
    }

    public void EndBlock()
    {
        if (!IsAvailable || _hidHide == null || _blockDepth <= 0)
        {
            return;
        }

        if (--_blockDepth > 0)
        {
            return;
        }

        EndBlockInternal();
    }

    public void Dispose()
    {
        EndBlock();
    }

    private void EndBlockInternal()
    {
        if (_hidHide == null)
        {
            return;
        }

        try
        {
            foreach (string instanceId in _addedInstanceIds)
            {
                _hidHide.RemoveBlockedInstanceId(instanceId);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to remove temporary HidHide entries: {ex.Message}");
        }
        finally
        {
            _addedInstanceIds.Clear();

            if (_previousActive.HasValue)
            {
                try
                {
                    _hidHide.IsActive = _previousActive.Value;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to restore HidHide active state: {ex.Message}");
                }

                _previousActive = null;
            }

            _blockDepth = 0;
        }
    }

    private void EnsureAppWhitelisted(string appPath)
    {
        if (_hidHide == null)
        {
            return;
        }

        bool alreadyListed = _hidHide.ApplicationPaths
            .Any(path => string.Equals(path, appPath, StringComparison.OrdinalIgnoreCase));
        if (!alreadyListed)
        {
            _hidHide.AddApplicationPath(appPath, throwIfInvalid: true);
        }
    }
}
