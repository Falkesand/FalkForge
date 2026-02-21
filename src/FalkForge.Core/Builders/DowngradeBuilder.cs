namespace FalkForge.Builders;

using FalkForge.Models;

public sealed class DowngradeBuilder
{
    private bool _allowDowngrades;
    private string? _errorMessage;

    public DowngradeBuilder Allow()
    {
        _allowDowngrades = true;
        return this;
    }

    public DowngradeBuilder Block(string message)
    {
        _allowDowngrades = false;
        _errorMessage = message;
        return this;
    }

    internal DowngradeModel Build() => new()
    {
        AllowDowngrades = _allowDowngrades,
        ErrorMessage = _errorMessage
    };
}
