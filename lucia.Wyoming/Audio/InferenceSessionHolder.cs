using Microsoft.ML.OnnxRuntime;

namespace lucia.Wyoming.Audio;

internal sealed class InferenceSessionHolder
{
    private readonly object _sync = new();
    private readonly Action<InferenceSession> _disposeSession;
    private InferenceSession? _session;
    private int _leaseCount;
    private bool _retired;

    internal InferenceSessionHolder(
        InferenceSession session,
        Action<InferenceSession>? disposeSession = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _disposeSession = disposeSession ?? (static value => value.Dispose());
    }

    internal InferenceSession Acquire()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_retired, this);
            _leaseCount++;
            return _session!;
        }
    }

    internal void Release()
    {
        InferenceSession? session = null;
        lock (_sync)
        {
            if (_leaseCount <= 0)
            {
                throw new InvalidOperationException("Inference session lease was already released.");
            }

            _leaseCount--;
            if (_retired && _leaseCount == 0)
            {
                session = _session;
                _session = null;
            }
        }

        if (session is not null)
        {
            _disposeSession(session);
        }
    }

    internal void Retire()
    {
        InferenceSession? session = null;
        lock (_sync)
        {
            if (_retired)
            {
                return;
            }

            _retired = true;
            if (_leaseCount == 0)
            {
                session = _session;
                _session = null;
            }
        }

        if (session is not null)
        {
            _disposeSession(session);
        }
    }
}
