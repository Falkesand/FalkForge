namespace FalkForge.Ui;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using FalkForge.Engine.Protocol;
using FalkForge.Plugins;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Localization;

public abstract class InstallerPage : INotifyPropertyChanged
{
    internal InstallerPage() { }

    public abstract string Title { get; }

    internal abstract FrameworkElement CreateViewInternal();

    public IInstallerEngine Engine { get; internal set; } = null!;
    public InstallerState SharedState { get; internal set; } = null!;
    public IPluginServices PluginServices { get; internal set; } = null!;
    public InstallState DetectedState { get; internal set; }

    public virtual PageResult OnNext() => PageResult.Next;
    public virtual PageResult OnBack() => PageResult.Previous;
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;
    public virtual Task OnNavigatingFromAsync() => Task.CompletedTask;

    /// <summary>
    /// Called before the engine begins detection. Return false to cancel.
    /// </summary>
    protected internal virtual Task<bool> OnDetectBeginAsync() => Task.FromResult(true);

    /// <summary>
    /// Called after detection completes with the result.
    /// </summary>
    protected internal virtual Task OnDetectCompleteAsync(DetectResult result) => Task.CompletedTask;

    /// <summary>
    /// Called before the engine begins planning. Return false to cancel.
    /// </summary>
    protected internal virtual Task<bool> OnPlanBeginAsync(InstallAction action) => Task.FromResult(true);

    /// <summary>
    /// Called after planning completes with the result.
    /// </summary>
    protected internal virtual Task OnPlanCompleteAsync(PlanResult result) => Task.CompletedTask;

    /// <summary>
    /// Called before the engine begins applying changes. Return false to cancel.
    /// </summary>
    protected internal virtual Task<bool> OnApplyBeginAsync() => Task.FromResult(true);

    /// <summary>
    /// Called after apply completes with the result.
    /// </summary>
    protected internal virtual Task OnApplyCompleteAsync(ApplyResult result) => Task.CompletedTask;

    public virtual bool CanGoNext => true;
    public virtual bool CanGoBack => true;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected bool SetField<T>(ref T field, T value, string[] alsoNotify, [CallerMemberName] string? name = null)
    {
        if (!SetField(ref field, value, name))
            return false;
        foreach (var dependent in alsoNotify)
            OnPropertyChanged(dependent);
        return true;
    }

    internal UiStringResolver? _stringResolver;

    private readonly Dictionary<string, PasswordBox> _passwordBoxes = new();

    internal void RegisterPasswordBox(string key, PasswordBox box)
        => _passwordBoxes[key] = box;

    internal void UnregisterPasswordBox(string key)
        => _passwordBoxes.Remove(key);

    protected SensitiveBytes GetPassword(string key)
    {
        if (!_passwordBoxes.TryGetValue(key, out var box))
            return default;
        return new SensitiveBytes(Encoding.UTF8.GetBytes(box.Password));
    }

    protected string Localize(string key)
        => _stringResolver?.Resolve(key) ?? key;

    internal void NotifyCultureChanged()
        => OnPropertyChanged(string.Empty);
}
