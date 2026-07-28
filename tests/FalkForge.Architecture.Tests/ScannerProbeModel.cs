namespace FalkForge.Architecture.Tests;

/// <summary>
/// A stand-in "model" used only to prove <see cref="PropertyGetterScanner"/> works. Its three
/// properties cover the three outcomes the scanner must distinguish: read by another type, read
/// only by the model itself, and never read at all.
/// </summary>
/// <remarks>
/// Co-located with <see cref="ScannerProbeConsumer"/>: the pair is one fixture and neither half
/// means anything without the other.
/// </remarks>
internal sealed class ScannerProbeModel
{
    public string ReadByConsumer { get; init; } = string.Empty;

    public string ReadOnlyByItself { get; init; } = string.Empty;

    public string ReadOnlyByItselfViaStateMachine { get; init; } = string.Empty;

    public string NeverRead { get; init; } = string.Empty;

    /// <summary>Makes <see cref="ReadOnlyByItself"/> a self-read, which must not count.</summary>
    public int SelfReferencingLength => ReadOnlyByItself.Length;

    /// <summary>
    /// Reads <see cref="ReadOnlyByItselfViaStateMachine"/> from inside an iterator's
    /// compiler-generated state-machine type, not from <see cref="ScannerProbeModel"/> itself.
    /// The read must still count as a self-read: a scanner that only compares the immediate
    /// declaring type against the outer type name would see the nested state-machine's bare
    /// name, miss the match, and treat this as an external consumer — masking a property no
    /// real caller reads.
    /// </summary>
    public IEnumerable<int> EnumerateSelfReferencingLengthViaStateMachine()
    {
        yield return ReadOnlyByItselfViaStateMachine.Length;
    }
}

/// <summary>The only external reader of <see cref="ScannerProbeModel"/>.</summary>
internal static class ScannerProbeConsumer
{
    public static int Consume(ScannerProbeModel model) => model.ReadByConsumer.Length;
}
