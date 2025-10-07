---
applyTo: '**/*ServiceDefaults/*, **/*AppHost/*'
---
Role Definition:
 - .NET Aspire Expert
 - Cloud-Native Architect
 - Orchestration Specialist
 - Distributed Systems Developer

## General:
### Description:
> .NET Aspire is an opinionated, cloud-ready stack for building observable,
> production-ready, distributed applications. It simplifies development of
> cloud-native applications by providing a consistent way to configure and
> connect services, manage dependencies, and observe application behavior.
> Version 9.3.1 (June 2025) brings significant enhancements to orchestration,
> dashboard usability, and Azure integrations.

### Requirements:
- Utilize .NET Aspire for new cloud-native .NET projects.
- Leverage the AppHost project for orchestration of all services and resources.
- Employ the ServiceDefaults project for common service configurations (telemetry, health checks).
- Actively use the Aspire Dashboard for development, debugging, and monitoring.
- Implement health checks for all application services.
- Use structured logging for enhanced observability.

## Key Concepts:
###  AppHost Project
Description:
> The central orchestrator for an Aspire application. It's a .NET project
> (typically `YourApp.AppHost`) that defines all the services, containers,
> executables, and cloud resources that make up the application.

Usage: 
> `Program.cs` in this project uses a `IDistributedApplicationBuilder` to compose the application.

### ServiceDefaults Project
Description:
> A shared project (typically `YourApp.ServiceDefaults`) that provides common
> configurations for services, such as logging, telemetry (OpenTelemetry),
> health checks, and resilience policies.
      
Usage: 
> Referenced by individual service projects to apply consistent defaults.
> Includes `builder.AddServiceDefaults()` extension.

### Aspire Dashboard
Description:
>  A web-based interface for viewing and interacting with the resources defined
>  in the AppHost. It displays logs, traces, metrics, environment variables,
>  and allows for console interaction with services.

Access: 
> CLI will output a URL with the access token for direct access and login

Features (v9.3+):
> Resource lifecycle management, mobile support, sensitive property masking, colorful logs, resource command execution.

### Resources
Description:
> Abstractions for the different parts of a distributed application.
> Examples include .NET projects, Docker containers, executables,
> and cloud services (e.g., databases, message queues, Azure resources).

### Service Discovery
Description:
> Aspire automatically configures service discovery, allowing services to
> find and communicate with each other using logical names, without needing
> to hardcode URIs. Connection strings and service URLs are injected as
> environment variables.

## AppHost Project (`Program.cs`) Development:
- Adding Project References:
    ```csharp
    // Good: Adding a .NET project as a service
    var builder = DistributedApplication.CreateBuilder(args);
    var myApiService = builder.AddProject<Projects.MyApiService>("myapiservice");
    var myWorkerService = builder.AddProject<Projects.MyWorkerService>("myworkerservice");
    ```

- Adding Container Resources:
    ```csharp
    // Good: Adding a Redis container with Redis Insight for diagnostics
    var cache = builder.AddRedis("cache").WithRedisInsight();

    // Good: Adding a PostgreSQL container
    var postgres = builder.AddPostgres("postgresdb")
                          .WithPgAdmin(); // Optional PgAdmin
    var db = postgres.AddDatabase("mydatabase");
    ```

- Adding Executable Resources:
    ```csharp
    // Good: Adding a standalone executable as a resource
    var myLegacyWorker = builder.AddExecutable("mylegacyworker", "path/to/worker.exe", "argument1", "argument2");
    ```

- Configuring Resources:
  - Environment Variables:
      ```csharp
      // Good: Setting environment variables for a service
      myApiService.WithEnvironment("FEATURE_FLAG_X", "true");
      myApiService.WithEnvironment(context => {
          context.EnvironmentVariables["API_KEY"] = context.Configuration["ApiKey"];
      });
      ```
  - Connection Strings:
      ```csharp
      // Good: Referencing a database resource to inject connection string
      var postgres = builder.AddPostgres("postgresdb")
                          .WithPgAdmin(); // Optional PgAdmin
      var db = postgres.AddDatabase("mydatabase");
      myApiService.WithReference(db); // Injects "ConnectionStrings__mydatabase"

      // Good: Manually setting a connection string (less common for Aspire-managed resources)
      myApiService.WithConnectionString("externaldb", "Server=...;");
      ```
  - Volume Mounts (for containers):
      ```csharp
      // Good: Mounting a volume to a container
      builder.AddContainer("mydataimporter", "mydataimporter-image")
              .WithVolumeMount("./data", "/app/data", VolumeMountType.Bind);
      ```
  - Service Bindings (Endpoints):
      ```csharp
      // Good: Defining service bindings for a project
      myApiService.WithServiceBinding(hostPort: 5001, scheme: "https", name: "https"); // Exposes on 5001 externally
      myApiService.WithServiceBinding(port: 8080, name: "http"); // Internal port
      ```

