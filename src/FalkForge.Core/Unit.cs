namespace FalkForge;

public readonly record struct Unit
{
    // CA1805: static readonly fields without an initializer are already default-initialized.
    public static readonly Unit Value;
}