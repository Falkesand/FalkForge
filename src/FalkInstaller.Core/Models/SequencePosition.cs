namespace FalkInstaller.Models;

public abstract record ActionPosition
{
    private ActionPosition() { }

    public sealed record AfterAction(string ReferenceAction) : ActionPosition;
    public sealed record BeforeAction(string ReferenceAction) : ActionPosition;
    public sealed record AtNumber(int SequenceNumber) : ActionPosition;
}
