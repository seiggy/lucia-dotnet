using lucia.Wyoming.Models;

namespace lucia.Tests.TestDoubles;

public sealed class NullModelChangeNotifier : IModelChangeNotifier
{
    public event Action<ActiveModelChangedEvent>? ActiveModelChanged
    {
        add { }
        remove { }
    }
}
