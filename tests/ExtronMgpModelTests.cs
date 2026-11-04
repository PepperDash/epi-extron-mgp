using FluentAssertions;
using Xunit;

namespace PepperDash.Essentials.Plugins.Extron.Mgp.Tests;

public class ExtronMgpModelTests
{
    private static ExtronMgpModel NewModel(bool namedLayouts = true) => new(
        new[]
        {
            new ExtronMgpPresetDefinition { Id = "quad", Preset = 5, Name = namedLayouts ? "Quad" : null },
            new ExtronMgpPresetDefinition { Id = "full", Preset = 1, Name = namedLayouts ? "Full" : null },
            new ExtronMgpPresetDefinition { Id = "bad", Preset = 0 },       // dropped: out of range
            new ExtronMgpPresetDefinition { Id = "bad2", Preset = 129 },    // dropped: out of range
        });

    [Fact]
    public void Layouts_Are_Filtered_And_Ordered_By_Preset()
    {
        NewModel().Layouts.Select(l => l.Id).Should().Equal("full", "quad");
    }

    [Fact]
    public void Null_Definitions_Are_Tolerated()
    {
        var m = new ExtronMgpModel(null!);
        m.Layouts.Should().BeEmpty();
    }

    [Fact]
    public void Id_Resolution()
    {
        var m = NewModel();
        m.TryGetLayoutPreset("quad", out var p).Should().BeTrue();
        p.Should().Be(5);
        m.TryGetLayoutPreset("nope", out _).Should().BeFalse();
        m.TryGetLayoutPreset(null!, out _).Should().BeFalse();
    }

    [Fact]
    public void Layout_Names_Fall_Back_From_Config_To_Unit_To_Default()
    {
        var m = NewModel(namedLayouts: false);
        m.Layouts.Single(l => l.Id == "quad").Name.Should().Be("Layout 5");

        m.ProcessLine("Pnam1*005,Four Up").Should().Be(ExtronMgpLineKind.Handled);
        m.Layouts.Single(l => l.Id == "quad").Name.Should().Be("Four Up");

        // Configured name wins over the unit's
        var named = NewModel();
        named.ProcessLine("Pnam1*005,Something Else");
        named.Layouts.Single(l => l.Id == "quad").Name.Should().Be("Quad");

        // Input preset names are not tracked
        m.ProcessLine("Pnam2*005,Ignored").Should().Be(ExtronMgpLineKind.Handled);
        m.Layouts.Single(l => l.Id == "quad").Name.Should().Be("Four Up");
    }

    [Fact]
    public void Only_Unnamed_Presets_Need_A_Name_Query()
    {
        var m = NewModel(namedLayouts: false);
        m.LayoutPresetsNeedingName.Should().Equal(1, 5);
        NewModel().LayoutPresetsNeedingName.Should().BeEmpty();
    }

    [Fact]
    public void Layout_Feedback_Updates_State()
    {
        var m = NewModel();
        m.ProcessLine("Prst L 1*005").Should().Be(ExtronMgpLineKind.Handled);
        m.CurrentLayout.Should().Be(5);
        m.ProcessLine("1Rpr001\r\n");
        m.CurrentLayout.Should().Be(1);
    }

    [Fact]
    public void Input_Preset_Recalls_Are_Not_Tracked()
    {
        var m = NewModel();
        m.ProcessLine("2Rpr3*010").Should().Be(ExtronMgpLineKind.Unhandled);
        m.CurrentLayout.Should().Be(0);
    }

    [Fact]
    public void Mute_And_Freeze_Including_All_Windows()
    {
        var m = NewModel();
        m.ProcessLine("Vmt2*1");
        m.GetMute(2).Should().BeTrue();
        m.GetMute(1).Should().BeFalse();

        m.ProcessLine("Vmt0*1");
        Enumerable.Range(1, 4).Should().OnlyContain(w => m.GetMute(w));

        m.ProcessLine("Frz0*1");
        Enumerable.Range(1, 4).Should().OnlyContain(w => m.GetFreeze(w));
        m.ProcessLine("Frz3*0");
        m.GetFreeze(3).Should().BeFalse();
        m.GetFreeze(4).Should().BeTrue();
    }

