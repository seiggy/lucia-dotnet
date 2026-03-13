namespace lucia.Wyoming.Wyoming;

public sealed record DescribeEvent : WyomingEvent
{
    public override string Type => "describe";
}
