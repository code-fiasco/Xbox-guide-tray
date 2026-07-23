using System.Runtime.InteropServices;

namespace XboxGuideTray.Services;

/// <summary>
/// Polls Xbox controller state via XInput, which works even when another app has focus.
/// </summary>
internal static class XInputGamepadReader
{
    private const int StickThreshold = 12_000;
    private const int ErrorSuccess = 0;
    private const int ErrorDeviceNotConnected = 1167;

    private const ushort DpadUp = 0x0001;
    private const ushort DpadDown = 0x0002;
    private const ushort DpadLeft = 0x0004;
    private const ushort DpadRight = 0x0008;
    private const ushort ButtonA = 0x1000;
    private const ushort ButtonB = 0x2000;

    private static readonly XInputGetStateDelegate?[] GetStateMethods = CreateGetStateMethods();

    public static bool TryRead(out GamepadState state)
    {
        state = default;

        foreach (XInputGetStateDelegate? getState in GetStateMethods)
        {
            if (getState == null)
            {
                continue;
            }

            for (int userIndex = 0; userIndex < 4; userIndex++)
            {
                int result = getState(userIndex, out XINPUT_STATE rawState);
                if (result == ErrorSuccess)
                {
                    state = Parse(rawState.Gamepad);
                    return true;
                }

                if (result != ErrorDeviceNotConnected)
                {
                    AppLogger.Warn($"XInputGetState({userIndex}) returned {result}.");
                }
            }
        }

        return false;
    }

    private static XInputGetStateDelegate?[] CreateGetStateMethods()
    {
        string[] libraries = ["xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll"];
        var methods = new List<XInputGetStateDelegate?>();

        foreach (string library in libraries)
        {
            try
            {
                if (NativeLibrary.TryLoad(library, out IntPtr handle))
                {
                    IntPtr export = NativeLibrary.GetExport(handle, "XInputGetState");
                    methods.Add(Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(export));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Could not load {library}: {ex.Message}");
            }
        }

        return methods.ToArray();
    }

    private static GamepadState Parse(XINPUT_GAMEPAD gamepad)
    {
        ushort buttons = gamepad.wButtons;

        int vertical = 0;
        if ((buttons & DpadUp) != 0 || gamepad.sThumbLY > StickThreshold)
        {
            vertical = -1;
        }
        else if ((buttons & DpadDown) != 0 || gamepad.sThumbLY < -StickThreshold)
        {
            vertical = 1;
        }

        int horizontal = 0;
        if ((buttons & DpadLeft) != 0 || gamepad.sThumbLX < -StickThreshold)
        {
            horizontal = -1;
        }
        else if ((buttons & DpadRight) != 0 || gamepad.sThumbLX > StickThreshold)
        {
            horizontal = 1;
        }

        return new GamepadState
        {
            Vertical = vertical,
            Horizontal = horizontal,
            APressed = (buttons & ButtonA) != 0,
            BPressed = (buttons & ButtonB) != 0,
            DpadUpPressed = (buttons & DpadUp) != 0,
            DpadDownPressed = (buttons & DpadDown) != 0,
            RawButtons = buttons,
            ThumbLX = gamepad.sThumbLX,
            ThumbLY = gamepad.sThumbLY,
        };
    }

    private delegate int XInputGetStateDelegate(int dwUserIndex, out XINPUT_STATE state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }
}

internal readonly struct GamepadState
{
    public int Vertical { get; init; }
    public int Horizontal { get; init; }
    public bool APressed { get; init; }
    public bool BPressed { get; init; }
    public bool DpadUpPressed { get; init; }
    public bool DpadDownPressed { get; init; }
    public ushort RawButtons { get; init; }
    public short ThumbLX { get; init; }
    public short ThumbLY { get; init; }
}
