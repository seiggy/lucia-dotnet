namespace lucia.Agents.Orchestration;

internal static class AgentFailureMessageFormatter
{
    public static string FormatTimeout(TimeSpan timeout)
        => $"the request timed out after {FormatDuration(timeout)} before the agent finished.";

    public static string FormatCanceled()
        => "the request was interrupted before the agent finished.";

    private static string FormatDuration(TimeSpan timeout)
    {
        if (timeout.TotalSeconds >= 1 && Math.Abs(timeout.TotalSeconds - Math.Round(timeout.TotalSeconds)) < 0.001)
        {
            var seconds = (int)Math.Round(timeout.TotalSeconds);
            return seconds == 1 ? "1 second" : $"{seconds} seconds";
        }

        return $"{timeout.TotalMilliseconds:F0}ms";
    }
}
