namespace lucia.Wyoming.WakeWord;

public interface IWakeWordStore
{
    Task<CustomWakeWord?> GetAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<CustomWakeWord>> GetAllAsync(CancellationToken ct);
    Task SaveAsync(CustomWakeWord wakeWord, CancellationToken ct);
    Task DeleteAsync(string id, CancellationToken ct);
}
