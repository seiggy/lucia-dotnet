namespace lucia.HomeAssistant.SourceGenerator;

public static class HomeAssistantEndpoints
{
    public static List<OpenApiEndpoint> GetEndpoints()
    {
        return new List<OpenApiEndpoint>
        {
            new OpenApiEndpoint
            {
                Path = "/api/",
                HttpMethod = "GET",
                OperationId = "GetApiRoot",
                Description = "API root status",
                ResponseType = "object"
            },
            new OpenApiEndpoint
            {
                Path = "/api/config",
                HttpMethod = "GET",
                OperationId = "GetConfig",
                Description = "Retrieve configuration details",
                ResponseType = "object"
            },
            new OpenApiEndpoint
            {
                Path = "/api/events",
                HttpMethod = "GET",
                OperationId = "GetEvents",
                Description = "List available events",
                ResponseType = "object[]"
            },
            new OpenApiEndpoint
            {
                Path = "/api/services",
                HttpMethod = "GET",
                OperationId = "GetServices",
                Description = "List available services",
                ResponseType = "object"
            },
            new OpenApiEndpoint
            {
                Path = "/api/history/period/{timestamp}",
                HttpMethod = "GET",
                OperationId = "GetHistory",
                Description = "Get historical data",
                Parameters = new List<OpenApiParameter>
                {
                    new OpenApiParameter { Name = "timestamp", Type = "string", IsRequired = true, Location = "path" },
                    new OpenApiParameter { Name = "filter_entity_id", Type = "string", IsRequired = true, Location = "query" },
                    new OpenApiParameter { Name = "end_time", Type = "string", IsRequired = false, Location = "query" },
                    new OpenApiParameter { Name = "minimal_response", Type = "bool", IsRequired = false, Location = "query" },
                    new OpenApiParameter { Name = "no_attributes", Type = "bool", IsRequired = false, Location = "query" }
                },
                ResponseType = "object[]"
            },
            new OpenApiEndpoint
            {
                Path = "/api/logbook/{timestamp}",
                HttpMethod = "GET",
                OperationId = "GetLogbook",
                Description = "Get logbook entries",
                Parameters = new List<OpenApiParameter>
                {
                    new OpenApiParameter { Name = "timestamp", Type = "string", IsRequired = true, Location = "path" },
                    new OpenApiParameter { Name = "entity", Type = "string", IsRequired = false, Location = "query" },
                    new OpenApiParameter { Name = "end_time", Type = "string", IsRequired = false, Location = "query" }
                },
                ResponseType = "object[]"
            },
            new OpenApiEndpoint
            {
                Path = "/api/states",
                HttpMethod = "GET",
                OperationId = "GetStates",
                Description = "Retrieve all entity states",
                ResponseType = "HomeAssistantState[]"
            },
            new OpenApiEndpoint
            {
                Path = "/api/states/{entity_id}",
                HttpMethod = "GET",
                OperationId = "GetState",
                Description = "Retrieve specific entity state",
                Parameters = new List<OpenApiParameter>
                {
                    new OpenApiParameter { Name = "entity_id", Type = "string", IsRequired = true, Location = "path" }
                },
                ResponseType = "HomeAssistantState?"
            },
            new OpenApiEndpoint
            {
                Path = "/api/states/{entity_id}",
                HttpMethod = "POST",
                OperationId = "SetState",
                Description = "Update specific entity state",
                Parameters = new List<OpenApiParameter>
                {
                    new OpenApiParameter { Name = "entity_id", Type = "string", IsRequired = true, Location = "path" }
                },
                RequestBodyType = "object",
                ResponseType = "HomeAssistantState"
            },
            new OpenApiEndpoint
            {
                Path = "/api/error_log",
                HttpMethod = "GET",
                OperationId = "GetErrorLog",
                Description = "Retrieve system error logs",
                ResponseType = "string"
            },
            new OpenApiEndpoint
            {
                Path = "/api/camera_proxy/{camera_entity_id}",
                HttpMethod = "GET",
                OperationId = "GetCameraProxy",
                Description = "Retrieve camera image",
                Parameters = new List<OpenApiParameter>
                {
                    new OpenApiParameter { Name = "camera_entity_id", Type = "string", IsRequired = true, Location = "path" }
                },
                ResponseType = "byte[]"
            },
            new OpenApiEndpoint
            {
                Path = "/api/calendars",
                HttpMethod = "GET",
                OperationId = "GetCalendars",
                Description = "List calendar entities",
                ResponseType = "object[]"
            },
            new OpenApiEndpoint
            {
                Path = "/api/calendars/{calendar_entity_id}",
                HttpMethod = "GET",
                OperationId = "GetCalendar",
                Description = "Get calendar events",
                Parameters = new List<OpenApiParameter>
                {
                    new OpenApiParameter { Name = "calendar_entity_id", Type = "string", IsRequired = true, Location = "path" },
                    new OpenApiParameter { Name = "start", Type = "string", IsRequired = false, Location = "query" },
                    new OpenApiParameter { Name = "end", Type = "string", IsRequired = false, Location = "query" }
                },
                ResponseType = "object[]"
            },
            new OpenApiEndpoint
            {
                Path = "/api/events/{event_type}",
                HttpMethod = "POST",
                OperationId = "FireEvent",
                Description = "Fire a custom event",
                Parameters = new List<OpenApiParameter>
                {
                    new OpenApiParameter { Name = "event_type", Type = "string", IsRequired = true, Location = "path" }
                },
                RequestBodyType = "object",
                ResponseType = "object"
            },
            new OpenApiEndpoint
            {
                Path = "/api/services/{domain}/{service}?{parameters}",
                HttpMethod = "POST",
                OperationId = "CallService",
                Description = "Call a specific service",
                Parameters = new List<OpenApiParameter>
                {
                    new OpenApiParameter { Name = "domain", Type = "string", IsRequired = true, Location = "path" },
                    new OpenApiParameter { Name = "service", Type = "string", IsRequired = true, Location = "path" },
                    new OpenApiParameter { Name = "parameters", Type = "string", IsRequired = false, Location = "query" }
                },
                RequestBodyType = "ServiceCallRequest",
                ResponseType = "string"
            },
            new OpenApiEndpoint
            {
                Path = "/api/template",
                HttpMethod = "POST",
                OperationId = "RenderTemplate",
                Description = "Render a template",
                RequestBodyType = "TemplateRenderRequest",
                ResponseType = "string"
            },
            new OpenApiEndpoint
            {
                Path = "/api/config/core/check_config",
                HttpMethod = "POST",
                OperationId = "CheckConfig",
                Description = "Validate configuration",
                ResponseType = "object"
            },
            new OpenApiEndpoint
            {
                Path = "/api/intent/handle",
                HttpMethod = "POST",
                OperationId = "HandleIntent",
                Description = "Handle an intent",
                RequestBodyType = "object",
                ResponseType = "object"
            }
        };
    }
}