using lucia.Agents.Services;

namespace lucia.Tests.Services;

public class EntityMatchNameFormatterTests
{
    [Fact]
    public void ResolveName_UsesPrimaryName_WhenProvided()
    {
        var result = EntityMatchNameFormatter.ResolveName(
            "  Kitchen Lamp  ",
            ["Alias Name"],
            "light.kitchen_lamp",
            stripDomainFromId: true);

        Assert.Equal("Kitchen Lamp", result);
    }

    [Fact]
    public void ResolveName_UsesFirstAlias_WhenNameMissing()
    {
        var result = EntityMatchNameFormatter.ResolveName(
            null,
            ["", "  Desk Lamp  "],
            "light.office_lamp",
            stripDomainFromId: true);

        Assert.Equal("Desk Lamp", result);
    }

    [Fact]
    public void ResolveName_UsesFormattedEntityId_WhenNameAndAliasesMissing()
    {
        var result = EntityMatchNameFormatter.ResolveName(
            null,
            [],
            "light.diannas_lamp",
            stripDomainFromId: true);

        Assert.Equal("diannas lamp", result);
    }

    [Fact]
    public void ResolveName_UsesFormattedIdWithoutDomainStrip_WhenRequested()
    {
        var result = EntityMatchNameFormatter.ResolveName(
            null,
            [],
            "living_room",
            stripDomainFromId: false);

        Assert.Equal("living room", result);
    }

    [Fact]
    public void SanitizeAliases_RemovesEmptyValuesAndDuplicates()
    {
        var result = EntityMatchNameFormatter.SanitizeAliases(
            ["", "  ", "Desk Lamp", "desk lamp ", "Desk   Lamp", "Office"]);

        Assert.Equal(["Desk Lamp", "Office"], result);
    }
}