- WaitFor Dependencies (New in v9.x):
    ```csharp
    // Good: Ensuring a service waits for its dependencies to be ready
    var backendApi = builder.AddProject<Projects.BackendApi>("backendapi");
    var frontend = builder.AddProject<Projects.Frontend>("frontend")
                          .WithReference(backendApi) // Service discovery
                          .WaitFor(backendApi);    // Waits for backendApi health check
    var worker = builder.AddProject<Projects.Worker>("worker")
                        .WithReference(cache)
                        .WaitFor(cache)
                        .WaitFor(db, "DatabaseReady"); // Waits for a specific health check tag
    ```
- Health Checks:
    >  Aspire automatically integrates health checks. Services implementing health
    >  checks (e.g., via `AddServiceDefaults()`) will report their status to the
    >  dashboard. `WaitFor` relies on these health checks.

- Persistent Containers (New in v9.x):
    ```csharp
    // Good: Marking a container to persist data across runs (dev only)
    var persistentRedis = builder.AddRedis("persistentcache").AsPersistent();
    ```

- Resource Commands (New in v9.x):
    ```csharp
    // Good: Defining custom commands executable from the dashboard
    postgres.WithCommand("create-backup", "pg_dumpall -U postgres > /backups/backup.sql")
            .WithVolumeMount("./backups", "/backups");
    ```

- Eventing Model (New in v9.x):
    > Allows hooking into resource lifecycle events (e.g., BeforeStart, AfterEndpointsAllocated)
    > for custom orchestration logic.
    ```csharp
    // Good: Customizing a resource before it starts
    myApiService.WithAnnotation(new LifecycleHookAnnotation(args =>
    {
        Console.WriteLine($"{args.Resource.Name} is about to start.");
        return Task.CompletedTask;
    }, LifecycleHookTiming.BeforeStart));
    ```

## ServiceDefaults Project (`Extensions.cs`):
  - Purpose:
      > Centralizes common configurations for individual services. Typically includes
      > OpenTelemetry setup (logging, tracing, metrics), default health check endpoints,
      > and potentially resilience policies (e.g., Polly).
  - `AddServiceDefaults()`:
      ```csharp
      // In ServiceDefaults/Extensions.cs
      public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
      {
          builder.ConfigureOpenTelemetry();
          builder.AddDefaultHealthChecks();
          // Add other defaults like resilience policies
          return builder;
      }

      // In individual apps Program.cs
      builder.AddServiceDefaults();
      ```
  - Customizing Telemetry:
      ```csharp
      // In ServiceDefaults/Extensions.cs
      public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
      {
          builder.Logging.AddOpenTelemetry(logging => { /* ... */ });
          builder.Services.AddOpenTelemetry()
              .WithMetrics(metrics => { /* ... */ })
              .WithTracing(tracing => { /* ... */ });
          // Add OTLP exporters for production, e.g., Azure Monitor, Prometheus
          return builder;
      }
      ```

### Aspire Dashboard:
- Key Features:
  - Resource View: List of all orchestrated resources, their states, and endpoints.
  - Logs: Aggregated logs from all services, with filtering and search.
  - Traces: Distributed traces for understanding request flows across services.
  - Metrics: Key performance indicators from services.
  - Console Output: View stdout/stderr for services.
  - Environment Variables: Inspect configured environment for each resource.
- Enhancements in v9.3+:
  - Resource Lifecycle Management: Start/stop/restart resources directly from the dashboard.
  - Mobile Support: Responsive design for use on smaller screens.
  - Sensitive Property Masking: Hides sensitive configuration values by default.
  - Colorful Logs: Improved readability of log output.
  - Resource Commands: Execute predefined commands on resources.

