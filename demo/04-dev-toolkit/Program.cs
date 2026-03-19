using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

return Installer.Build(args, p =>
{
    // Package metadata
    p.Name = "Falk Developer Toolkit";
    p.Manufacturer = "Falk Technologies";
    p.Version = new Version(5, 0, 0);
    p.UpgradeCode = new Guid("D4E8F1A2-7B3C-4D9E-8A5F-1C6E2B9D4F7A");
    p.Description = "Complete developer toolkit with editor, compiler, and debugger";
    p.LicenseFile = "payload/license.rtf";

    // Mondo UI -- Welcome, License, InstallDir, Features, Progress, Exit
    p.UseDialogSet(MsiDialogSet.Mondo);

    // Install directory
    p.DefaultInstallDirectory = KnownFolder.ProgramFiles / "Falk Technologies" / "DevToolkit";

    // =====================================================================
    // Feature: Core Tools (required)
    // =====================================================================
    p.Feature("CoreTools", f =>
    {
        f.Title = "Core Tools";
        f.Description = "CLI launcher and core runtime (required)";
        f.IsRequired = true;
        f.IsDefault = true;
    });

    p.Files(f => f
        .Add("payload/core/falk.exe")
        .Add("payload/core/falk.core.dll")
        .Add("payload/core/falk.config.json")
        .To(KnownFolder.ProgramFiles / "Falk Technologies" / "DevToolkit"));

    // FALK_HOME environment variable
    p.EnvironmentVariable("FALK_HOME", "[INSTALLFOLDER]", ev =>
    {
        ev.IsSystem = true;
        ev.Action = EnvironmentVariableAction.Set;
    });

    // PATH append
    p.EnvironmentVariable("PATH", "[INSTALLFOLDER]bin", ev =>
    {
        ev.IsSystem = true;
        ev.Action = EnvironmentVariableAction.Append;
        ev.Separator = ";";
    });

    // =====================================================================
    // Feature: Editor (default, with nested Editor Plugins)
    // =====================================================================
    p.Feature("Editor", f =>
    {
        f.Title = "Editor";
        f.Description = "Source code editor with syntax highlighting and themes";
        f.IsDefault = true;
        f.IsRequired = false;

        // Nested feature: Editor Plugins
        f.Feature("EditorPlugins", child =>
        {
            child.Title = "Editor Plugins";
            child.Description = "Linter, formatter, and Git integration plugins";
            child.IsDefault = true;
            child.IsRequired = false;
        });
    });

    // Editor files
    p.Files(f => f
        .Add("payload/editor/editor.exe")
        .Add("payload/editor/editor.dll")
        .Add("payload/editor/editor.themes.dll")
        .To(KnownFolder.ProgramFiles / "Falk Technologies" / "DevToolkit" / "Editor"));

    // Editor syntax files
    p.Files(f => f
        .Add("payload/editor/syntax/csharp.xml")
        .Add("payload/editor/syntax/python.xml")
        .Add("payload/editor/syntax/javascript.xml")
        .To(KnownFolder.ProgramFiles / "Falk Technologies" / "DevToolkit" / "Editor" / "syntax"));

    // Editor plugins
    p.Files(f => f
        .Add("payload/editor/plugins/plugin.linter.dll")
        .Add("payload/editor/plugins/plugin.formatter.dll")
        .Add("payload/editor/plugins/plugin.git.dll")
        .To(KnownFolder.ProgramFiles / "Falk Technologies" / "DevToolkit" / "Editor" / "plugins"));

    // File associations
    p.FileAssociation(".falk", fa =>
    {
        fa.ProgId("FalkTech.FalkProject");
        fa.Description = "Falk Project File";
        fa.Verb("open", "\"%1\"");
    });

    p.FileAssociation(".fks", fa =>
    {
        fa.ProgId("FalkTech.FalkScript");
        fa.Description = "Falk Script";
        fa.Verb("open", "\"%1\"");
    });

    // Editor shortcuts
    p.Shortcut("Falk Editor", "editor.exe")
        .WithDescription("Falk Developer Toolkit Editor")
        .OnDesktop()
        .OnStartMenu("Falk Developer Toolkit");

    // =====================================================================
    // Feature: Compiler (default)
    // =====================================================================
    p.Feature("Compiler", f =>
    {
        f.Title = "Compiler";
        f.Description = "Falk language compiler and standard library";
        f.IsDefault = true;
        f.IsRequired = false;
    });

    p.Files(f => f
        .Add("payload/compiler/compiler.exe")
        .Add("payload/compiler/compiler.core.dll")
        .Add("payload/compiler/compiler.codegen.dll")
        .Add("payload/compiler/stdlib.lib")
        .To(KnownFolder.ProgramFiles / "Falk Technologies" / "DevToolkit" / "Compiler"));

    p.Registry(r => r
        .Key(RegistryRoot.LocalMachine, @"Software\FalkTech\Toolkit", k => k
            .Value("CompilerPath", @"[INSTALLFOLDER]Compiler\compiler.exe")));

    // =====================================================================
    // Feature: Debugger (not default -- user opt-in)
    // =====================================================================
    p.Feature("Debugger", f =>
    {
        f.Title = "Debugger";
        f.Description = "Interactive debugger with symbol support";
        f.IsDefault = false;
        f.IsRequired = false;
    });

    p.Files(f => f
        .Add("payload/debugger/debugger.exe")
        .Add("payload/debugger/debugger.core.dll")
        .Add("payload/debugger/debugger.symbols.dll")
        .To(KnownFolder.ProgramFiles / "Falk Technologies" / "DevToolkit" / "Debugger"));

    p.Registry(r => r
        .Key(RegistryRoot.LocalMachine, @"Software\FalkTech\Toolkit", k => k
            .Value("DebuggerPath", @"[INSTALLFOLDER]Debugger\debugger.exe")));

    // =====================================================================
    // Feature: Documentation (default)
    // =====================================================================
    p.Feature("Documentation", f =>
    {
        f.Title = "Documentation";
        f.Description = "Getting started guide, language reference, and API docs";
        f.IsDefault = true;
        f.IsRequired = false;
    });

    p.Files(f => f
        .Add("payload/docs/getting-started.html")
        .Add("payload/docs/language-reference.html")
        .Add("payload/docs/api-docs.html")
        .Add("payload/docs/changelog.txt")
        .To(KnownFolder.ProgramFiles / "Falk Technologies" / "DevToolkit" / "Docs"));

    // Major upgrade support
    p.MajorUpgrade(_ => { });
    p.Downgrade(d => d.Block("A newer version of Falk Developer Toolkit is already installed."));

    // Custom action: SetProperty FALK_VERSION = "5.0.0" after InstallValidate
    p.CustomAction("SetFalkVersion", ca =>
    {
        ca.SetProperty("FALK_VERSION", "5.0.0");
        ca.After = "InstallValidate";
    });
}, new MsiCompiler());
