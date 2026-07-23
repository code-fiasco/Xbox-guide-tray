using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace XboxBtDisconnect;

internal static class PnpDisconnect
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const int CrSuccess = 0;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;
    private const uint DnStarted = 0x00000008;
    private const uint CmProbDisabled = 22;
    private const int DifPropertyChange = 0x00000012;
    private const int DicsEnable = 0x00000001;
    private const int DicsDisable = 0x00000002;
    private const int DicsFlagGlobal = 0x00000001;
    private const int DicsFlagConfigSpecific = 0x00000002;
    private const uint SpdrpConfigflags = 0x0000000A;
    private const uint ConfigFlagDisabled = 0x00000001;
    private const uint CmDisableAbsolute = 0x00000001;
    private const uint CmDisableUiNotOk = 0x00000002;

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        string enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiOpenDeviceInfo(
        IntPtr deviceInfoSet,
        string deviceInstanceId,
        IntPtr hwndParent,
        uint openFlags,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        StringBuilder deviceInstanceId,
        int bufferSize,
        out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiSetClassInstallParams(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        IntPtr classInstallParams,
        int classInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiCallClassInstaller(
        int installFunction,
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[] propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Disable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Enable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public int CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;

        public static SpDevinfoData Create() =>
            new() { CbSize = Marshal.SizeOf<SpDevinfoData>() };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpClassInstallHeader
    {
        public int CbSize;
        public int InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpPropChangeParams
    {
        public SpClassInstallHeader ClassInstallHeader;
        public int StateChange;
        public int Scope;
        public int HwProfile;
    }

    public static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static IReadOnlyList<string> FindRelatedInstanceIds(ulong address)
    {
        string token = BluetoothApi.FormatPnpMacToken(address);
        string altToken = BluetoothApi.FormatBluetoothAddress(address).Replace(":", string.Empty);
        var matches = new List<string>();

        IntPtr deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            return matches;
        }

        try
        {
            var deviceInfo = SpDevinfoData.Create();
            for (uint index = 0; ; index++)
            {
                if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
                {
                    if (Marshal.GetLastWin32Error() == ErrorNoMoreItems)
                    {
                        break;
                    }

                    return matches;
                }

                if (!TryGetInstanceId(deviceInfoSet, ref deviceInfo, out string instanceId))
                {
                    continue;
                }

                if (InstanceIdMatchesAddress(instanceId, token, altToken))
                {
                    matches.Add(instanceId);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return matches
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool TryResetRelatedDevices(ulong address, bool verbose, out string error) =>
        TryEnableAllRelatedDevices(address, verbose, out error);

    public static bool TryDisableGattServiceDevices(
        ulong address,
        bool verbose,
        bool allowSetupApi,
        out IReadOnlyList<string> stoppedIds,
        out IReadOnlyList<string> failedIds,
        out string error) =>
        TryDisableInstanceIds(
            GetGattServiceInstanceIds(address),
            verbose,
            allowSetupApi,
            requireFunctionalStop: false,
            out stoppedIds,
            out failedIds,
            out error);

    public static bool TryDisableInputPathDevices(
        ulong address,
        bool verbose,
        bool allowSetupApi,
        out IReadOnlyList<string> stoppedIds,
        out IReadOnlyList<string> failedIds,
        out string error) =>
        TryDisableInstanceIds(
            GetHidInstanceIds(address),
            verbose,
            allowSetupApi,
            requireFunctionalStop: true,
            out stoppedIds,
            out failedIds,
            out error);

    public static bool TryDisableAllRelatedDevices(
        ulong address,
        bool verbose,
        bool allowSetupApi,
        out IReadOnlyList<string> stoppedIds,
        out IReadOnlyList<string> failedIds,
        out string error) =>
        TryDisableInstanceIds(
            FindRelatedInstanceIds(address),
            verbose,
            allowSetupApi,
            requireFunctionalStop: true,
            out stoppedIds,
            out failedIds,
            out error);

    private static bool TryDisableInstanceIds(
        IReadOnlyList<string> instanceIds,
        bool verbose,
        bool allowSetupApi,
        bool requireFunctionalStop,
        out IReadOnlyList<string> stoppedIds,
        out IReadOnlyList<string> failedIds,
        out string error)
    {
        stoppedIds = Array.Empty<string>();
        failedIds = Array.Empty<string>();
        error = null;

        if (instanceIds.Count == 0)
        {
            error = "No device nodes were found to disable.";
            return false;
        }

        var stopped = new List<string>();
        var failed = new List<string>();

        foreach (string instanceId in instanceIds.OrderBy(GetDisablePriority))
        {
            if (IsNodeStoppedForDisconnect(instanceId))
            {
                if (verbose)
                {
                    Console.WriteLine($"Already stopped: {instanceId}");
                }

                stopped.Add(instanceId);
                continue;
            }

            if (TryDisableDevice(instanceId, verbose, allowSetupApi, out string disableError))
            {
                stopped.Add(instanceId);
                continue;
            }

            failed.Add(instanceId);
            if (verbose)
            {
                Console.WriteLine($"Warning: {disableError}");
            }
        }

        stoppedIds = instanceIds.Where(IsNodeStoppedForDisconnect).Distinct().ToList();
        failedIds = instanceIds.Where(id => !IsNodeStoppedForDisconnect(id)).ToList();

        if (requireFunctionalStop && !IsFunctionallyStopped(instanceIds))
        {
            error = "Could not stop the core Xbox controller device nodes.";
            return false;
        }

        if (!requireFunctionalStop && stoppedIds.Count == 0 && failedIds.Count == instanceIds.Count)
        {
            error = "Could not stop any GATT service nodes.";
            return false;
        }

        if (failedIds.Count > 0 && verbose)
        {
            string label = requireFunctionalStop
                ? "optional node(s) could not be disabled; the controller stack is stopped"
                : "GATT service node(s) could not be disabled; continuing with input path";
            Console.WriteLine($"{failedIds.Count} {label}.");
            foreach (string instanceId in failedIds)
            {
                Console.WriteLine($"  still active: {instanceId}");
            }
        }

        return true;
    }

    public static bool IsInputPathFunctionallyStopped(ulong address) =>
        IsHidInputStopped(address);

    public static bool IsHidInputStopped(ulong address)
    {
        IReadOnlyList<string> hidIds = GetHidInstanceIds(address);
        return hidIds.Count > 0 && hidIds.All(IsNodeStoppedForDisconnect);
    }

    public static bool TryResetInputPathDevices(ulong address, bool verbose, out string error)
    {
        error = null;
        IReadOnlyList<string> inputIds = GetInputPathInstanceIds(address);
        if (inputIds.Count == 0)
        {
            return true;
        }

        var failures = new List<string>();
        foreach (string instanceId in inputIds)
        {
            if (IsNodeActive(instanceId))
            {
                continue;
            }

            if (!TryEnableDevice(instanceId, verbose, out string enableError))
            {
                failures.Add(enableError);
            }
        }

        if (failures.Count > 0)
        {
            error = failures[0];
            return false;
        }

        return true;
    }

    private static int GetEnablePriority(string instanceId)
    {
        if (IsParentNode(instanceId))
        {
            return 10;
        }

        if (instanceId.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase) &&
            !instanceId.Contains("{00001812", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        if (instanceId.Contains("{00001812", StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }

        if (instanceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase))
        {
            return 40;
        }

        return 25;
    }

    public static bool IsHidInputActive(ulong address)
    {
        IReadOnlyList<string> hidIds = GetHidInstanceIds(address);
        return hidIds.Count > 0 && hidIds.All(IsNodeActive);
    }

    public static bool TryEnableAllRelatedDevices(ulong address, bool verbose, out string error)
    {
        error = null;
        IReadOnlyList<string> instanceIds = FindRelatedInstanceIds(address);
        if (instanceIds.Count == 0)
        {
            error = "No related device nodes were found.";
            return false;
        }

        var failures = new List<string>();
        for (int pass = 1; pass <= 2; pass++)
        {
            if (verbose && pass > 1)
            {
                Console.WriteLine("Retrying nodes that are still disabled...");
            }

            foreach (string instanceId in instanceIds.OrderBy(GetEnablePriority))
            {
                if (IsNodeActive(instanceId))
                {
                    continue;
                }

                if (!TryEnableDevice(instanceId, verbose, out string enableError))
                {
                    failures.Add(enableError);
                    if (verbose)
                    {
                        Console.WriteLine($"Warning: {enableError}");
                    }
                }
            }

            if (IsHidInputActive(address))
            {
                return true;
            }
        }

        IReadOnlyList<string> inactiveNodes = instanceIds.Where(id => !IsNodeActive(id)).ToList();
        if (inactiveNodes.Count > 0 && verbose)
        {
            Console.WriteLine($"{inactiveNodes.Count} device node(s) are still disabled:");
            foreach (string instanceId in inactiveNodes)
            {
                Console.WriteLine($"  {instanceId}");
            }
        }

        error = failures.Count > 0
            ? failures[0]
            : "HID input device nodes are still disabled.";
        return false;
    }

    public static IReadOnlyList<string> GetGattServiceInstanceIds(ulong address) =>
        FindRelatedInstanceIds(address)
            .Where(id => id.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase))
            .ToList();

    public static IReadOnlyList<string> GetHidInstanceIds(ulong address) =>
        FindRelatedInstanceIds(address)
            .Where(id => id.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase))
            .ToList();

    public static IReadOnlyList<string> GetInputPathInstanceIds(ulong address) =>
        FindRelatedInstanceIds(address)
            .Where(id =>
                id.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("{00001812", StringComparison.OrdinalIgnoreCase))
            .ToList();

    public static bool HasMixedPartialState(ulong address)
    {
        IReadOnlyList<string> instanceIds = FindRelatedInstanceIds(address);
        if (instanceIds.Count == 0)
        {
            return false;
        }

        bool anyStopped = instanceIds.Any(IsNodeStoppedForDisconnect);
        bool anyStarted = instanceIds.Any(id => !IsNodeStoppedForDisconnect(id));
        return anyStopped && anyStarted;
    }

    public static bool IsInputPathStarted(ulong address) =>
        GetHidInstanceIds(address).Any(id => !IsNodeStoppedForDisconnect(id));

    public static bool AreAllNodesStopped(IEnumerable<string> instanceIds)
    {
        foreach (string instanceId in instanceIds)
        {
            if (!IsNodeStoppedForDisconnect(instanceId))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsFunctionallyStopped(IEnumerable<string> instanceIds)
    {
        var ids = instanceIds.ToList();
        if (ids.Count == 0)
        {
            return false;
        }

        if (ids.All(IsInputPathNode) && ids.All(IsNodeStoppedForDisconnect))
        {
            return true;
        }

        if (ids.All(IsGattServiceNode) && ids.All(IsNodeStoppedForDisconnect))
        {
            return true;
        }

        return HasStoppedParent(ids) ||
            (HasStoppedInputNode(ids) && CountStoppedServiceNodes(ids) >= 3);
    }

    public static bool IsNodeStarted(string instanceId)
    {
        if (!TryGetDevNodeStatus(instanceId, out uint status, out _))
        {
            return false;
        }

        return (status & DnStarted) != 0;
    }

    public static bool IsNodeDisabled(string instanceId)
    {
        if (!TryGetDevNodeStatus(instanceId, out _, out uint problem))
        {
            return false;
        }

        return problem == CmProbDisabled;
    }

    public static bool IsNodeActive(string instanceId)
    {
        if (!TryGetDevNodeStatus(instanceId, out uint status, out uint problem))
        {
            return false;
        }

        if (problem == CmProbDisabled || IsNodeDisabledInRegistry(instanceId))
        {
            return false;
        }

        return (status & DnStarted) != 0;
    }

    public static bool IsNodeStoppedForDisconnect(string instanceId)
    {
        if (!TryGetDevNodeStatus(instanceId, out uint status, out uint problem))
        {
            return true;
        }

        if (problem == CmProbDisabled)
        {
            return true;
        }

        if ((status & DnStarted) == 0)
        {
            return true;
        }

        return IsNodeDisabledInRegistry(instanceId);
    }

    private static bool IsNodeDisabledInRegistry(string instanceId) =>
        TryReadConfigFlags(instanceId, out uint configFlags) &&
        (configFlags & ConfigFlagDisabled) != 0;

    private static bool TryReadConfigFlags(string instanceId, out uint configFlags)
    {
        configFlags = 0;
        IntPtr deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            return false;
        }

        try
        {
            var deviceInfo = SpDevinfoData.Create();
            for (uint index = 0; ; index++)
            {
                if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
                {
                    return false;
                }

                if (!TryGetInstanceId(deviceInfoSet, ref deviceInfo, out string currentId))
                {
                    continue;
                }

                if (!string.Equals(currentId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var buffer = new byte[4];
                if (!SetupDiGetDeviceRegistryProperty(
                        deviceInfoSet,
                        ref deviceInfo,
                        SpdrpConfigflags,
                        out _,
                        buffer,
                        (uint)buffer.Length,
                        out _))
                {
                    return false;
                }

                configFlags = BitConverter.ToUInt32(buffer, 0);
                return true;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static bool TryGetDevNodeStatus(string instanceId, out uint status, out uint problem)
    {
        status = 0;
        problem = 0;
        if (CM_Locate_DevNode(out uint devInst, instanceId, 0) != CrSuccess)
        {
            return false;
        }

        return CM_Get_DevNode_Status(out status, out problem, devInst, 0) == CrSuccess;
    }

    public static bool TryDisableDevice(string instanceId, bool verbose, bool allowSetupApi, out string error)
    {
        error = null;
        if (IsNodeStoppedForDisconnect(instanceId))
        {
            return true;
        }

        if (verbose)
        {
            Console.WriteLine($"Disabling: {instanceId}");
        }

        string cmError = null;
        if (!IsHidNode(instanceId) && CM_Locate_DevNode(out uint devInst, instanceId, 0) == CrSuccess)
        {
            foreach (uint cmFlags in GetDisableFlagAttempts(instanceId))
            {
                int disableResult = CM_Disable_DevNode(devInst, cmFlags);
                if (disableResult == CrSuccess && WaitUntilStopped(instanceId))
                {
                    return true;
                }

                cmError = DescribeConfigManagerError(disableResult);
            }
        }
        else if (IsHidNode(instanceId))
        {
            cmError = "CM disable is not used for HID nodes on this platform";
        }
        else
        {
            cmError = $"CM_Locate_DevNode failed for {instanceId}";
        }

        if (!allowSetupApi)
        {
            error = $"CM_Disable_DevNode failed ({cmError})";
            return false;
        }

        if (TryPropertyChange(instanceId, DicsDisable, out string setupApiError))
        {
            if (WaitUntilStopped(instanceId) || IsNodeDisabledInRegistry(instanceId))
            {
                return true;
            }

            error = "SetupAPI disable completed but the device node is still active";
            return false;
        }

        error = IsHidNode(instanceId)
            ? $"SetupAPI disable failed ({setupApiError})"
            : $"CM_Disable_DevNode failed ({cmError}); SetupAPI disable failed ({setupApiError})";
        return false;
    }

    private static bool WaitUntilStopped(string instanceId, int attempts = 15, int delayMs = 200)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (IsNodeStoppedForDisconnect(instanceId))
            {
                return true;
            }

            Thread.Sleep(delayMs);
        }

        return IsNodeStoppedForDisconnect(instanceId);
    }

    public static bool TryEnableDevice(string instanceId, bool verbose, out string error)
    {
        error = null;
        if (IsNodeActive(instanceId))
        {
            return true;
        }

        if (verbose)
        {
            Console.WriteLine($"Enabling: {instanceId}");
        }

        string cmError = null;
        if (!IsHidNode(instanceId) && CM_Locate_DevNode(out uint devInst, instanceId, 0) == CrSuccess)
        {
            int enableResult = CM_Enable_DevNode(devInst, 0);
            if (enableResult == CrSuccess && WaitUntilActive(instanceId))
            {
                return true;
            }

            cmError = DescribeConfigManagerError(enableResult);
        }
        else if (IsHidNode(instanceId))
        {
            cmError = "CM enable is not used for HID nodes on this platform";
        }
        else
        {
            cmError = $"CM_Locate_DevNode failed for {instanceId}";
        }

        if (TryPropertyChange(instanceId, DicsEnable, out string setupApiError))
        {
            if (WaitUntilActive(instanceId))
            {
                return true;
            }

            error = "SetupAPI enable completed but the device node is still disabled";
            return false;
        }

        if (IsHidNode(instanceId) &&
            CM_Locate_DevNode(out devInst, instanceId, 0) == CrSuccess &&
            CM_Enable_DevNode(devInst, 0) == CrSuccess &&
            WaitUntilActive(instanceId))
        {
            return true;
        }

        error = IsHidNode(instanceId)
            ? $"SetupAPI enable failed ({setupApiError})"
            : $"CM_Enable_DevNode failed ({cmError}); SetupAPI enable failed ({setupApiError})";
        return false;
    }

    private static bool WaitUntilActive(string instanceId, int attempts = 20, int delayMs = 250)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (IsNodeActive(instanceId))
            {
                return true;
            }

            Thread.Sleep(delayMs);
        }

        return IsNodeActive(instanceId);
    }

    private static bool TryPropertyChange(string instanceId, int stateChange, out string error)
    {
        var errors = new List<string>();
        foreach (int scope in new[] { DicsFlagGlobal, DicsFlagConfigSpecific })
        {
            if (TryPropertyChangeViaEnumeration(instanceId, stateChange, scope, out string enumError))
            {
                error = null;
                return true;
            }

            errors.Add($"enum(scope={scope}): {enumError}");

            if (TryPropertyChangeViaOpenDeviceInfo(instanceId, stateChange, scope, out string openError))
            {
                error = null;
                return true;
            }

            errors.Add($"open(scope={scope}): {openError}");
        }

        error = string.Join("; ", errors);
        return false;
    }

    private static bool TryPropertyChangeViaEnumeration(
        string instanceId,
        int stateChange,
        int scope,
        out string error)
    {
        error = null;
        IntPtr deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            error = $"SetupDiGetClassDevs failed: {Marshal.GetLastWin32Error()}";
            return false;
        }

        try
        {
            var deviceInfo = SpDevinfoData.Create();
            for (uint index = 0; ; index++)
            {
                if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
                {
                    if (Marshal.GetLastWin32Error() == ErrorNoMoreItems)
                    {
                        error = "device not found in enumeration";
                        return false;
                    }

                    error = $"SetupDiEnumDeviceInfo failed: {Marshal.GetLastWin32Error()}";
                    return false;
                }

                if (!TryGetInstanceId(deviceInfoSet, ref deviceInfo, out string currentId))
                {
                    continue;
                }

                if (!string.Equals(currentId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return TryCallPropertyChangeInstaller(deviceInfoSet, ref deviceInfo, stateChange, scope, out error);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static bool TryPropertyChangeViaOpenDeviceInfo(
        string instanceId,
        int stateChange,
        int scope,
        out string error)
    {
        error = null;
        IntPtr deviceInfoSet = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            error = $"SetupDiCreateDeviceInfoList failed: {Marshal.GetLastWin32Error()}";
            return false;
        }

        try
        {
            var deviceInfo = SpDevinfoData.Create();
            if (!SetupDiOpenDeviceInfo(deviceInfoSet, instanceId, IntPtr.Zero, 0, ref deviceInfo))
            {
                error = $"SetupDiOpenDeviceInfo failed: {Marshal.GetLastWin32Error()}";
                return false;
            }

            return TryCallPropertyChangeInstaller(deviceInfoSet, ref deviceInfo, stateChange, scope, out error);
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static bool TryCallPropertyChangeInstaller(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfo,
        int stateChange,
        int scope,
        out string error)
    {
        error = null;
        var propChange = new SpPropChangeParams
        {
            ClassInstallHeader = new SpClassInstallHeader
            {
                CbSize = Marshal.SizeOf<SpClassInstallHeader>(),
                InstallFunction = DifPropertyChange,
            },
            StateChange = stateChange,
            Scope = scope,
            HwProfile = 0,
        };

        int paramsSize = Marshal.SizeOf<SpPropChangeParams>();
        IntPtr paramsPtr = Marshal.AllocHGlobal(paramsSize);
        try
        {
            Marshal.StructureToPtr(propChange, paramsPtr, false);
            if (!SetupDiSetClassInstallParams(deviceInfoSet, ref deviceInfo, paramsPtr, paramsSize))
            {
                error = $"SetupDiSetClassInstallParams failed: {Marshal.GetLastWin32Error()}";
                return false;
            }

            if (!SetupDiCallClassInstaller(DifPropertyChange, deviceInfoSet, ref deviceInfo))
            {
                error = $"SetupDiCallClassInstaller failed: {Marshal.GetLastWin32Error()}";
                return false;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(paramsPtr);
        }

        return true;
    }

    private static IEnumerable<uint> GetDisableFlagAttempts(string instanceId)
    {
        if (IsParentNode(instanceId))
        {
            yield return 0;
            yield return CmDisableUiNotOk;
            yield break;
        }

        yield return 0;
        yield return CmDisableUiNotOk;
        yield return CmDisableAbsolute | CmDisableUiNotOk;
    }

    private static bool IsHidNode(string instanceId) =>
        instanceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase);

    private static bool IsGattServiceNode(string instanceId) =>
        instanceId.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase);

    private static bool IsInputPathNode(string instanceId) =>
        instanceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) ||
        instanceId.Contains("{00001812", StringComparison.OrdinalIgnoreCase);

    private static bool HasStoppedParent(IEnumerable<string> instanceIds) =>
        instanceIds.Any(id => IsParentNode(id) && IsNodeStoppedForDisconnect(id));

    private static bool HasStoppedInputNode(IEnumerable<string> instanceIds) =>
        instanceIds.Any(id =>
            (id.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) ||
             id.Contains("{00001812", StringComparison.OrdinalIgnoreCase)) &&
            IsNodeStoppedForDisconnect(id));

    private static int CountStoppedServiceNodes(IEnumerable<string> instanceIds) =>
        instanceIds.Count(id => id.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase) && IsNodeStoppedForDisconnect(id));

    private static bool IsParentNode(string instanceId) =>
        instanceId.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase);

    private static bool IsFunctionallyStarted(IEnumerable<string> instanceIds)
    {
        var ids = instanceIds.ToList();
        IReadOnlyList<string> hidIds = ids.Where(IsHidNode).ToList();
        if (hidIds.Count > 0)
        {
            return hidIds.All(IsNodeActive);
        }

        return ids.Any(id => IsParentNode(id) && IsNodeActive(id));
    }

    private static int GetDisablePriority(string instanceId)
    {
        if (instanceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase))
        {
            return 10;
        }

        if (instanceId.Contains("{00001812", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        if (instanceId.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }

        if (IsParentNode(instanceId))
        {
            return 100;
        }

        return 50;
    }

    private static bool InstanceIdMatchesAddress(string instanceId, string token, string altToken) =>
        instanceId.Contains(token, StringComparison.OrdinalIgnoreCase) ||
        instanceId.Contains(altToken, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetInstanceId(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo, out string instanceId)
    {
        instanceId = null;
        var builder = new StringBuilder(256);
        if (SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfo, builder, builder.Capacity, out int requiredSize))
        {
            instanceId = builder.ToString();
            return true;
        }

        if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || requiredSize <= 0)
        {
            return false;
        }

        builder = new StringBuilder(requiredSize);
        if (!SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfo, builder, builder.Capacity, out _))
        {
            return false;
        }

        instanceId = builder.ToString();
        return true;
    }

    private static string DescribeConfigManagerError(int error) =>
        error switch
        {
            0 => "CR_SUCCESS",
            17 => "CR_REMOVE_VETOED",
            18 => "CR_INVALID_LOADED_DRIVER",
            19 => "CR_NO_SUCH_DEVICE_INTERFACE",
            23 => "CR_INVALID_POWER_FLAGS",
            24 => "CR_NO_SUCH_DEVINST",
            31 => "CR_ACCESS_DENIED",
            37 => "CR_DEVICE_NOT_THERE",
            _ => $"CR_UNKNOWN_{error}",
        } + $" ({error})";
}
