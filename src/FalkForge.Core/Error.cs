namespace FalkForge;

public readonly record struct Error(ErrorKind Kind, string Message)
{
    public override string ToString() => $"{Kind}: {Message}";
}
