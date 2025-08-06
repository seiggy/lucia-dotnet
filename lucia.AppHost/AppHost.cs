using Projects;

var builder = DistributedApplication.CreateBuilder(args);



builder.AddProject<lucia_dotnet>("lucia-dotnet")
    .WithExternalHttpEndpoints();

builder.Build().Run();