using lucia.Wyoming.Models;

namespace lucia.Tests.Wyoming;

internal sealed class TestModelChangeNotifier : IModelChangeNotifier
{
    public event Action<ActiveModelChangedEvent>? ActiveModelChanged;

    public void Raise(ActiveModelChangedEvent evt) => ActiveModelChanged?.Invoke(evt);

    public Action<ActiveModelChangedEvent> CaptureHandler()
        => ActiveModelChanged
            ?? throw new InvalidOperationException("No model-change handler is registered.");
}
