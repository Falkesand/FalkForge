namespace FalkForge.Engine.Variables;

using System.Globalization;
using System.Runtime.InteropServices;
using FalkForge.Engine.Pipeline;
using FalkForge.Platform;

public static class BuiltInVariables
{
    /// <remarks>
    /// <paramref name="elevationCompanionAvailable"/> feeds the <c>Privileged</c> built-in
    /// (see <see cref="PopulateSessionInfo"/>). <c>FalkForge.Engine</c> runs <c>asInvoker</c>
    /// (see <c>app.manifest</c>) and performs per-machine work through a separate elevated
    /// companion process reached two phases after Detect (<c>ElevateStep</c>, at
    /// <c>EnginePhase.Elevating</c>) — so <c>platform.Environment.IsElevated</c> alone answers the
    /// wrong question for this architecture: on the normal double-click flow the engine process
    /// itself is never elevated even when the install can perfectly well perform a per-machine
    /// install via the companion, so a package gated on <c>Condition.IsPrivileged</c> would be
    /// silently skipped every time. <c>Privileged</c> now means "can THIS INSTALL perform
    /// privileged work" — the process token is already elevated (covers a caller that runs the
    /// engine elevated directly with no companion), OR an elevation companion is configured and
    /// available. The companion's availability is known at Detect time (resolved from
    /// <c>EngineSessionOptions.ElevationCompanionPath</c>/<c>ElevationCompanionPolicy</c> before
    /// <c>EngineSession.BindToPipe</c> builds the pipeline), so this is answerable up front rather
    /// than guessed.
    /// </remarks>
    public static void Populate(
        VariableStore store,
        IPlatformServices? platform,
        ISystemClock? clock = null,
        bool elevationCompanionAvailable = false)
    {
        PopulateOsVersion(store);
        PopulateArchitecture(store);
        PopulateFolders(store, platform);
        PopulateSessionInfo(store, platform, elevationCompanionAvailable);
        PopulateUserInfo(store, platform);
        PopulateMsiInfo(store);
        PopulateDateInfo(store, clock);
        PopulateRebootPending(store, platform);
    }

    private static void PopulateOsVersion(VariableStore store)
    {
        var osVersion = System.Environment.OSVersion.Version;
        store.Set(BuiltInVariableNames.VersionNT, osVersion);
        store.Set(BuiltInVariableNames.VersionNTMajor, (long)osVersion.Major);
        store.Set(BuiltInVariableNames.VersionNTMinor, (long)osVersion.Minor);
        store.Set(BuiltInVariableNames.ServicePackLevel, 0L);
        store.Set(BuiltInVariableNames.WindowsBuildNumber, (long)osVersion.Build);
    }

