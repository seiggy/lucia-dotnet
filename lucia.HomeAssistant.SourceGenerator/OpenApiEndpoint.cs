namespace lucia.HomeAssistant.SourceGenerator;

public class OpenApiEndpoint
{
    public string Path { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<OpenApiParameter> Parameters { get; set; } = new();
    public string RequestBodyType { get; set; } = "object";
    public string ResponseType { get; set; } = "object";
}

public class OpenApiParameter
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public bool IsRequired { get; set; }
    public string Location { get; set; } = "query"; // query, path, header
}