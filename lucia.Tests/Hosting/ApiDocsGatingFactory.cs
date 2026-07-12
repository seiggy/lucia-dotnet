using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace lucia.Tests.Hosting;

/// <summary>
/// Boots an actual host assembly (identified by <typeparamref name="TEntryPoint"/>)
/// in-process under a specified environment, overriding only the configuration
/// required to run offline: lightweight InMemory cache + SQLite store, an empty
/// plugin directory, and placeholder Home Assistant settings. No Redis, MongoDB,
/// or real HTTP dependencies are contacted. Used to prove that API documentation
/// routes (<c>/openapi/*</c> and <c>/scalar/*</c>) are development-only.
/// </summary>
internal sealed class ApiDocsGatingFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    private readonly string _environment;
    private readonly string _dataRoot;

    public ApiDocsGatingFactory(string environment)
    {
        _environment = environment;
        _dataRoot = Path.Combine(AppContext.BaseDirectory, "apidocs-gating", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dataRoot, "plugins"));
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // The overrides go into host configuration because the actual top-level
        // Program reads these keys inline while constructing the builder (choosing
        // the data provider, plugin directory, and registry URL). Host configuration
        // is applied early enough to be visible at that point, whereas app
        // configuration added by the test host is not.
        builder.UseEnvironment(_environment);

        // These tests assert only that the API-documentation routes are gated to
        // Development; they are not a check of the hosts' dependency-injection
        // graphs. The Development environment turns on build-time container
        // validation, which would otherwise abort the boot on pre-existing,
        // unrelated service-registration gaps in a host rather than on anything
        // touched by this change. Disable it so the boot mirrors the provider
        // behavior the hosts use in Production and the route assertions can run.
        builder.UseDefaultServiceProvider((_, options) =>
        {
            options.ValidateOnBuild = false;
            options.ValidateScopes = false;
        });

        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [HostDefaults.EnvironmentKey] = _environment,
                ["DataProvider:Cache"] = "InMemory",
                ["DataProvider:Store"] = "SQLite",
                ["DataProvider:SqlitePath"] = Path.Combine(_dataRoot, "lucia.db"),
                ["PluginDirectory"] = Path.Combine(_dataRoot, "plugins"),
                // Bind the Wyoming voice TCP listener to an ephemeral port so that
                // multiple in-process host boots never collide on the fixed default.
                ["Wyoming:Port"] = "0",
                // Placeholder Home Assistant settings so hosts that block startup
                // until HA is configured proceed immediately without contacting it.
                ["HomeAssistant:BaseUrl"] = "http://127.0.0.1:9",
                ["HomeAssistant:AccessToken"] = "integration-test-token",
                // Point registry lookups at a closed port so any stray attempt
                // fails fast rather than resolving a real DNS name.
                ["services:registryApi"] = "http://127.0.0.1:9",
            });
        });

        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        try
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup; the SQLite file may still be locked briefly.
            Debug.WriteLine($"ApiDocsGatingFactory temp cleanup skipped: {ex.Message}");
        }
    }
}
