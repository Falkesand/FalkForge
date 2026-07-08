using System.Runtime.InteropServices;

namespace FalkForge.Engine.Execution;

/// <summary>
/// Parses MSI progress messages from MsiSetExternalUIW callback
/// and converts them into 0-100 percent values.
/// </summary>
internal sealed class MsiProgressState
{
    private int _total;
    private int _completed;
    private bool _forward = true;

    private const uint ProgressMessageFlag = 0x0400;

    /// <summary>
    /// Processes an MSI UI message and returns a percent (0-100),
    /// or -1 if the message is not a progress update.
    /// </summary>
    public int ProcessMessage(uint messageType, string message)
    {
        if ((messageType & ProgressMessageFlag) == 0)
            return -1;

        var fields = ParseFields(message);
        if (fields.Field1 is not int field1)
            return -1;

        return field1 switch
        {
            0 => HandleMasterReset(fields),
            2 => HandleProgressTick(fields),
            _ => -1
        };
    }

    private int HandleMasterReset(ParsedFields fields)
    {
        if (fields.Field2 is not int total || total <= 0)
            return -1;

        _total = total;
        _completed = 0;
        _forward = fields.Field3 is not int direction || direction == 0;

        return 0;
    }

    private int HandleProgressTick(ParsedFields fields)
    {
        if (_total <= 0)
            return -1;

        if (fields.Field2 is int increment)
        {
            if (_forward)
                _completed += increment;
            else
                _completed -= increment;
        }

        var percent = (int)((long)Math.Abs(_completed) * 100 / _total);
        return Math.Clamp(percent, 0, 100);
    }

    /// <summary>
    /// Fixed-slot holder for MSI progress message fields 1-3 (message type, argument,
    /// direction). Replaces a per-call <c>Dictionary&lt;int,int&gt;</c> allocation on the
    /// hot msi.dll progress callback path — every field this parser ever consults is one
    /// of these three, so a heap dictionary bought nothing but allocation churn.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private struct ParsedFields
    {
        public int? Field1;
        public int? Field2;
        public int? Field3;
    }

    private static ParsedFields ParseFields(string message)
    {
        var result = new ParsedFields();
        var span = message.AsSpan();

        while (span.Length > 0)
        {
            span = span.TrimStart();
            if (span.Length == 0) break;

            var colonIndex = span.IndexOf(':');
            if (colonIndex < 0) break;

            if (!int.TryParse(span[..colonIndex].Trim(), out var fieldNum))
                break;

            span = span[(colonIndex + 1)..].TrimStart();

            var spaceIndex = span.IndexOf(' ');
            var valueSpan = spaceIndex >= 0 ? span[..spaceIndex] : span;

            if (int.TryParse(valueSpan.Trim(), out var value))
            {
                switch (fieldNum)
                {
                    case 1: result.Field1 = value; break;
                    case 2: result.Field2 = value; break;
                    case 3: result.Field3 = value; break;
                    // Fields beyond index 3 (e.g. MSI's reserved 4th progress field) are
                    // parsed only to keep the cursor advancing; no handler above ever
                    // consulted them even when they lived in the old dictionary.
                }
            }

            span = spaceIndex >= 0 ? span[(spaceIndex + 1)..] : [];
        }

        return result;
    }
}
