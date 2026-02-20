using MAS.Views;

namespace MAS.Pages;

public sealed class LicensePage : MasPageBase<LicenseView>
{
    private bool _accepted;

    public override string Title => "End-User License Agreement";
    public override string? Subtitle => "Please read the following license agreement carefully";
    public override bool ShowPrintButton => true;
    public override bool CanGoNext => _accepted;

    public bool Accepted
    {
        get => _accepted;
        set { if (SetField(ref _accepted, value)) OnPropertyChanged(nameof(CanGoNext)); }
    }

    public string LicenseText => """
        LICENSE AGREEMENT

        © Copyright ASSA ABLOY OPENING SOLUTIONS SWEDEN AB. All rights reserved.

        This is a legal agreement between you, the end user and ASSA ABLOY OPENING SOLUTIONS SWEDEN AB. If you do not agree to the terms of this agreement, please cancel the installation.

        1. GRANT OF LICENCE
        ASSA ABLOY OPENING SOLUTIONS SWEDEN AB permits you to use copies of the software on single computers or on a single hard disk for use by you.

        2. COPYRIGHT
        The SOFTWARE is owned by ASSA ABLOY OPENING SOLUTIONS SWEDEN AB and is protected by Swedish copyright laws, international treaty provisions and other applicable laws.

        3. OTHER RESTRICTIONS
        You may not reverse engineer, decompile or disassemble the software except as permitted under mandatory laws. You may not amend or make changes to the software.
        """;
}
