namespace lucia.AgentHost.Family;

public sealed record FamilyOperationResult<T>(FamilyOperationStatus Status, T? Value, string? Error)
{
    public static FamilyOperationResult<T> Ok(T value) => new(FamilyOperationStatus.Ok, value, null);

    public static FamilyOperationResult<T> NotFound(string error = "Family member not found.") =>
        new(FamilyOperationStatus.NotFound, default, error);

    public static FamilyOperationResult<T> Invalid(string error) => new(FamilyOperationStatus.Invalid, default, error);

    public static FamilyOperationResult<T> Conflict(string error) => new(FamilyOperationStatus.Conflict, default, error);

    public IResult ToHttpResult(Func<T, IResult>? onOk = null) => Status switch
    {
        FamilyOperationStatus.Ok => onOk?.Invoke(Value!) ?? Results.Ok(Value),
        FamilyOperationStatus.NotFound => Results.NotFound(new { error = Error }),
        FamilyOperationStatus.Conflict => Results.Conflict(new { error = Error }),
        _ => Results.BadRequest(new { error = Error }),
    };
}
