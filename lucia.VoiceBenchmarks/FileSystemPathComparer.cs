namespace lucia.VoiceBenchmarks;

public static class FileSystemPathComparer
{
    public static StringComparer Instance { get; } =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
