using System;
using System.Collections.Generic;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using OsuToolsService.Grpc;
using OsuToolsService.Helpers;
using Xunit;

namespace OsuToolsService.Tests;

public class ModParserTests
{
    [Fact]
    public void ParseMods_Empty_ReturnsEmptyArray()
    {
        var ruleset = new OsuRuleset();
        var mods = ModParser.ParseMods(ruleset, new List<Mod>());

        Assert.Empty(mods);
    }

    [Fact]
    public void ParseMods_Null_ReturnsEmptyArray()
    {
        var ruleset = new OsuRuleset();
        var mods = ModParser.ParseMods(ruleset, null);

        Assert.Empty(mods);
    }

    [Fact]
    public void ParseMods_DT_ReturnsDoubleTimeMod()
    {
        var ruleset = new OsuRuleset();
        var mods = ModParser.ParseMods(ruleset, new List<Mod> { new Mod { Acronym = "DT" } });

        Assert.Single(mods);
        Assert.Contains(mods, m => m is osu.Game.Rulesets.Osu.Mods.OsuModDoubleTime);
    }

    [Fact]
    public void ParseMods_HD_ReturnsHiddenMod()
    {
        var ruleset = new OsuRuleset();
        var mods = ModParser.ParseMods(ruleset, new List<Mod> { new Mod { Acronym = "HD" } });

        Assert.Single(mods);
        Assert.Contains(mods, m => m is osu.Game.Rulesets.Osu.Mods.OsuModHidden);
    }

    [Fact]
    public void ParseMods_MultipleMods_ReturnsAllMods()
    {
        var ruleset = new OsuRuleset();
        var mods = ModParser.ParseMods(ruleset, new List<Mod>
        {
            new Mod { Acronym = "HD" },
            new Mod { Acronym = "DT" }
        });

        Assert.Equal(2, mods.Length);
    }

    [Fact]
    public void ParseMods_WithSettings_AppliesSettings()
    {
        var ruleset = new OsuRuleset();
        var mods = ModParser.ParseMods(ruleset, new List<Mod>
        {
            new Mod
            {
                Acronym = "DT",
                Settings =
                {
                    { "speed_change", "1.5" }
                }
            }
        });

        Assert.Single(mods);
        var dt = mods[0] as osu.Game.Rulesets.Osu.Mods.OsuModDoubleTime;
        Assert.NotNull(dt);
        Assert.Equal(1.5, dt!.SpeedChange.Value);
    }

    [Fact]
    public void ParseMods_TaikoRuleset_ReturnsTaikoMods()
    {
        var ruleset = new TaikoRuleset();
        var mods = ModParser.ParseMods(ruleset, new List<Mod> { new Mod { Acronym = "HD" } });

        Assert.Single(mods);
        Assert.Contains(mods, m => m is osu.Game.Rulesets.Taiko.Mods.TaikoModHidden);
    }

    [Fact]
    public void ParseMods_CatchRuleset_ReturnsCatchMods()
    {
        var ruleset = new CatchRuleset();
        var mods = ModParser.ParseMods(ruleset, new List<Mod> { new Mod { Acronym = "HD" } });

        Assert.Single(mods);
        Assert.Contains(mods, m => m is osu.Game.Rulesets.Catch.Mods.CatchModHidden);
    }

    [Fact]
    public void ParseMods_ManiaRuleset_ReturnsManiaMods()
    {
        var ruleset = new ManiaRuleset();
        var mods = ModParser.ParseMods(ruleset, new List<Mod> { new Mod { Acronym = "4K" } });

        Assert.Single(mods);
        Assert.Contains(mods, m => m is osu.Game.Rulesets.Mania.Mods.ManiaKeyMod);
    }

    [Fact]
    public void ParseMods_InvalidSettingValue_ThrowsFormatException()
    {
        var ruleset = new OsuRuleset();
        // "not-a-number" is not a valid double for SpeedChange
        Assert.Throws<FormatException>(() =>
            ModParser.ParseMods(ruleset, new List<Mod>
            {
                new Mod
                {
                    Acronym = "DT",
                    Settings = { { "speed_change", "not-a-number" } }
                }
            })
        );
    }

    [Fact]
    public void ParseMods_UnknownAcronym_SilentlySkipped()
    {
        var ruleset = new OsuRuleset();
        var mods = ModParser.ParseMods(ruleset, new List<Mod> { new Mod { Acronym = "ZZZ" } });

        Assert.Empty(mods);
    }
}
