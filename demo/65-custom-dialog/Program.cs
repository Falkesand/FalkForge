using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Demo 65 — Custom MSI dialog authoring.
//
// This installer does NOT use one of the stock dialog sets. Instead it authors a single
// custom "License key" screen from scratch with PackageBuilder.AddCustomDialog: a scrollable
// licence body, an "I accept" check box, a licence-key edit field, and Install / Cancel
// buttons — with a real control event (Install ends the dialog and proceeds with the install),
// a real control condition (Install is disabled until the licence is accepted), and explicit
// tab order. Marking the dialog with .Sequence(1100) makes it the install-UI entry screen.
return Installer.Build(args, package =>
{
    package.Name = "Custom Dialog Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.txt")
        .To(KnownFolder.ProgramFiles / "Demo" / "CustomDialog"));

    package.AddCustomDialog("LicenseKeyDlg", dlg => dlg
        .Title("Custom Dialog Demo — License")
        .Size(370, 270)
        .Sequence(1100)                 // Show this dialog as the first install-UI screen.
        .FirstControl("AcceptCheck")
        .CancelControl("CancelButton")
        .DefaultControl("InstallButton")

        // Header + separator.
        .Text("Title", 15, 15, 340, 15, "Please review and accept the license",
            b => b.Attributes(0x00030003)) // Visible | Enabled | Transparent | NoPrefix
        .Line("HeaderLine", 0, 40, 370)

        // Scrollable licence body.
        .ScrollableText("LicenseBody", 15, 50, 340, 120,
            "This is a demonstration end-user license agreement.\n\n" +
            "You may use this sample installer for evaluation purposes.",
            b => b.Sunken())

        // Accept check box, bound to ACCEPTEULA.
        .CheckBox("AcceptCheck", 15, 180, 340, 12, property: "ACCEPTEULA",
            text: "I &accept the terms in the License Agreement",
            configure: b => b.Next("KeyEdit"))

        // Licence-key entry, bound to LICENSEKEY.
        .Text("KeyPrompt", 15, 200, 120, 12, "License &key:")
        .Edit("KeyEdit", 135, 198, 220, 16, property: "LICENSEKEY",
            configure: b => b.Next("InstallButton"))

        // Buttons.
        .PushButton("InstallButton", 210, 240, 66, 17, "&Install", b => b
            .Next("CancelButton")
            .EndDialog("Return")                       // proceed with the installation
            .DisableWhen("ACCEPTEULA <> \"1\""))       // gated on the accept check box
        .PushButton("CancelButton", 289, 240, 66, 17, "Cancel", b => b
            .Next("AcceptCheck")
            .EndDialog("Exit")));
}, new MsiCompiler());
