using Nefarius.Drivers.HidHide;

namespace XboxGuideTray.Services;

/// <summary>
/// Temporarily hides the configured controller from other applications via HidHide while the
/// power menu is open. This app is whitelisted so XInput and Guide (VK 0x07) still work here.
/// </summary>
public sealed class ControllerInputBlocker : IDisposable
{
    private readonly List<string> _addedInstanceIds = new();
    private bool? _previousActive;
    private int _blockDepth;
    private string? _lastFailureReason;

    public bool IsAvailable
    {
        get
        {
            TryCreateService(out HidHideControlService? service, out _);
            return service != null;
        }
    }

    public string? UnavailableReason => _lastFailureReason;

    public bool BeginBlock(ulong bluetoothAddress)
    {
        _lastFailureReason = null;

        if (bluetoothAddress == 0)
        {
            _lastFailureReason = "No controller Bluetooth address is configured.";
            AppLogger.Warn(_lastFailureReason);
            return false;
        }

        if (!TryCreateService(out HidHideControlService? hidHide, out string? unavailableReason) || hidHide == null)
        {
            _lastFailureReason = unavailableReason;
            AppLogger.Warn($"HidHide input blocking skipped: {unavailableReason}");
            return false;
        }

        if (_blockDepth++ > 0)
        {
            return true;
        }

        try
        {
            string appPath = Path.GetFullPath(Application.ExecutablePath);
            EnsureAppWhitelisted(hidHide, appPath);

            _previousActive = hidHide.IsActive;
            _addedInstanceIds.Clear();

            IReadOnlyList<string> instanceIds = ControllerHidEnumerator.GetHidHideBlockInstanceIds(bluetoothAddress);
            if (instanceIds.Count == 0)
            {
                _lastFailureReason = "No controller device paths were found for HidHide.";
                AppLogger.Warn(_lastFailureReason);
                _blockDepth = 0;
                return false;
            }

            HashSet<string> existing = new(hidHide.BlockedInstanceIds, StringComparer.OrdinalIgnoreCase);
            foreach (string instanceId in instanceIds)
            {
                if (existing.Contains(instanceId))
                {
                    continue;
                }

                hidHide.AddBlockedInstanceId(instanceId);
                _addedInstanceIds.Add(instanceId);
            }

            hidHide.IsActive = true;
            AppLogger.Info(
                $"HidHide active for power menu ({instanceIds.Count} device path(s), " +
                $"{_addedInstanceIds.Count} newly added, app whitelisted: {appPath}).");
            return true;
        }
        catch (Exception ex)
        {
            _lastFailureReason = ex.Message;
            AppLogger.Error(ex, "Failed to block controller input.");
            EndBlockInternal(hidHide);
            return false;
        }
    }

    public void EndBlock()
    {
        if (_blockDepth <= 0)
        {
            return;
        }

        if (--_blockDepth > 0)
        {
            return;
        }

        if (TryCreateService(out HidHideControlService? hidHide, out _) && hidHide != null)
        {
            EndBlockInternal(hidHide);
        }
    }

    public void Dispose()
    {
        EndBlock();
    }

    private static bool TryCreateService(out HidHideControlService? service, out string? unavailableReason)
    {
        service = null;
        unavailableReason = null;

        try
        {
            HidHideControlService candidate = new();
            if (!candidate.IsInstalled)
            {
                unavailableReason = "HidHide driver is not installed.";
                return false;
            }

            if (!candidate.IsOperational)
            {
                unavailableReason = "HidHide is installed but not operational. Restart your PC after installing it.";
                return false;
            }

            service = candidate;
            return true;
        }
        catch (Exception ex)
        {
            unavailableReason = ex.Message;
            return false;
        }
    }

    private void EndBlockInternal(HidHideControlService hidHide)
    {
        try
        {
            foreach (string instanceId in _addedInstanceIds)
            {
                hidHide.RemoveBlockedInstanceId(instanceId);
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
                    hidHide.IsActive = _previousActive.Value;
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

    private static void EnsureAppWhitelisted(HidHideControlService hidHide, string appPath)
    {
        bool alreadyListed = hidHide.ApplicationPaths
            .Any(path => string.Equals(Path.GetFullPath(path), appPath, StringComparison.OrdinalIgnoreCase));
        if (!alreadyListed)
        {
            hidHide.AddApplicationPath(appPath, throwIfInvalid: true);
        }
    }
}
