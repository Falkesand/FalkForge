using FalkForge;
using FalkForge.Compiler.Msi;

// Custom actions: SetProperty, DllFromBinary, ExeFromBinary, deferred, rollback, commit.
return Installer.Build(args, package =>
{
    package.Name = "Custom Actions Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "CustomActionDemo"));

    // Embed a DLL binary for use in DLL-based custom actions
    package.Binary("CustomActionsDll", "payload/CustomActions.dll");

    // --- Type 1: DLL-based custom action (embedded DLL with C entry point) ---
    package.CustomAction("CheckSystemRequirements", ca =>
    {
        ca.DllFromBinary("CustomActionsDll", "CheckRequirements");
    });

    // --- Type 2: EXE-based custom action (embedded EXE) ---
    package.CustomAction("RunSetupTool", ca =>
    {
        ca.ExeFromBinary("CustomActionsDll");
        ca.Target = "--setup --silent";
        ca.Deferred();
        ca.NoImpersonate();
    });

    // --- Type 51: SetProperty custom action ---
    package.CustomAction("SetInstallMode", ca =>
    {
        ca.SetProperty("INSTALL_MODE", "standard");
    });

    // --- Deferred + rollback pair ---
    package.CustomAction("ConfigureApp", ca =>
    {
        ca.SetProperty("CONFIGURE_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --configure");
        ca.Deferred();
        ca.NoImpersonate();
    });

    package.CustomAction("UndoConfigureApp", ca =>
    {
        ca.SetProperty("UNDO_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --unconfigure");
        ca.Rollback();
        ca.NoImpersonate();
    });

    // --- Commit action (runs only on successful install completion) ---
    package.CustomAction("NotifySuccess", ca =>
    {
        ca.SetProperty("NOTIFY_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --notify-complete");
        ca.Commit();
        ca.NoImpersonate();
    });

    // --- ContinueOnError (installer proceeds even if this CA fails) ---
    package.CustomAction("OptionalTelemetry", ca =>
    {
        ca.SetProperty("TELEMETRY_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --telemetry");
        ca.Deferred();
        ca.ContinueOnError();
    });

    // Schedule every custom action above — CustomActionBuilder's After/Before/Condition
    // properties are metadata only; the compiler reads scheduling exclusively from
    // ExecuteSequence(...)/UISequence(...), so each action must be placed here to run.
    package.ExecuteSequence(seq => seq
        .Action("CheckSystemRequirements")
            .After("CostFinalize")
            .Condition(Condition.IsInstalling)
        .Action("SetInstallMode")
            .After("CostFinalize")
            .Condition(Condition.IsInstalling)
        .Action("UndoConfigureApp")
            .Before("ConfigureApp")
        .Action("ConfigureApp")
            .After("InstallFiles")
        .Action("RunSetupTool")
            .After("InstallFiles")
        .Action("NotifySuccess")
            .After("ConfigureApp")
        .Action("OptionalTelemetry")
            .After("NotifySuccess"));
}, new MsiCompiler());