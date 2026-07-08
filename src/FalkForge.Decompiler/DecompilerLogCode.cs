namespace FalkForge.Decompiler;

/// <summary>
/// Extracts the leading diagnostic code (e.g. <c>"DEC001"</c>, <c>"BDC003"</c>,
/// <c>"WBD004"</c>, <c>"WMM001"</c>) from an <see cref="Error"/> message for use as the
/// structured <c>"code"</c> log property, mirroring the Phase 1 <c>MsiAuthoring</c>
/// convention of attaching a discrete code to every logged failure. Decompiler error
/// messages embed their code as a <c>"CODE: message"</c> prefix rather than exposing it
/// as a separate field, so this helper recovers it without re-deriving it at every call
/// site. Falls back to <see cref="Error.Kind"/> when no such prefix is present (e.g. a
/// failure surfaced from a dependency that has no discrete code of its own).
/// </summary>
internal static class DecompilerLogCode
{
    public static string From(Error error)
    {
        var message = error.Message.AsSpan();

        var letterCount = 0;
        while (letterCount < message.Length && letterCount < 4 && char.IsAsciiLetterUpper(message[letterCount]))
            letterCount++;

        if (letterCount is >= 2 and <= 4)
        {
            var digitsEnd = letterCount + 3;
            if (digitsEnd <= message.Length &&
                char.IsAsciiDigit(message[letterCount]) &&
                char.IsAsciiDigit(message[letterCount + 1]) &&
                char.IsAsciiDigit(message[letterCount + 2]) &&
                (digitsEnd == message.Length || message[digitsEnd] == ':'))
            {
                return error.Message[..digitsEnd];
            }
        }

        return error.Kind.ToString();
    }
}
