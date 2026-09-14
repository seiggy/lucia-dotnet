namespace lucia.InstallerHost;

internal sealed record WifiConfigurationRequest
{
    public required string Ssid { get; init; }

    public required string Passphrase { get; init; }
}
