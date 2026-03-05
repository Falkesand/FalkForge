namespace FalkForge.Engine.Elevation.Commands;

/// <summary>
/// Parses MSI progress messages from MsiSetExternalUIW callback
/// and converts them into 0-100 percent values.
/// Duplicated from FalkForge.Engine.Execution because the Elevation
/// project is a standalone AOT executable that does not reference Engine.
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
        if (fields.Count == 0 || !fields.TryGetValue(1, out var field1))
            return -1;

        return field1 switch
        {
            0 => HandleMasterReset(fields),
            2 => HandleProgressTick(fields),
            _ => -1
        };
    }

    private int HandleMasterReset(Dictionary<int, int> fields)
    {
        if (!fields.TryGetValue(2, out var total) || total <= 0)
            return -1;

        _total = total;
        _completed = 0;
        _forward = !fields.TryGetValue(3, out var direction) || direction == 0;

        return 0;
    }

    private int HandleProgressTick(Dictionary<int, int> fields)
    {
        if (_total <= 0)
            return -1;

        if (fields.TryGetValue(2, out var increment))
        {
            if (_forward)
                _completed += increment;
            else
                _completed -= increment;
        }

        var percent = (int)((long)Math.Abs(_completed) * 100 / _total);
        return Math.Clamp(percent, 0, 100);
    }

    private static Dictionary<int, int> ParseFields(string message)
    {
        var result = new Dictionary<int, int>();
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
                result[fieldNum] = value;

            span = spaceIndex >= 0 ? span[(spaceIndex + 1)..] : [];
        }

        return result;
    }
}