### Key Integrations:
- Databases:
    ```csharp
    // PostgreSQL (with database)
    var postgres = builder.AddPostgres("pg").AddDatabase("catalogdb");
    // SQL Server
    var sqlserver = builder.AddSqlServer("sql").AddDatabase("inventorydb");
    // MongoDB
    var mongo = builder.AddMongoDB("mongo").AddDatabase("productdb");
    // Redis (as a cache)
    var redis = builder.AddRedis("cache");
    ```
- Messaging:
    ```csharp
    // RabbitMQ
    var rabbitmq = builder.AddRabbitMQ("messagebus");
    // Kafka
    var kafka = builder.AddKafka("eventstream");
    ```
- Azure Services:
    ```csharp
    // Azure Storage (Blobs)
    var storage = builder.AddAzureStorage("storage");
    var blobStorage = storage.AddBlobs("blobcontainer");
    // Azure Key Vault
    var keyVault = builder.AddAzureKeyVault("keyvault");
    // Azure Service Bus
    var serviceBus = builder.AddAzureServiceBus("sb");
    // Azure OpenAI
    var openAI = builder.AddAzureOpenAI("myopenai");
    // Azure Functions (Preview in v9.x)
    var functionsApp = builder.AddAzureFunctions("myfunctionsapp", "<path_to_functions_project_file>");
    ```
- Dapr:
    ```csharp
    // Good: Integrating with Dapr
    builder.AddDapr();
    myApiService.WithDaprSidecar();
    ```
- OpenAI (Non-Azure):
    ```csharp
    // Good: Adding a generic OpenAI client
    var openAIClient = builder.AddOpenAI("openai_generic");
    myApiService.WithReference(openAIClient);
    ```

### Deployment:
- Azure Developer CLI (`azd`):
  > The primary recommended tool for deploying Aspire applications to Azure.
  > Uses a manifest generated by Aspire.
  - Commands:
    - `azd init` (to initialize in an Aspire project)
    - `azd up` (to provision and deploy)
    - `azd provision`
    - `azd deploy`
- Manifest Generation:
  > Generates a JSON manifest describing the application's resources, which
  > can be used by deployment tools or custom scripts.
  - Command: `dotnet run --project .\MyApp.AppHost\MyApp.AppHost.csproj -- publisher manifest --output-path .\aspire-manifest.json`
  
- Containerizing Apps:
  > Aspire simplifies building containerized applications. Projects added to
  > the AppHost can be automatically configured to build as containers.
  > Ensure Dockerfiles are present or use SDK container builds.

### Best Practices:
- AppHost Focus: Keep the AppHost project strictly for orchestration. Avoid business logic.
- ServiceDefaults Purpose: Use ServiceDefaults for genuinely common, cross-cutting concerns.
- Health Checks: Implement thorough health checks in all services for reliability and `WaitFor` functionality.
- Structured Logging: Utilize structured logging (e.g., Serilog with OpenTelemetry) for better observability.
- Configuration & Secrets:
  - Development: Use .NET user secrets (`dotnet user-secrets set`) for sensitive values.
  - Production: Use managed identity and services like Azure Key Vault. Aspire integrates with these.
- Explicit Dependencies: Clearly define service dependencies using `WithReference()` for service discovery and `WaitFor()` for startup orchestration.
- Dashboard Usage: Leverage the Aspire Dashboard extensively during development for insights into logs, traces, metrics, and configurations.
- Resource Optimization: For containerized services, consider setting resource requests and limits.
- Idempotency: Design services to be idempotent, especially if they interact with messaging systems or perform critical operations.
- Telemetry: Ensure all services are configured for telemetry (logs, traces, metrics) via ServiceDefaults. Export to appropriate backends in production.
- API Versioning: For service projects that expose APIs, implement proper API versioning strategies.

### Troubleshooting:
- Check Dashboard Logs: The Aspire Dashboard is the first place to look for errors, logs, and environment variables.
- Inspect Connection Strings: Verify that connection strings injected by Aspire are correctly formatted and that services can reach their dependencies.
- Container Issues: If using containers, check Docker logs (`docker logs <container_id>`).
- Health Check Failures: Investigate why a service's health check might be failing if `WaitFor` is not working as expected.
---
