using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Interop;

namespace FalkForge.Compiler.Msi;

[SupportedOSPlatform("windows")]
public sealed class SummaryInfoWriter
{
    private readonly MsiDatabaseHandle _handle;

    internal SummaryInfoWriter(MsiDatabaseHandle handle)
    {
        _handle = handle;
    }

    public SummaryInfoWriter Title(string value) => SetStringProperty(NativeMethods.PID_TITLE, value);
    public SummaryInfoWriter Subject(string value) => SetStringProperty(NativeMethods.PID_SUBJECT, value);
    public SummaryInfoWriter Author(string value) => SetStringProperty(NativeMethods.PID_AUTHOR, value);
    public SummaryInfoWriter Keywords(string value) => SetStringProperty(NativeMethods.PID_KEYWORDS, value);
    public SummaryInfoWriter Comments(string value) => SetStringProperty(NativeMethods.PID_COMMENTS, value);
    public SummaryInfoWriter Template(string value) => SetStringProperty(NativeMethods.PID_TEMPLATE, value);
    public SummaryInfoWriter RevisionNumber(string value) => SetStringProperty(NativeMethods.PID_REVNUMBER, value);
    public SummaryInfoWriter CreatingApplication(string value) => SetStringProperty(NativeMethods.PID_APPNAME, value);

    public SummaryInfoWriter WordCount(int value) => SetIntProperty(NativeMethods.PID_WORDCOUNT, value);
    public SummaryInfoWriter PageCount(int value) => SetIntProperty(NativeMethods.PID_PAGECOUNT, value);
    public SummaryInfoWriter Security(int value) => SetIntProperty(NativeMethods.PID_SECURITY, value);
    public SummaryInfoWriter Codepage(int value) => SetIntProperty(NativeMethods.PID_CODEPAGE, value);

    private SummaryInfoWriter SetStringProperty(uint propertyId, string value)
    {
        long ft = 0;
        var result = NativeMethods.MsiSummaryInfoSetProperty(
            _handle.DangerousGetHandle(), propertyId, NativeMethods.VT_LPSTR, 0, ref ft, value);
        if (result != NativeMethods.ERROR_SUCCESS)
            throw new InvalidOperationException($"MsiSummaryInfoSetProperty failed for property {propertyId} (string). Error code: {result}");
        return this;
    }

    private SummaryInfoWriter SetIntProperty(uint propertyId, int value)
    {
        long ft = 0;
        var result = NativeMethods.MsiSummaryInfoSetProperty(
            _handle.DangerousGetHandle(), propertyId, NativeMethods.VT_I4, value, ref ft, null);
        if (result != NativeMethods.ERROR_SUCCESS)
            throw new InvalidOperationException($"MsiSummaryInfoSetProperty failed for property {propertyId} (int). Error code: {result}");
        return this;
    }
}
