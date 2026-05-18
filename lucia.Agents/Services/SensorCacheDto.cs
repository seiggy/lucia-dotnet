namespace lucia.Agents.Services;

internal sealed record SensorCacheDto(
    string EntityId,
    string FriendlyName,
    string? Area,
    string? DeviceClass,
    string? UnitOfMeasurement,
    string? StateClass);
