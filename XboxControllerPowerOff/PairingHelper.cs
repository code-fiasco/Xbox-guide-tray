using System;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;

namespace XboxControllerPowerOff;

internal static class PairingHelper
{
    public static async Task<DevicePairingResult> TryPairSilentlyAsync(DeviceInformationPairing pairing)
    {
        AppLogger.Info($"Pairing: CanPair={pairing.CanPair}, IsPaired={pairing.IsPaired}.");

        if (!pairing.CanPair)
        {
            AppLogger.Warn("Device reports CanPair=false; pairing will likely fail.");
        }

        DevicePairingResult lastResult = null;

        foreach ((DevicePairingKinds kinds, DevicePairingProtectionLevel protection) in GetStrategies())
        {
            AppLogger.Info($"Trying custom PairAsync (kinds={kinds}, protection={protection})...");
            DevicePairingResult result = await TryCustomPairAsync(pairing, kinds, protection);
            lastResult = result;

            if (IsSuccess(result))
            {
                return result;
            }

            AppLogger.Warn($"Custom pair failed: {result.Status} (protection={result.ProtectionLevelUsed}).");
        }

        foreach (DevicePairingProtectionLevel protection in new[]
                 {
                     DevicePairingProtectionLevel.EncryptionAndAuthentication,
                     DevicePairingProtectionLevel.Default,
                     DevicePairingProtectionLevel.None,
                 })
        {
            AppLogger.Info($"Trying default PairAsync (protection={protection})...");
            DevicePairingResult result = await pairing.PairAsync(protection);
            lastResult = result;

            if (IsSuccess(result))
            {
                return result;
            }

            AppLogger.Warn($"Default pair failed: {result.Status} (protection={result.ProtectionLevelUsed}).");
        }

        return lastResult;
    }

    private static async Task<DevicePairingResult> TryCustomPairAsync(
        DeviceInformationPairing pairing,
        DevicePairingKinds kinds,
        DevicePairingProtectionLevel protection)
    {
        DeviceInformationCustomPairing custom = pairing.Custom;
        custom.PairingRequested += OnPairingRequested;
        try
        {
            return await custom.PairAsync(kinds, protection);
        }
        finally
        {
            custom.PairingRequested -= OnPairingRequested;
        }
    }

    private static void OnPairingRequested(
        DeviceInformationCustomPairing sender,
        DevicePairingRequestedEventArgs args)
    {
        AppLogger.Info($"Pairing ceremony: {args.PairingKind}");
        switch (args.PairingKind)
        {
            case DevicePairingKinds.ConfirmOnly:
            case DevicePairingKinds.ConfirmPinMatch:
                args.Accept();
                break;
            case DevicePairingKinds.ProvidePin:
                args.Accept();
                break;
            case DevicePairingKinds.DisplayPin:
                AppLogger.Info($"Display PIN ceremony reported pin={args.Pin}; accepting.");
                args.Accept();
                break;
            default:
                AppLogger.Info($"Accepting pairing ceremony {args.PairingKind}.");
                args.Accept();
                break;
        }
    }

    private static bool IsSuccess(DevicePairingResult result) =>
        result.Status == DevicePairingResultStatus.Paired ||
        result.Status == DevicePairingResultStatus.AlreadyPaired;

    private static (DevicePairingKinds Kinds, DevicePairingProtectionLevel Protection)[] GetStrategies() =>
        new[]
        {
            (DevicePairingKinds.ConfirmOnly, DevicePairingProtectionLevel.EncryptionAndAuthentication),
            (DevicePairingKinds.ConfirmOnly, DevicePairingProtectionLevel.Default),
            (
                DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch,
                DevicePairingProtectionLevel.EncryptionAndAuthentication),
            (
                DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch,
                DevicePairingProtectionLevel.Default),
        };
}
