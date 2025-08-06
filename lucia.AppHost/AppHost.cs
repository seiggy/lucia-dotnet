using Projects;
using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);



var lucia = builder.AddProject<lucia_dotnet>("lucia-dotnet")
    .WithUrlForEndpoint("https", url =>
        {
            url.DisplayText = "Scalar (HTTPS)";
            url.Url = "/scalar";
        });

// Scalar API ref UI isn't working, not sure why. Come back to this later.
// var scalar = builder.AddScalarApiReference(options =>
// {
//     options.WithTheme(ScalarTheme.DeepSpace);
// });
//
// scalar.WithApiReference(lucia, options =>
// {
//     options.AddDocument("v1", "Lucia API v1", routePattern: "/api/v{version:apiVersion}.json");
// });

builder.Build().Run();