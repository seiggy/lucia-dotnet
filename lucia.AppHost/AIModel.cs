// Copyright (c) Microsoft. All rights reserved.

namespace lucia.AppHost;

/// <summary>
/// A resource representing an AI model.
/// </summary>
[Obsolete("Use AddAzureAIFoundry with AddDeployment instead. See AppHost.cs for the new pattern.")]
public class AIModel(string name) : Resource(name), IResourceWithConnectionString
{
    internal string? Provider { get; set; }
    internal IResourceWithConnectionString? UnderlyingResource { get; set; }
    internal ReferenceExpression? ConnectionString { get; set; }

    public ReferenceExpression ConnectionStringExpression =>
        this.Build();

    public ReferenceExpression Build()
    {
        var connectionString = this.ConnectionString ?? throw new InvalidOperationException("No connection string available.");

        if (this.Provider is null)
        {
            throw new InvalidOperationException("No provider configured.");
        }

        return ReferenceExpression.Create($"{connectionString};Provider={this.Provider}");
    }
}
