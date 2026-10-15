namespace SOG_DevOpsLastActivities.ViewModels.Features;

/// <summary>
/// Something a session can do with its configuration. Each feature reaches Azure DevOps through
/// <see cref="SessionViewModel.Client"/> and asks the session who the user is.
/// </summary>
public abstract class FeatureViewModel(SessionViewModel session) : ObservableObject, IDisposable
{
    public SessionViewModel Session { get; } = session;

    public abstract string Title { get; }
    public abstract string Description { get; }

    /// <summary>Called when the tab closes: stop whatever is still running.</summary>
    public virtual void Dispose() { }
}
