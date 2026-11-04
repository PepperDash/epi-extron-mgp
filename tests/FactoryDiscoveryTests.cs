using FluentAssertions;
using Xunit;

namespace PepperDash.Essentials.Plugins.Extron.Mgp.Tests;

public class FactoryDiscoveryTests
{
    [Fact]
    public void Assembly_Loads_And_Is_Named_As_Expected()
    {
        AssemblyFixture.PluginAssembly.GetTypes().Should().NotBeEmpty();
        AssemblyFixture.PluginAssembly.GetName().Name.Should().Be("epi-extron-mgp.4Series");
    }

    [Fact]
    public void Real_And_Mock_Factories_Exist()
    {
        var factories = AssemblyFixture.FindFactoryTypes();
        factories.Select(t => t.Name).Should().BeEquivalentTo("ExtronMgpFactory", "ExtronMgpMockFactory");
    }

    [Fact]
    public void Factories_Have_Parameterless_Constructors()
    {
        foreach (var factory in AssemblyFixture.FindFactoryTypes())
            factory.GetConstructor(Type.EmptyTypes).Should().NotBeNull(factory.Name);
    }

    [Theory]
    [InlineData("ExtronMgpFactory", "\"extronMgp641\"")]
    [InlineData("ExtronMgpMockFactory", "\"extronMgp641Mock\"")]
    public void Factory_Declares_Its_Type_Name(string factory, string typeName)
    {
        AssemblyFixture.FindSourceForClass(factory).Should().Contain(typeName);
    }

    [Fact]
    public void Type_Names_Are_Unique_Across_Factories()
    {
        var names = new[] { "ExtronMgpFactory", "ExtronMgpMockFactory" }
            .SelectMany(f => System.Text.RegularExpressions.Regex
                .Matches(AssemblyFixture.FindSourceForClass(f)!, "TypeNames = new List<string>\\(\\) \\{([^}]*)\\}")
                .Select(m => m.Groups[1].Value)
                .SelectMany(v => v.Split(',').Select(s => s.Trim().Trim('"').ToLowerInvariant())))
            .ToList();
        names.Should().OnlyHaveUniqueItems();
        names.Should().HaveCount(4);
    }

    [Fact]
    public void Minimum_Framework_Version_Is_Numeric()
    {
        foreach (var f in new[] { "ExtronMgpFactory", "ExtronMgpMockFactory" })
            AssemblyFixture.FindSourceForClass(f).Should().MatchRegex("MinimumEssentialsFrameworkVersion = \"\\d+\\.\\d+\\.\\d+\"");
    }

    [Theory]
    [InlineData("IExtronMgp")]
    [InlineData("IRoutingInputs")]
    public void Real_And_Mock_Both_Implement(string interfaceName)
    {
        foreach (var name in new[] { "ExtronMgpDevice", "ExtronMgpMock" })
        {
            var type = AssemblyFixture.PluginAssembly.GetTypes().Single(t => t.Name == name);
            type.GetInterfaces().Select(i => i.Name).Should().Contain(interfaceName);
        }
    }

    [Fact]
    public void Input_Ports_Are_Hdmi_Windows_And_Background()
    {
        var source = AssemblyFixture.FindSourceForClass("ExtronMgpInputPorts")!;
        source.Should().Contain("\"hdmiIn\" + window");
        source.Should().Contain("\"hdmiInBackground\"");
    }

    [Fact]
    public void No_Input_Selection_Remains_On_The_Control_Surface()
    {
        foreach (var name in new[] { "IExtronMgp", "ExtronMgpDevice", "ExtronMgpMock", "ExtronMgpMessenger" })
        {
            var type = AssemblyFixture.PluginAssembly.GetTypes().Single(t => t.Name == name);
            type.GetMethods().Select(m => m.Name).Should().NotContain(n => n.Contains("WindowInput"), name);
        }
    }
}
