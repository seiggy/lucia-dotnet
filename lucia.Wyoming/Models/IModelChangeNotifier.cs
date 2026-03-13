namespace lucia.Wyoming.Models;

/// <summary>
/// Publishes model change notifications.
/// </summary>
public interface IModelChangeNotifier
{
    event Action<ActiveModelChangedEvent>? ActiveModelChanged;
}
