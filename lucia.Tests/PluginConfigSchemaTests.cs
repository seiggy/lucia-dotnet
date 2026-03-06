using FakeItEasy;
using lucia.Agents.Abstractions;

namespace lucia.Tests;

/// <summary>
/// Tests for the plugin configuration schema infrastructure.
/// </summary>
public sealed class PluginConfigSchemaTests
{
    [Fact]
    public void DefaultInterface_ConfigSection_ReturnsNull()
    {
        ILuciaPlugin plugin = new MinimalPlugin();
        Assert.Null(plugin.ConfigSection);
    }

    [Fact]
    public void DefaultInterface_ConfigProperties_ReturnsEmpty()
    {
        ILuciaPlugin plugin = new MinimalPlugin();
        Assert.Empty(plugin.ConfigProperties);
    }

    [Fact]
    public void DefaultInterface_ConfigDescription_ReturnsNull()
    {
        ILuciaPlugin plugin = new MinimalPlugin();
        Assert.Null(plugin.ConfigDescription);
    }

    [Fact]
    public void PluginWithConfig_ExposesSchemaCorrectly()
    {
        var plugin = new ConfigurablePlugin();

        Assert.Equal("TestSection", plugin.ConfigSection);
        Assert.Equal("Test plugin config", plugin.ConfigDescription);
        Assert.Single(plugin.ConfigProperties);
        Assert.Equal("ApiKey", plugin.ConfigProperties[0].Name);
        Assert.Equal("string", plugin.ConfigProperties[0].Type);
        Assert.True(plugin.ConfigProperties[0].IsSensitive);
    }

    [Fact]
    public void PluginConfigProperty_Equality_WorksByValue()
    {
        var a = new PluginConfigProperty("Name", "string", "desc", "default");
        var b = new PluginConfigProperty("Name", "string", "desc", "default");
        Assert.Equal(a, b);
    }

    [Fact]
    public void PluginConfigProperty_IsSensitive_DefaultsFalse()
    {
        var prop = new PluginConfigProperty("Url", "string", "Base URL", "http://localhost");
        Assert.False(prop.IsSensitive);
    }

    [Fact]
    public void MultiplePlugins_OnlyConfigurableOnesReturnSchemas()
    {
        ILuciaPlugin[] plugins = [new MinimalPlugin(), new ConfigurablePlugin(), new AnotherMinimalPlugin()];

        var withConfig = plugins
            .Where(p => p.ConfigSection is not null && p.ConfigProperties.Count > 0)
            .ToList();

        Assert.Single(withConfig);
        Assert.Equal("configurable", withConfig[0].PluginId);
    }

    /// <summary>
    /// A plugin with no configuration (uses all defaults).
    /// </summary>
    private sealed class MinimalPlugin : ILuciaPlugin
    {
        public string PluginId => "minimal";
    }

    /// <summary>
    /// Another plugin with no configuration.
    /// </summary>
    private sealed class AnotherMinimalPlugin : ILuciaPlugin
    {
        public string PluginId => "another-minimal";
    }

    /// <summary>
    /// A plugin that declares a configuration schema.
    /// </summary>
    private sealed class ConfigurablePlugin : ILuciaPlugin
    {
        public string PluginId => "configurable";
        public string? ConfigSection => "TestSection";
        public string? ConfigDescription => "Test plugin config";
        public IReadOnlyList<PluginConfigProperty> ConfigProperties =>
        [
            new("ApiKey", "string", "API key for the test service", "", IsSensitive: true),
        ];
    }
}
