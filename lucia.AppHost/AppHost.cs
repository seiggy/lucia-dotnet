using Projects;
using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);



var lucia = builder.AddProject<lucia_dotnet>("lucia-dotnet")
    .WithUrlForEndpoint("https", url =>
        {
            url.DisplayText = "Scalar (HTTPS)";
            url.Url = "/scalar";
        });

builder.Build().Run();