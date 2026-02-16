namespace FalkForge.Ui;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.Abstractions;

public abstract class InstallerPage : INotifyPropertyChanged
{
    internal InstallerPage() { }

    public abstract string Title { get; }

    internal abstract FrameworkElement CreateViewInternal();

    public IInstallerEngine Engine { get; internal set; } = null!;
    public InstallerState SharedState { get; internal set; } = null!;
    public InstallState DetectedState { get; internal set; }

    public virtual PageResult OnNext() => PageResult.Next;
    public virtual PageResult OnBack() => PageResult.Previous;
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;
    public virtual Task OnNavigatingFromAsync() => Task.CompletedTask;
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
}
