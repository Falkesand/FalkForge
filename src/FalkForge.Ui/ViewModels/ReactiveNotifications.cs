using ReactiveUI;

namespace FalkForge.Ui.ViewModels;

/// <summary>
/// Turns on <see cref="System.ComponentModel.INotifyPropertyChanged"/> delivery for a view model
/// that implements <see cref="IReactiveObject"/> by hand.
/// <para>
/// ReactiveUI holds each object's property-changed subject in a <c>Lazy&lt;&gt;</c>, and
/// <c>ExtensionState.RaisePropertyChanged</c> pushes an event into it only when that lazy has
/// already been created. <c>ReactiveObject</c> creates it from the add-accessor of its own
/// <c>PropertyChanged</c> event. A view model that declares a plain field-like
/// <c>PropertyChanged</c> event has no accessor to do that, so the subject is never created and
/// every <c>RaiseAndSetIfChanged</c> notification is discarded — the property value changes and
/// nothing bound to it ever hears about it.
/// </para>
/// <para>
/// The wizard's view models cannot derive from <c>ReactiveObject</c> because they already derive
/// from <c>InstallerPageViewModel</c>, so each one calls this from its constructor instead.
/// </para>
/// </summary>
internal static class ReactiveNotifications
{
    /// <summary>
    /// Creates the ReactiveUI property-changing and property-changed subjects for
    /// <paramref name="viewModel"/> so its <c>RaiseAndSetIfChanged</c> calls actually raise events.
    /// Call once, from the constructor, before any property is set.
    /// </summary>
    internal static void Enable<T>(T viewModel) where T : IReactiveObject
    {
        viewModel.SubscribePropertyChangingEvents();
        viewModel.SubscribePropertyChangedEvents();
    }
}