    private static void PopulateArchitecture(VariableStore store)
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown"
        };

        store.Set(BuiltInVariableNames.NativeMachine, arch);
        store.Set(BuiltInVariableNames.ProcessorArchitecture, arch);

        var processArch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown"
        };
        store.Set(BuiltInVariableNames.ProcessArchitecture, processArch);

        store.Set(BuiltInVariableNames.Is64BitOperatingSystem, RuntimeInformation.OSArchitecture is Architecture.X64 or Architecture.Arm64 ? 1L : 0L);
    }

    private static void PopulateFolders(VariableStore store, IPlatformServices? platform)
    {
        if (platform is null)
        {
            PopulateFoldersFallback(store);
            return;
        }

        var env = platform.Environment;

        store.Set(BuiltInVariableNames.SystemFolder, env.GetFolderPath(System.Environment.SpecialFolder.System));
        store.Set(BuiltInVariableNames.WindowsFolder, env.GetFolderPath(System.Environment.SpecialFolder.Windows));
        store.Set(BuiltInVariableNames.ProgramFilesFolder, env.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles));
        store.Set(BuiltInVariableNames.CommonFilesFolder, env.GetFolderPath(System.Environment.SpecialFolder.CommonProgramFiles));
        store.Set(BuiltInVariableNames.TempFolder, Path.GetTempPath());
        store.Set(BuiltInVariableNames.DesktopFolder, env.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory));
        store.Set(BuiltInVariableNames.AdminToolsFolder, env.GetFolderPath(System.Environment.SpecialFolder.AdminTools));
        store.Set(BuiltInVariableNames.LocalAppDataFolder, env.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData));
        store.Set(BuiltInVariableNames.AppDataFolder, env.GetFolderPath(System.Environment.SpecialFolder.ApplicationData));
        store.Set(BuiltInVariableNames.StartMenuFolder, env.GetFolderPath(System.Environment.SpecialFolder.StartMenu));
        store.Set(BuiltInVariableNames.StartupFolder, env.GetFolderPath(System.Environment.SpecialFolder.Startup));
        store.Set(BuiltInVariableNames.PersonalFolder, env.GetFolderPath(System.Environment.SpecialFolder.Personal));
        store.Set(BuiltInVariableNames.FontsFolder, env.GetFolderPath(System.Environment.SpecialFolder.Fonts));

        // ProgramFiles64Folder: same as ProgramFilesFolder on 64-bit OS
        if (env.Is64BitOperatingSystem)
        {
            store.Set(BuiltInVariableNames.ProgramFiles64Folder, env.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles));
        }
        else
        {
            store.Set(BuiltInVariableNames.ProgramFiles64Folder, string.Empty);
        }
    }

    private static void PopulateFoldersFallback(VariableStore store)
    {
        store.Set(BuiltInVariableNames.SystemFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.System));
        store.Set(BuiltInVariableNames.WindowsFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows));
        store.Set(BuiltInVariableNames.ProgramFilesFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles));
        store.Set(BuiltInVariableNames.CommonFilesFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonProgramFiles));
        store.Set(BuiltInVariableNames.TempFolder, Path.GetTempPath());
        store.Set(BuiltInVariableNames.DesktopFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory));
        store.Set(BuiltInVariableNames.AdminToolsFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.AdminTools));
        store.Set(BuiltInVariableNames.LocalAppDataFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData));
        store.Set(BuiltInVariableNames.AppDataFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData));
        store.Set(BuiltInVariableNames.StartMenuFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.StartMenu));
        store.Set(BuiltInVariableNames.StartupFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.Startup));
        store.Set(BuiltInVariableNames.PersonalFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal));
        store.Set(BuiltInVariableNames.FontsFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts));
        store.Set(BuiltInVariableNames.ProgramFiles64Folder, System.Environment.Is64BitOperatingSystem
            ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles)
            : string.Empty);
    }

    private static void PopulateSessionInfo(
        VariableStore store, IPlatformServices? platform, bool elevationCompanionAvailable)
    {
        // Privileged means "can THIS INSTALL perform privileged (per-machine) work", not "is the
        // current process token elevated" — those are different questions for an asInvoker engine
        // that elevates through a separate companion process (see the <remarks> on Populate).
        // A prior probe read HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion (a key every logged-on
        // user, admin or not, can read — always "elevated"), but that probe was DEAD CODE, not a
        // live bug: Populate() itself was never called from production until the commit that
        // wired DetectStep to call it, so no released build ever reached that probe. The probe
        // after it was IEnvironment.IsElevated alone (the process token), which is honest about what IT
        // reports but is the wrong SIGNAL here: on the normal double-click flow the engine process
        // is never itself elevated even when the install can do per-machine work via the
        // companion. IEnvironment.IsElevated is still one of the two real inputs — a caller that
        // runs the engine already elevated with no companion (e.g. BindToChannel test/headless
        // hosts) must still see Privileged=1.
        var processElevated = platform?.Environment.IsElevated == true;
        store.Set(BuiltInVariableNames.Privileged,
            processElevated || elevationCompanionAvailable ? 1L : 0L);

        // Terminal Server / Remote Desktop detection via registry
        var isTerminalServer = false;
        var isRemoteSession = false;
        if (platform is not null)
        {
            var tsMode = platform.Registry.GetDWordValue(RegistryRoot.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Terminal Server",
                "TSAppCompat");
            isTerminalServer = tsMode is 1;

            var sessionName = platform.Environment.GetEnvironmentVariable("SESSIONNAME");
            isRemoteSession = sessionName is not null &&
                              !sessionName.StartsWith("Console", StringComparison.OrdinalIgnoreCase);
        }
        store.Set(BuiltInVariableNames.TerminalServer, isTerminalServer ? 1L : 0L);
        store.Set(BuiltInVariableNames.RemoteSession, isRemoteSession ? 1L : 0L);
    }

    private static void PopulateUserInfo(VariableStore store, IPlatformServices? platform)
    {
        if (platform is not null)
        {
            store.Set(BuiltInVariableNames.ComputerName, platform.Environment.MachineName);
        }
        else
        {
            store.Set(BuiltInVariableNames.ComputerName, System.Environment.MachineName);
        }

        var userName = System.Environment.UserName;
        store.Set(BuiltInVariableNames.LogonUser, userName);

        store.Set(BuiltInVariableNames.InstalledCulture, CultureInfo.CurrentCulture.Name);
        store.Set(BuiltInVariableNames.UserLanguageID, (long)CultureInfo.CurrentCulture.LCID);
        store.Set(BuiltInVariableNames.SystemLanguageID, (long)CultureInfo.InstalledUICulture.LCID);
    }

    private static void PopulateMsiInfo(VariableStore store)
    {
        // MSI version we emulate — we report 5.0 (Windows Installer 5.0)
        store.Set(BuiltInVariableNames.VersionMsi, new Version(5, 0));
    }

    private static void PopulateDateInfo(VariableStore store, ISystemClock? clock)
    {
        var now = clock?.UtcNow.UtcDateTime ?? DateTime.UtcNow;
        store.Set(BuiltInVariableNames.Date, now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        store.Set(BuiltInVariableNames.Time, now.ToString("HHmmss", CultureInfo.InvariantCulture));
    }

    private static void PopulateRebootPending(VariableStore store, IPlatformServices? platform)
    {
        // No platform means no probe was possible, not that a probe was attempted and failed —
        // PopulateSessionInfo's TerminalServer/RemoteSession probe is the only other platform-null
        // branch in this file that defaults to a state (rather than falling back to a real value) for
        // the same reason, so RebootPending follows suit here.
        var rebootPending = false;
        if (platform is not null)
        {
            // PendingFileRenameOperations is a registry VALUE under Session Manager, not a subkey of
            // its own — KeyExists on that path can never see it (bare OpenSubKey). TryValueExists
            // probes the value directly, type-agnostic (it's REG_MULTI_SZ).
            var pendingRenameResult = platform.Registry.TryValueExists(
                RegistryRoot.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager",
                "PendingFileRenameOperations");

            // TryKeyExists (not the bare KeyExists) for both key probes below: an ACL-denied key
            // must fail closed to "pending" like the value probe above, not silently read as
            // "absent" the way bare KeyExists would (it cannot report failure at all).
            var cbsResult = platform.Registry.TryKeyExists(
                RegistryRoot.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            var windowsUpdateResult = platform.Registry.TryKeyExists(
                RegistryRoot.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");

            // An unreadable probe is not evidence of safety — fail closed to "pending" rather than
            // read an inconclusive result as "absent" (mirrors the fail-closed precedent on
            // IRegistry.TryReadSubKeyNames: an unknown state must never look like the safe answer).
            // Result<T>.Value throws on a failed result, so the IsFailure short-circuit before each
            // .Value access is load-bearing — do not reorder.
            rebootPending =
                cbsResult.IsFailure || cbsResult.Value ||
                windowsUpdateResult.IsFailure || windowsUpdateResult.Value ||
                pendingRenameResult.IsFailure || pendingRenameResult.Value;
        }
        store.Set(BuiltInVariableNames.RebootPending, rebootPending ? 1L : 0L);
    }
}
