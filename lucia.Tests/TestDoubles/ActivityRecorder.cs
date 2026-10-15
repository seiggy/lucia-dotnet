using System.Collections.Concurrent;
using System.Diagnostics;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// Records finished activities from one source. Skill activity sources are static and shared across
/// parallel tests, so assertions should look an activity up by a tag value unique to the test.
/// </summary>
public sealed class ActivityRecorder : IDisposable
{
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ActivityListener _listener;

    public ActivityRecorder(string sourceName)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _activities.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public Activity? WithTag(string key, string value)
        => _activities.FirstOrDefault(a => a.GetTagItem(key)?.ToString() == value);

    public void Dispose() => _listener.Dispose();
}
