using System.Reflection;
using lucia.AgentHost.Apis;
using lucia.AgentHost.Models;
using lucia.Agents.Configuration;
using lucia.Data.Sqlite;
using lucia.Tests.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;

namespace lucia.Tests.Apis;

/// <summary>
/// The setup wizard's model telemetry choice, and the Configuration page entry that
/// lets users change it after setup.
/// </summary>
public sealed class ModelTelemetrySetupTests : IDisposable
{
    private readonly SqliteTestHelper _db = new();
    private readonly SqliteConfigStoreWriter _store;
    private readonly IConfigurationRoot _configuration;

    public ModelTelemetrySetupTests()
    {
        _store = new SqliteConfigStoreWriter(_db.ConnectionFactory);
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ModelTelemetry:IncludeModelContent"] = "true" })
            .AddSqliteConfiguration(_db.ConnectionFactory, TimeSpan.FromHours(1))
            .Build();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WizardChoice_IsStoredAndVisibleToOptionsImmediately(bool includeContent)
    {
        var result = await SetupApi.ConfigureModelTelemetryAsync(
            new SetupApi.ConfigureModelTelemetryRequest(includeContent),
            _store,
            _configuration,
            new DefaultHttpContext());

        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(includeContent ? "true" : "false", await _store.GetAsync("ModelTelemetry:IncludeModelContent"));
        var options = _configuration.GetSection(ModelTelemetryOptions.SectionName).Get<ModelTelemetryOptions>()!;
        Assert.Equal(includeContent, options.IncludeModelContent);
    }

    [Fact]
    public void ConfigurationPage_ListsModelTelemetryWithTheCodeDefault()
    {
        var method = typeof(ConfigurationApi).GetMethod("GetAllSchemas", BindingFlags.NonPublic | BindingFlags.Static)!;
        var schemas = (List<ConfigSectionSchema>)method.Invoke(null, null)!;

        var section = Assert.Single(schemas, schema => schema.Section == ModelTelemetryOptions.SectionName);
        var property = Assert.Single(section.Properties, p => p.Name == nameof(ModelTelemetryOptions.IncludeModelContent));
        Assert.Equal("boolean", property.Type);
        Assert.Equal(new ModelTelemetryOptions().IncludeModelContent ? "true" : "false", property.DefaultValue);
        Assert.True(new ModelTelemetryOptions().IncludeModelContent);
    }

    public void Dispose()
    {
        (_configuration as IDisposable)?.Dispose();
        _db.Dispose();
    }
}
