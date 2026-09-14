namespace lucia.InstallerHost;

internal sealed record InstallerConfigurationRequest
{
    public required string DeviceId { get; init; }

    public required string EraseConfirmation { get; init; }

    public required string Hostname { get; init; }

    public required string RecoveryPassword { get; init; }

    public WifiConfigurationRequest? Wifi { get; init; }
}
