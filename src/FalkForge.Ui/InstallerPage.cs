using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using FalkForge.Engine.Protocol;
using FalkForge.Plugins;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Localization;

namespace FalkForge.Ui;

public abstract class InstallerPage : INotifyPropertyChanged
{
    private readonly Dictionary<string, PasswordBox> _passwordBoxes = new();

    internal UiStringResolver? _stringResolver;

    internal InstallerPage()
    {
    }

    public abstract string Title { get; }

    public IInstallerEngine Engine { get; internal set; } = null!;
    public InstallerState SharedState { get; internal set; } = null!;
    public IPluginServices PluginServices { get; internal set; } = null!;
    public InstallState DetectedState { get; internal set; }

    public virtual bool CanGoNext => true;
    public virtual bool CanGoBack => true;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal abstract FrameworkElement CreateViewInternal();

    public virtual PageResult OnNext()
    {
        return PageResult.Next;
    }

    public virtual PageResult OnBack()
    {
        return PageResult.Previous;
    }

    public virtual Task OnNavigatedToAsync()
    {
        return Task.CompletedTask;
    }

    public virtual Task OnNavigatingFromAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Called before the engine begins detection. Return false to cancel.
    /// </summary>
    protected internal virtual Task<bool> OnDetectBeginAsync()
    {
        return Task.FromResult(true);
    }

    /// <summary>
    ///     Called after detection completes with the result.
    /// </summary>
    protected internal virtual Task OnDetectCompleteAsync(DetectResult result)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Called before the engine begins planning. Return false to cancel.
    /// </summary>
    protected internal virtual Task<bool> OnPlanBeginAsync(InstallAction action)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    ///     Called after planning completes with the result.
    /// </summary>
    protected internal virtual Task OnPlanCompleteAsync(PlanResult result)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Called before the engine begins applying changes. Return false to cancel.
    /// </summary>
    protected internal virtual Task<bool> OnApplyBeginAsync()
    {
        return Task.FromResult(true);
    }

    /// <summary>
    ///     Called after apply completes with the result.
    /// </summary>
    protected internal virtual Task OnApplyCompleteAsync(ApplyResult result)
    {
        return Task.CompletedTask;
    }

    /// <summary>Called when an update is detected and background download is starting.</summary>
    protected virtual Task OnUpdateAvailableAsync(string version, string? releaseNotes)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Called on each download progress tick.
    ///     When totalBytes is -1, size is unknown — show an indeterminate (Knight Rider) progress bar.
    /// </summary>
    protected virtual Task OnUpdateProgressAsync(int percent, long bytesReceived, long totalBytes)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Called when download is complete and update is ready to install (DownloadAndPrompt only).
    ///     Call Engine.LaunchUpdate() when the user confirms.
    /// </summary>
    protected virtual Task OnUpdateReadyAsync(string version)
    {
        return Task.CompletedTask;
    }

    internal Task DispatchUpdateAvailableAsync(string version, string? releaseNotes)
    {
        return OnUpdateAvailableAsync(version, releaseNotes);
    }

    internal Task DispatchUpdateProgressAsync(int percent, long bytesReceived, long totalBytes)
    {
        return OnUpdateProgressAsync(percent, bytesReceived, totalBytes);
    }

    internal Task DispatchUpdateReadyAsync(string version)
    {
        return OnUpdateReadyAsync(version);
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

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

    internal void RegisterPasswordBox(string key, PasswordBox box)
    {
        _passwordBoxes[key] = box;
    }

    internal void UnregisterPasswordBox(string key)
    {
        _passwordBoxes.Remove(key);
    }

    protected SensitiveBytes GetPassword(string key)
    {
        if (!_passwordBoxes.TryGetValue(key, out var box))
            return default;
        return new SensitiveBytes(Encoding.UTF8.GetBytes(box.Password));
    }

    protected string Localize(string key)
    {
        return _stringResolver?.Resolve(key) ?? key;
    }

    internal void NotifyCultureChanged()
    {
        OnPropertyChanged(string.Empty);
    }
}