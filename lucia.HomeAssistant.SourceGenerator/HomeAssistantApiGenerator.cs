using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text;

namespace lucia.HomeAssistant.SourceGenerator;

[Generator]
public class HomeAssistantApiGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not SyntaxReceiver receiver)
            return;

        foreach (var candidateClass in receiver.CandidateClasses)
        {
            var model = context.Compilation.GetSemanticModel(candidateClass.SyntaxTree);
            var classSymbol = model.GetDeclaredSymbol(candidateClass);
            
            if (classSymbol == null)
                continue;

            var attribute = classSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "HomeAssistantApiAttribute" || 
                                   a.AttributeClass?.ToDisplayString().Contains("HomeAssistantApiAttribute") == true);

            if (attribute == null)
                continue;

            var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
            var className = classSymbol.Name;

            var source = GenerateApiClient(namespaceName, className, attribute);
            context.AddSource($"{className}.g.cs", source);
        }
    }

    private string GenerateApiClient(string namespaceName, string className, AttributeData attribute)
    {
        var configSectionName = GetAttributeValue(attribute, "ConfigSectionName", "HomeAssistant");
        var endpoints = HomeAssistantEndpoints.GetEndpoints();
        
        var source = $@"#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Models;

namespace {namespaceName}
{{
    public partial class {className}
    {{
        private readonly HttpClient _httpClient;
        private readonly HomeAssistantOptions _options;
        private readonly JsonSerializerOptions _jsonOptions;

        public {className}(HttpClient httpClient, IOptions<HomeAssistantOptions> options)
        {{
            _httpClient = httpClient;
            _options = options.Value;
            _jsonOptions = new JsonSerializerOptions
            {{
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            }};

            ConfigureHttpClient();
        }}

        private void ConfigureHttpClient()
        {{
            _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/'));
            _httpClient.DefaultRequestHeaders.Add(""Authorization"", $""Bearer {{_options.AccessToken}}"");
            _httpClient.DefaultRequestHeaders.Add(""Accept"", ""application/json"");
            _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        }}

        // Generated API Methods
{GenerateEndpointMethods(endpoints)}

        // Helper Methods
        private string BuildQueryString(Dictionary<string, object?> parameters)
        {{
            var query = new List<string>();
            foreach (var param in parameters)
            {{
                if (param.Value != null)
                {{
                    query.Add($""{{param.Key}}={{Uri.EscapeDataString(param.Value.ToString())}}"");
                }}
            }}
            return query.Count > 0 ? ""?"" + string.Join(""&"", query) : """";
        }}
    }}
}}";

        return source;
    }

    private string GenerateEndpointMethods(List<OpenApiEndpoint> endpoints)
    {
        var methods = new StringBuilder();
        
        foreach (var endpoint in endpoints)
        {
            methods.AppendLine(GenerateEndpointMethod(endpoint));
        }
        
        return methods.ToString();
    }

    private string GenerateEndpointMethod(OpenApiEndpoint endpoint)
    {
        var methodName = endpoint.OperationId;
        var pathParams = endpoint.Parameters.Where(p => p.Location == "path").ToList();
        var queryParams = endpoint.Parameters.Where(p => p.Location == "query").ToList();
        var hasRequestBody = !string.IsNullOrEmpty(endpoint.RequestBodyType) && endpoint.HttpMethod.ToUpper() == "POST";
        
        // Build parameter list
        var parameters = new List<string>();
        
        // Add path parameters
        foreach (var param in pathParams)
        {
            parameters.Add($"{GetCSharpType(param.Type)} {param.Name}");
        }
        
        // Add request body if needed
        if (hasRequestBody)
        {
            parameters.Add($"{endpoint.RequestBodyType}? request = null");
        }
        
        // Add query parameters
        foreach (var param in queryParams)
        {
            var paramType = GetCSharpType(param.Type);
            if (!param.IsRequired)
            {
                paramType += "?";
            }
            parameters.Add($"{paramType} {param.Name} = default");
        }
        
        parameters.Add("CancellationToken cancellationToken = default");
        
        var parameterList = string.Join(", ", parameters);
        
        // Build path with parameters
        var path = endpoint.Path;
        foreach (var param in pathParams)
        {
            path = path.Replace($"{{{param.Name}}}", $"{{Uri.EscapeDataString({param.Name})}}");
        }
        
        // Build query string logic
        var queryStringBuilder = new StringBuilder();
        if (queryParams.Any())
        {
            queryStringBuilder.AppendLine("            var queryParams = new Dictionary<string, object?>();");
            foreach (var param in queryParams)
            {
                queryStringBuilder.AppendLine($"            if ({param.Name} != default) queryParams[\"{param.Name}\"] = {param.Name};");
            }
            queryStringBuilder.AppendLine("            var queryString = BuildQueryString(queryParams);");
        }
        
        // Generate method body based on HTTP method
        var methodBody = endpoint.HttpMethod.ToUpper() switch
        {
            "GET" => GenerateGetMethodBody(endpoint, path, queryParams.Any()),
            "POST" => GeneratePostMethodBody(endpoint, path, hasRequestBody, queryParams.Any()),
            _ => throw new System.NotSupportedException($"HTTP method {endpoint.HttpMethod} not supported")
        };
        
        return $@"
        /// <summary>
        /// {endpoint.Description}
        /// </summary>
        public async Task<{endpoint.ResponseType}> {methodName}Async({parameterList})
        {{
{queryStringBuilder}
{methodBody}
        }}";
    }

    private string GenerateGetMethodBody(OpenApiEndpoint endpoint, string path, bool hasQueryParams)
    {
        var url = hasQueryParams ? $"$\"{path}\" + queryString" : $"$\"{path}\"";
        
        if (endpoint.ResponseType == "byte[]")
        {
            return $@"            var response = await _httpClient.GetAsync({url}, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();";
        }
        else if (endpoint.ResponseType == "string")
        {
            return $@"            var response = await _httpClient.GetAsync({url}, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();";
        }
        else if (endpoint.ResponseType.EndsWith("[]"))
        {
            var elementType = endpoint.ResponseType.TrimEnd('[', ']');
            return $@"            var result = await _httpClient.GetFromJsonAsync<{elementType}[]>({url}, _jsonOptions, cancellationToken);
            return result ?? Array.Empty<{elementType}>();";
        }
        else
        {
            // Special handling for GetState endpoint - return null on 404
            if (endpoint.OperationId == "GetState")
            {
                return $@"            try
            {{
                var result = await _httpClient.GetFromJsonAsync<{endpoint.ResponseType}>({url}, _jsonOptions, cancellationToken);
                return result ?? throw new InvalidOperationException(""Failed to deserialize response"");
            }}
            catch (HttpRequestException ex) when (ex.Message.Contains(""404""))
            {{
                return null;
            }}";
            }
            else
            {
                return $@"            var result = await _httpClient.GetFromJsonAsync<{endpoint.ResponseType}>({url}, _jsonOptions, cancellationToken);
            return result ?? throw new InvalidOperationException(""Failed to deserialize response"");";
            }
        }
    }

    private string GeneratePostMethodBody(OpenApiEndpoint endpoint, string path, bool hasRequestBody, bool hasQueryParams)
    {
        var url = hasQueryParams ? $"$\"{path}\" + queryString" : $"$\"{path}\"";
        
        var requestBodySerialization = hasRequestBody 
            ? "JsonSerializer.Serialize(request, _jsonOptions)" 
            : "\"{}\"";
        
        if (endpoint.ResponseType == "string")
        {
            return $@"            var json = {requestBodySerialization};
            var content = new StringContent(json, Encoding.UTF8, ""application/json"");
            var response = await _httpClient.PostAsync({url}, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();";
        }
        else
        {
            return $@"            var json = {requestBodySerialization};
            var content = new StringContent(json, Encoding.UTF8, ""application/json"");
            var response = await _httpClient.PostAsync({url}, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<{endpoint.ResponseType}>(_jsonOptions, cancellationToken);
            return result ?? throw new InvalidOperationException(""Failed to deserialize response"");";
        }
    }

    private string GetCSharpType(string type)
    {
        return type switch
        {
            "string" => "string",
            "bool" => "bool",
            "int" => "int",
            "long" => "long",
            "double" => "double",
            "float" => "float",
            "DateTime" => "DateTime",
            _ => "string"
        };
    }

    private string GetAttributeValue(AttributeData attribute, string propertyName, string defaultValue)
    {
        var namedArg = attribute.NamedArguments.FirstOrDefault(na => na.Key == propertyName);
        return namedArg.Value.Value?.ToString() ?? defaultValue;
    }
}