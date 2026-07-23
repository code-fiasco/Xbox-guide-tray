using System.Runtime.InteropServices;

namespace XboxGuideTray.Services;

/// <summary>
/// Polls the Xbox Guide button via VK 0x07 (same approach as the original AutoHotkey script).
/// Short press fires <see cref="ShortPress"/>; hold for <see cref="LongPressThresholdMilliseconds"/> fires <see cref="LongPress"/>.
/// </summary>
public sealed class GuideButtonService : IDisposable
{
    public const int LongPressThresholdMilliseconds = 600;

    // Guide is exposed as VK 0x07 by the Windows Xbox accessory stack (not via XInput).
    private const int GuideVirtualKey = 0x07;

    // Bit 15 of GetAsyncKeyState indicates the key is currently held down.
    private const int KeyDownMask = 0x8000;

    private readonly System.Windows.Forms.Timer _pollTimer;
    private bool _guidePressed;
    private DateTime _guidePressStartUtc;
    private bool _longPressTriggered;
    private bool _suppressUntilRelease;

    public event EventHandler? ShortPress;
    public event EventHandler? LongPress;

    public GuideButtonService()
    {
        _pollTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _pollTimer.Tick += (_, _) => Poll();
        AppLogger.Info("Guide button service initialized (VK 0x07 polling).");
    }

    public void Start() => _pollTimer.Start();

    /// <summary>
    /// Ignores guide input until the button is released. Used after a long-press opens the
    /// power menu so the release does not also trigger a short press.
    /// </summary>
    public void SuppressUntilGuideReleased() => _suppressUntilRelease = true;

    public bool IsGuidePressed() => IsGuideKeyDown();

    private void Poll()
    {
        bool guideDown = IsGuideKeyDown();

        if (_suppressUntilRelease)
        {
            if (!guideDown)
            {
                _suppressUntilRelease = false;
                _guidePressed = false;
                _longPressTriggered = false;
            }

            return;
        }

        if (guideDown && !_guidePressed)
        {
            _guidePressed = true;
            _guidePressStartUtc = DateTime.UtcNow;
            _longPressTriggered = false;
        }
        else if (guideDown && _guidePressed && !_longPressTriggered)
        {
            double heldMs = (DateTime.UtcNow - _guidePressStartUtc).TotalMilliseconds;
            if (heldMs >= LongPressThresholdMilliseconds)
            {
                _longPressTriggered = true;
                _suppressUntilRelease = true;
                LongPress?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (!guideDown && _guidePressed)
        {
            _guidePressed = false;

            if (!_longPressTriggered)
            {
                ShortPress?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private static bool IsGuideKeyDown() =>
        (GetAsyncKeyState(GuideVirtualKey) & KeyDownMask) != 0;

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