    [Fact]
    public void Out_Of_Range_Windows_Are_Ignored()
    {
        var m = NewModel();
        m.SetMute(5, true);
        m.GetMute(5).Should().BeFalse();
    }

    [Fact]
    public void StateChanged_Fires_Only_On_Actual_Change()
    {
        var m = NewModel();
        var count = 0;
        m.StateChanged += (_, _) => count++;

        m.SetCurrentLayout(5);
        m.SetCurrentLayout(5);
        m.SetMute(1, true);
        m.SetMute(1, true);
        m.SetMute(0, true);   // windows 2-4 change, one event
        m.SetLayoutName(5, "X");
        m.SetLayoutName(5, "X");

        count.Should().Be(4);
    }

    [Fact]
    public void Error_Lines_Record_The_Code()
    {
        var m = NewModel();
        m.ProcessLine("E13").Should().Be(ExtronMgpLineKind.Error);
        m.LastError.Should().Be("E13");
    }

    [Fact]
    public void Connect_Lines_Are_Classified_For_The_Transport()
    {
        var m = NewModel();
        m.ProcessLine("Password").Should().Be(ExtronMgpLineKind.PasswordPrompt);
        m.ProcessLine("Login Administrator").Should().Be(ExtronMgpLineKind.LoginAccepted);
        m.ProcessLine("(c) Copyright 2026, Extron Electronics, MGP 641, V1.05, 60-1574-01").Should().Be(ExtronMgpLineKind.Banner);
        m.FirmwareVersion.Should().Be("1.05");
        m.ProcessLine("\r\n").Should().Be(ExtronMgpLineKind.Empty);
    }

    [Fact]
    public void Unsolicited_And_Echoed_Lines_Are_Unhandled_And_Change_Nothing()
    {
        var m = NewModel();
        foreach (var line in new[] { "Reconfig1", "HplgO1", "HdcpI1*2", "\u001b0ECHO", "1*5." })
            m.ProcessLine(line).Should().Be(ExtronMgpLineKind.Unhandled, line);
        m.CurrentLayout.Should().Be(0);
    }

    [Fact]
    public void Device_Info_From_Tagged_And_Bare_Replies()
    {
        var m = NewModel();
        var infoEvents = 0;
        m.InfoChanged += (_, _) => infoEvents++;

        m.ProcessLine("192.168.1.50");
        m.ProcessLine("00-05-A6-AA-BB-CC");
        m.ProcessLine("Bld1.05.0012");
        m.ExpectUnitName();
        m.ProcessLine("MGP-641-AA-BB-CC");   // bare name, taken because a name was expected

        m.IpAddress.Should().Be("192.168.1.50");
        m.MacAddress.Should().Be("00-05-A6-AA-BB-CC");
        m.FirmwareVersion.Should().Be("1.05.0012");
        m.HostName.Should().Be("MGP-641-AA-BB-CC");
        infoEvents.Should().Be(4);
    }

    [Fact]
    public void A_Bare_Line_Is_Not_A_Name_Unless_One_Was_Expected()
    {
        var m = NewModel();
        m.ProcessLine("some-unknown-line").Should().Be(ExtronMgpLineKind.Unhandled);
        m.HostName.Should().BeNull();

        m.ExpectUnitName();
        m.ProcessLine("some-unknown-line").Should().Be(ExtronMgpLineKind.Handled);
        m.HostName.Should().Be("some-unknown-line");

        // expectation is consumed
        m.ProcessLine("another-line").Should().Be(ExtronMgpLineKind.Unhandled);
    }

    [Fact]
    public void Tagged_Unit_Name_Clears_The_Expectation()
    {
        var m = NewModel();
        m.ExpectUnitName();
        m.ProcessLine("Ipn•BoardroomMGP");
        m.HostName.Should().Be("BoardroomMGP");
        m.ProcessLine("stray").Should().Be(ExtronMgpLineKind.Unhandled);
    }
}
