using FluentAssertions;
using Xunit;

namespace PepperDash.Essentials.Plugins.Extron.Mgp.Tests;

public class ExtronMgpProtocolTests
{
    [Fact]
    public void Commands_Match_The_Manual()
    {
        ExtronMgpProtocol.EchoOff().Should().Be("\u001b0ECHO\r");
        ExtronMgpProtocol.VerboseMode3().Should().Be("\u001b3CV\r");
        ExtronMgpProtocol.RecallLayout(5).Should().Be("1*5.");
        ExtronMgpProtocol.QueryCurrentLayout().Should().Be("\u001bL1PRST\r");
        ExtronMgpProtocol.QueryLayoutName(12).Should().Be("\u001b1*12PNAM\r");
        ExtronMgpProtocol.SetWindowMute(3, true).Should().Be("3*1B");
        ExtronMgpProtocol.SetWindowMute(3, false).Should().Be("3*0B");
        ExtronMgpProtocol.QueryWindowMute(4).Should().Be("4B");
        ExtronMgpProtocol.SetWindowFreeze(1, true).Should().Be("1*1F");
        ExtronMgpProtocol.QueryWindowFreeze(1).Should().Be("1F");
        ExtronMgpProtocol.QueryIpAddress().Should().Be("\u001bCI\r");
        ExtronMgpProtocol.QueryMacAddress().Should().Be("\u001bCH\r");
        ExtronMgpProtocol.QueryUnitName().Should().Be("\u001bCN\r");
        ExtronMgpProtocol.QueryFirmwareBuild().Should().Be("*Q");
    }

    [Theory]
    [InlineData("1Rpr005", 5)]
    [InlineData("Rpr1*005", 5)]
    [InlineData("Rpr1005", 5)]
    [InlineData("Prst L 1*012", 12)]
    [InlineData("PrstL1*128", 128)]
    public void Layout_Responses_Parse(string line, int expected)
    {
        ExtronMgpProtocol.TryParseLayout(line, out var preset).Should().BeTrue();
        preset.Should().Be(expected);
    }

    [Theory]
    [InlineData("2Rpr3*010")]
    [InlineData("Rpr2*3*010")]
    public void Layout_Parser_Ignores_Other_Preset_Recalls(string line)
    {
        ExtronMgpProtocol.TryParseLayout(line, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("Pnam1*005,Quad View", 1, 5, "Quad View")]
    [InlineData("Pnam 2*010, Laptop", 2, 10, "Laptop")]
    public void Preset_Names_Parse(string line, int kind, int preset, string name)
    {
        ExtronMgpProtocol.TryParsePresetName(line, out var k, out var p, out var n).Should().BeTrue();
        (k, p, n).Should().Be((kind, preset, name));
    }

    [Theory]
    [InlineData("Vmt1*1", 1, true)]
    [InlineData("Vmt 4*0", 4, false)]
    public void Mute_Parses(string line, int window, bool muted)
    {
        ExtronMgpProtocol.TryParseMute(line, out var w, out var m).Should().BeTrue();
        (w, m).Should().Be((window, muted));
    }

    [Theory]
    [InlineData("Frz2*1", 2, true)]
    [InlineData("Frz2*0", 2, false)]
    public void Freeze_Parses(string line, int window, bool frozen)
    {
        ExtronMgpProtocol.TryParseFreeze(line, out var w, out var f).Should().BeTrue();
        (w, f).Should().Be((window, frozen));
    }

    [Fact]
    public void Banner_Is_Detected_And_Firmware_Extracted()
    {
        const string banner = "(c) Copyright 2026, Extron Electronics, MGP 641, V1.05, 60-1574-01";
        ExtronMgpProtocol.IsBanner(banner).Should().BeTrue();
        ExtronMgpProtocol.TryParseBannerFirmware(banner, out var fw).Should().BeTrue();
        fw.Should().Be("1.05");
        ExtronMgpProtocol.IsBanner("Vmt1*1").Should().BeFalse();
    }

    [Theory]
    [InlineData("Password", true)]
    [InlineData("Login Administrator", false)]
    public void Password_Prompt(string line, bool expected) =>
        ExtronMgpProtocol.IsPasswordPrompt(line).Should().Be(expected);

    [Theory]
    [InlineData("Login Administrator")]
    [InlineData("Login User")]
    public void Login_Accepted(string line) => ExtronMgpProtocol.IsLoginAccepted(line).Should().BeTrue();

    [Theory]
    [InlineData("E10", "E10")]
    [InlineData("E24", "E24")]
    [InlineData("Vmt1*1", null)]
    public void Errors(string line, string? expected) => ExtronMgpProtocol.ParseError(line).Should().Be(expected);

    [Fact]
    public void Device_Info_Responses_Parse()
    {
        ExtronMgpProtocol.TryParseIpAddress("10.1.2.3", out var ip).Should().BeTrue();
        ip.Should().Be("10.1.2.3");
        ExtronMgpProtocol.TryParseIpAddress("1.05", out _).Should().BeFalse();

        ExtronMgpProtocol.TryParseMac("00-05-a6-12-34-56", out var mac).Should().BeTrue();
        mac.Should().Be("00-05-a6-12-34-56");
        ExtronMgpProtocol.TryParseMac("Iph•00-05-a6-12-34-56", out mac).Should().BeTrue();

        ExtronMgpProtocol.TryParseFirmware("Ver01*1.05", out var fw).Should().BeTrue();
        fw.Should().Be("1.05");
        ExtronMgpProtocol.TryParseFirmware("Bld1.05.0012", out fw).Should().BeTrue();
        fw.Should().Be("1.05.0012");

        ExtronMgpProtocol.TryParseTaggedUnitName("Ipn•MGP-641-00-02-3D", out var name).Should().BeTrue();
        name.Should().Be("MGP-641-00-02-3D");
    }
}
