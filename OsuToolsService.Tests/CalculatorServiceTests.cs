using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;
using osu.Game.Rulesets.Mods;
using OsuToolsService.Grpc;
using OsuToolsService.Helpers;
using Xunit;

namespace OsuToolsService.Tests;

public class CalculatorServiceTests
{
    private static byte[] getBeatmapData(string filename)
    {
        return File.ReadAllBytes($"fixtures/{filename}");
    }

    public CalculatorServiceTests()
    {
        Logger.Enabled = false;
        LegacyDifficultyCalculatorBeatmapDecoder.Register();
    }

    [Fact]
    public void DifficultyCalculation_OsuRuleset_ReturnsStarRating()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();

        var attributes = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(mods);

        Assert.True(attributes.StarRating > 0);
        Assert.True(attributes.MaxCombo > 0);
    }

    [Fact]
    public void DifficultyCalculation_TaikoRuleset_ReturnsStarRating()
    {
        var data = getBeatmapData("taiko.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new TaikoRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();

        var attributes = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(mods);

        Assert.True(attributes.StarRating > 0);
        Assert.True(attributes.MaxCombo > 0);
    }

    [Fact]
    public void DifficultyCalculation_CatchRuleset_ReturnsStarRating()
    {
        var data = getBeatmapData("catch.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new CatchRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();

        var attributes = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(mods);

        Assert.True(attributes.StarRating > 0);
        Assert.True(attributes.MaxCombo > 0);
    }

    [Fact]
    public void DifficultyCalculation_ManiaRuleset_ReturnsStarRating()
    {
        var data = getBeatmapData("mania.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new ManiaRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();

        var attributes = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(mods);

        Assert.True(attributes.StarRating > 0);
        Assert.True(attributes.MaxCombo > 0);
    }

    [Fact]
    public void DifficultyCalculation_WithDT_ReturnsDifferentStarRating()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();

        var noMods = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(Array.Empty<osu.Game.Rulesets.Mods.Mod>());
        var withDt = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(
            ModParser.ParseMods(ruleset, new List<Grpc.Mod> { new Grpc.Mod { Acronym = "DT" } })
        );

        // DT should generally increase star rating
        Assert.True(withDt.StarRating > 0);
        Assert.NotEqual(noMods.StarRating, withDt.StarRating);
    }

    private static double calculateClockRate(osu.Game.Rulesets.Mods.Mod[] mods)
    {
        double rate = 1.0;
        foreach (var mod in mods)
        {
            if (mod is IApplicableToRate applicableToRate)
                rate = applicableToRate.ApplyToRate(0, rate);
        }
        return rate;
    }

    private static (double ar, double od, double cs, double hp) getModdedAttributes(WorkingBeatmap workingBeatmap, osu.Game.Rulesets.Mods.Mod[] mods)
    {
        var playableBeatmap = workingBeatmap.GetPlayableBeatmap(workingBeatmap.BeatmapInfo.Ruleset, mods);
        var difficulty = playableBeatmap.Difficulty;

        double clockRate = calculateClockRate(mods);

        double preempt = IBeatmapDifficultyInfo.DifficultyRange(difficulty.ApproachRate, 1800, 1200, 450);
        double adjustedPreempt = preempt / clockRate;
        double ar = IBeatmapDifficultyInfo.InverseDifficultyRange(adjustedPreempt, 1800, 1200, 450);

        double hitWindowGreat = IBeatmapDifficultyInfo.DifficultyRange(difficulty.OverallDifficulty, 80, 50, 20);
        double adjustedWindow = hitWindowGreat / clockRate;
        double od = IBeatmapDifficultyInfo.InverseDifficultyRange(adjustedWindow, 80, 50, 20);

        return (ar, od, difficulty.CircleSize, difficulty.DrainRate);
    }

    [Fact]
    public void DifficultyAttributes_WithHR_AreModded()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();

        var mods = ModParser.ParseMods(ruleset, new List<Grpc.Mod> { new Grpc.Mod { Acronym = "HR" } });
        var (ar, od, cs, hp) = getModdedAttributes(workingBeatmap, mods);

        var baseDiff = workingBeatmap.BeatmapInfo.Difficulty;

        // HR multiplies by 1.4 and caps at 10
        Assert.True(ar > baseDiff.ApproachRate, "HR should increase AR");
        Assert.True(od > baseDiff.OverallDifficulty, "HR should increase OD");
        Assert.True(cs > baseDiff.CircleSize, "HR should increase CS");
        Assert.True(hp > baseDiff.DrainRate, "HR should increase HP");
        Assert.True(ar <= 10, "AR should be capped at 10");
        Assert.True(od <= 10, "OD should be capped at 10");
    }

    [Fact]
    public void DifficultyAttributes_WithEZ_AreModded()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();

        var mods = ModParser.ParseMods(ruleset, new List<Grpc.Mod> { new Grpc.Mod { Acronym = "EZ" } });
        var (ar, od, cs, hp) = getModdedAttributes(workingBeatmap, mods);

        var baseDiff = workingBeatmap.BeatmapInfo.Difficulty;

        // EZ halves all difficulty values
        Assert.True(ar < baseDiff.ApproachRate, "EZ should decrease AR");
        Assert.True(od < baseDiff.OverallDifficulty, "EZ should decrease OD");
        Assert.True(cs < baseDiff.CircleSize, "EZ should decrease CS");
        Assert.True(hp < baseDiff.DrainRate, "EZ should decrease HP");
    }

    [Fact]
    public void DifficultyAttributes_NoMods_MatchBaseBeatmap()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();

        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();
        var (ar, od, cs, hp) = getModdedAttributes(workingBeatmap, mods);

        var baseDiff = workingBeatmap.BeatmapInfo.Difficulty;

        Assert.Equal(baseDiff.ApproachRate, ar, 0.001);
        Assert.Equal(baseDiff.OverallDifficulty, od, 0.001);
        Assert.Equal(baseDiff.CircleSize, cs, 0.001);
        Assert.Equal(baseDiff.DrainRate, hp, 0.001);
    }

    [Fact]
    public void DifficultyAttributes_WithHR_HasExactModdedValues()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();

        var mods = ModParser.ParseMods(ruleset, new List<Grpc.Mod> { new Grpc.Mod { Acronym = "HR" } });
        var (ar, od, cs, hp) = getModdedAttributes(workingBeatmap, mods);

        // Base values: AR=9.6, OD=9.2, CS=4, HP=5.5
        // HR multiplies AR/OD/HP by 1.4, CS by 1.3, all capped at 10
        Assert.Equal(Math.Min(9.6 * 1.4, 10), ar, 0.001);
        Assert.Equal(Math.Min(9.2 * 1.4, 10), od, 0.001);
        Assert.Equal(Math.Min(4 * 1.3, 10), cs, 0.001);
        Assert.Equal(Math.Min(5.5 * 1.4, 10), hp, 0.001);
    }

    [Fact]
    public void DifficultyAttributes_WithEZ_HasExactModdedValues()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();

        var mods = ModParser.ParseMods(ruleset, new List<Grpc.Mod> { new Grpc.Mod { Acronym = "EZ" } });
        var (ar, od, cs, hp) = getModdedAttributes(workingBeatmap, mods);

        // Base values: AR=9.6, OD=9.2, CS=4, HP=5.5
        // EZ halves all values
        Assert.Equal(9.6 / 2, ar, 0.001);
        Assert.Equal(9.2 / 2, od, 0.001);
        Assert.Equal(4.0 / 2, cs, 0.001);
        Assert.Equal(5.5 / 2, hp, 0.001);
    }

    [Fact]
    public void DifficultyAttributes_WithDA_OverridesSpecificValues()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();

        var mods = ModParser.ParseMods(ruleset, new List<Grpc.Mod>
        {
            new Grpc.Mod
            {
                Acronym = "DA",
                Settings =
                {
                    { "circle_size", "7" },
                    { "approach_rate", "3" },
                    { "overall_difficulty", "1" },
                    { "drain_rate", "8" }
                }
            }
        });
        var (ar, od, cs, hp) = getModdedAttributes(workingBeatmap, mods);

        Assert.Equal(7, cs, 0.001);
        Assert.Equal(3, ar, 0.001);
        Assert.Equal(1, od, 0.001);
        Assert.Equal(8, hp, 0.001);
    }

    [Fact]
    public void DifficultyAttributes_WithDA_PartialOverride_KeepsOthers()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();

        var mods = ModParser.ParseMods(ruleset, new List<Grpc.Mod>
        {
            new Grpc.Mod
            {
                Acronym = "DA",
                Settings =
                {
                    { "approach_rate", "2" }
                }
            }
        });
        var (ar, od, cs, hp) = getModdedAttributes(workingBeatmap, mods);

        var baseDiff = workingBeatmap.BeatmapInfo.Difficulty;

        // Only AR should be overridden
        Assert.Equal(2, ar, 0.001);
        // Others should remain at base values
        Assert.Equal(baseDiff.OverallDifficulty, od, 0.001);
        Assert.Equal(baseDiff.CircleSize, cs, 0.001);
        Assert.Equal(baseDiff.DrainRate, hp, 0.001);
    }

    [Fact]
    public void DifficultyAttributes_WithDT_IncreasesEffectiveArAndOd()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();

        var mods = ModParser.ParseMods(ruleset, new List<Grpc.Mod> { new Grpc.Mod { Acronym = "DT" } });
        var (ar, od, _, _) = getModdedAttributes(workingBeatmap, mods);

        var baseDiff = workingBeatmap.BeatmapInfo.Difficulty;

        // Base: AR=9.6, OD=9.2, DT rate=1.5
        // AR: 9.6 -> 510ms -> 340ms -> 10.73
        // OD: 9.2 -> 24.8ms -> 16.53ms -> 10.58
        Assert.True(ar > baseDiff.ApproachRate, "DT should increase effective AR");
        Assert.True(od > baseDiff.OverallDifficulty, "DT should increase effective OD");
        Assert.Equal(10.73, ar, 0.01);
        Assert.Equal(10.58, od, 0.01);
    }

    [Fact]
    public void DifficultyAttributes_WithHRAndDT_ArOdExceed10()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();

        var mods = ModParser.ParseMods(ruleset, new List<Grpc.Mod>
        {
            new Grpc.Mod { Acronym = "HR" },
            new Grpc.Mod { Acronym = "DT" }
        });
        var (ar, od, _, _) = getModdedAttributes(workingBeatmap, mods);

        // Base: AR=9.6, OD=9.2
        // After HR: AR=10 (capped), OD=10 (capped)
        // After DT (rate=1.5): AR=11, OD=11.11
        Assert.Equal(11.0, ar, 0.01);
        Assert.Equal(11.11, od, 0.01);
    }

    [Fact]
    public void DifficultyAttributes_WithHT_DecreasesEffectiveArAndOd()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();

        var mods = ModParser.ParseMods(ruleset, new List<Grpc.Mod> { new Grpc.Mod { Acronym = "HT" } });
        var (ar, od, _, _) = getModdedAttributes(workingBeatmap, mods);

        var baseDiff = workingBeatmap.BeatmapInfo.Difficulty;

        // HT rate=0.75 should decrease effective AR and OD
        Assert.True(ar < baseDiff.ApproachRate, "HT should decrease effective AR");
        Assert.True(od < baseDiff.OverallDifficulty, "HT should decrease effective OD");
    }

    [Fact]
    public void DifficultyCalculation_AutoDetectRuleset_Works()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);

        // The osu! beatmap has Mode: 0, so auto-detect should use osu! ruleset
        Assert.Equal(0, workingBeatmap.BeatmapInfo.Ruleset.OnlineID);
    }

    [Fact]
    public void DifficultyCalculation_CrossRuleset_StdToCatch_Works()
    {
        // An osu! standard beatmap can be played with the catch ruleset (rulesetId=2)
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new osu.Game.Rulesets.Catch.CatchRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();

        var attributes = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(mods);

        Assert.True(attributes.StarRating > 0);
        Assert.True(attributes.MaxCombo > 0);
    }

    [Fact]
    public void PerformanceCalculation_OsuRuleset_ReturnsPP()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();

        var playableBeatmap = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
        var beatmapMaxCombo = playableBeatmap.GetMaxCombo();

        var statistics = HitResultGenerator.Generate(
            ruleset, playableBeatmap, mods,
            accuracy: 100, misses: 0,
            mehs: null, goods: null, oks: null, greats: null,
            largeTickMisses: 0, sliderTailMisses: 0,
            tinyDroplets: null, droplets: null
        );

        var accuracy = HitResultGenerator.GetAccuracy(ruleset, playableBeatmap, statistics, mods);

        var scoreInfo = new osu.Game.Scoring.ScoreInfo(playableBeatmap.BeatmapInfo, ruleset.RulesetInfo)
        {
            Accuracy = accuracy,
            MaxCombo = beatmapMaxCombo,
            Statistics = statistics,
            Mods = mods
        };

        var difficultyAttributes = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(mods);
        var performanceCalculator = ruleset.CreatePerformanceCalculator();
        var performanceAttributes = performanceCalculator?.Calculate(scoreInfo, difficultyAttributes);

        Assert.NotNull(performanceAttributes);
        Assert.True(performanceAttributes!.Total > 0);
    }

    [Fact]
    public void PerformanceCalculation_WithMisses_ReturnsLowerPP()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();

        var playableBeatmap = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
        var beatmapMaxCombo = playableBeatmap.GetMaxCombo();

        // Perfect score
        var perfectStats = HitResultGenerator.Generate(
            ruleset, playableBeatmap, mods, 100, 0,
            null, null, null, null, 0, 0, null, null
        );
        var perfectAccuracy = HitResultGenerator.GetAccuracy(ruleset, playableBeatmap, perfectStats, mods);
        var perfectScore = new osu.Game.Scoring.ScoreInfo(playableBeatmap.BeatmapInfo, ruleset.RulesetInfo)
        {
            Accuracy = perfectAccuracy,
            MaxCombo = beatmapMaxCombo,
            Statistics = perfectStats,
            Mods = mods
        };

        // Score with misses
        var missStats = HitResultGenerator.Generate(
            ruleset, playableBeatmap, mods, 80, 1,
            null, null, null, null, 0, 0, null, null
        );
        var missAccuracy = HitResultGenerator.GetAccuracy(ruleset, playableBeatmap, missStats, mods);
        var missScore = new osu.Game.Scoring.ScoreInfo(playableBeatmap.BeatmapInfo, ruleset.RulesetInfo)
        {
            Accuracy = missAccuracy,
            MaxCombo = beatmapMaxCombo,
            Statistics = missStats,
            Mods = mods
        };

        var diffAttrs = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(mods);
        var perfCalc = ruleset.CreatePerformanceCalculator();

        var perfectPP = perfCalc?.Calculate(perfectScore, diffAttrs)?.Total ?? 0;
        var missPP = perfCalc?.Calculate(missScore, diffAttrs)?.Total ?? 0;

        // Score with misses should have lower PP than perfect score
        Assert.True(missPP < perfectPP);
    }

    // === BPM / Length / DrainLength tests ===

    private static (double bpm, double length, double drainLength) computeBpmAndLength(WorkingBeatmap beatmap, osu.Game.Rulesets.Mods.Mod[] mods)
    {
        double baseBpm = 0;
        var timingPoints = beatmap.Beatmap.ControlPointInfo.TimingPoints;
        if (timingPoints.Count > 0)
            baseBpm = 60000 / beatmap.Beatmap.GetMostCommonBeatLength();

        double rate = 1.0;
        foreach (var mod in mods)
        {
            if (mod is IApplicableToRate applicableToRate)
                rate = applicableToRate.ApplyToRate(0, rate);
        }

        var playableBeatmap = beatmap.Beatmap;
        double moddedLength = playableBeatmap.CalculatePlayableLength() / rate / 1000.0;
        double moddedDrainLength = playableBeatmap.CalculateDrainLength() / rate / 1000.0;

        return (baseBpm * rate, moddedLength, moddedDrainLength);
    }

    [Fact]
    public void BpmAndLength_NoMods_MatchesBaseValues()
    {
        var data = getBeatmapData("osu.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var (bpm, length, drainLength) = computeBpmAndLength(wb, Array.Empty<osu.Game.Rulesets.Mods.Mod>());

        Assert.Equal(204.0, bpm, 0.01);
        Assert.Equal(249.117, length, 0.01);
        Assert.Equal(227.593, drainLength, 0.01);
        Assert.True(drainLength < length, "Drain length should be less than total length (map has breaks)");
    }

    [Fact]
    public void BpmAndLength_WithDT_ScalesByRate()
    {
        var data = getBeatmapData("osu.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();
        var mods = ModParser.ParseMods(ruleset, new List<Grpc.Mod> { new Grpc.Mod { Acronym = "DT" } });

        var (bpm, length, drainLength) = computeBpmAndLength(wb, mods);

        // DT rate = 1.5
        Assert.Equal(204.0 * 1.5, bpm, 0.01);
        Assert.Equal(249.117 / 1.5, length, 0.01);
        Assert.Equal(227.593 / 1.5, drainLength, 0.01);
    }

    [Fact]
    public void BpmAndLength_WithHT_ScalesByRate()
    {
        var data = getBeatmapData("osu.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();
        var mods = ModParser.ParseMods(ruleset, new List<Grpc.Mod> { new Grpc.Mod { Acronym = "HT" } });

        var (bpm, length, drainLength) = computeBpmAndLength(wb, mods);

        // HT rate = 0.75
        Assert.Equal(204.0 * 0.75, bpm, 0.01);
        Assert.Equal(249.117 / 0.75, length, 0.01);
        Assert.Equal(227.593 / 0.75, drainLength, 0.01);
    }

    [Fact]
    public void BpmAndLength_WithCustomRate_ScalesByCustomRate()
    {
        var data = getBeatmapData("osu.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();
        var mods = ModParser.ParseMods(ruleset, new List<Grpc.Mod>
        {
            new Grpc.Mod { Acronym = "DT", Settings = { { "speed_change", "1.1" } } }
        });

        var (bpm, length, drainLength) = computeBpmAndLength(wb, mods);

        // Custom DT rate = 1.1
        Assert.Equal(204.0 * 1.1, bpm, 0.01);
        Assert.Equal(249.117 / 1.1, length, 0.01);
        Assert.Equal(227.593 / 1.1, drainLength, 0.01);
    }

    [Fact]
    public void BpmAndLength_ManiaNoBreaks_DrainEqualsLength()
    {
        var data = getBeatmapData("mania.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var (bpm, length, drainLength) = computeBpmAndLength(wb, Array.Empty<osu.Game.Rulesets.Mods.Mod>());

        Assert.Equal(200.0, bpm, 0.01);
        Assert.Equal(172.8, length, 0.01);
        // mania fixture has no breaks, so drain == length
        Assert.Equal(length, drainLength, 0.01);
    }

    // === Performance tests for non-osu rulesets ===

    [Fact]
    public void PerformanceCalculation_TaikoRuleset_ReturnsPP()
    {
        var data = getBeatmapData("taiko.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new TaikoRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();

        var playableBeatmap = wb.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
        var beatmapMaxCombo = playableBeatmap.GetMaxCombo();

        var statistics = HitResultGenerator.Generate(
            ruleset, playableBeatmap, mods, 100, 0,
            null, null, null, null, 0, 0, null, null
        );
        var accuracy = HitResultGenerator.GetAccuracy(ruleset, playableBeatmap, statistics, mods);

        var scoreInfo = new osu.Game.Scoring.ScoreInfo(playableBeatmap.BeatmapInfo, ruleset.RulesetInfo)
        {
            Accuracy = accuracy,
            MaxCombo = beatmapMaxCombo,
            Statistics = statistics,
            Mods = mods
        };

        var diffAttrs = ruleset.CreateDifficultyCalculator(wb).Calculate(mods);
        var perfCalc = ruleset.CreatePerformanceCalculator();
        var perfAttrs = perfCalc?.Calculate(scoreInfo, diffAttrs);

        Assert.NotNull(perfAttrs);
        Assert.True(perfAttrs!.Total > 0);
    }

    [Fact]
    public void PerformanceCalculation_CatchRuleset_ReturnsPP()
    {
        var data = getBeatmapData("catch.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new CatchRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();

        var playableBeatmap = wb.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
        var beatmapMaxCombo = playableBeatmap.GetMaxCombo();

        var statistics = HitResultGenerator.Generate(
            ruleset, playableBeatmap, mods, 100, 0,
            null, null, null, null, 0, 0, null, null
        );
        var accuracy = HitResultGenerator.GetAccuracy(ruleset, playableBeatmap, statistics, mods);

        var scoreInfo = new osu.Game.Scoring.ScoreInfo(playableBeatmap.BeatmapInfo, ruleset.RulesetInfo)
        {
            Accuracy = accuracy,
            MaxCombo = beatmapMaxCombo,
            Statistics = statistics,
            Mods = mods
        };

        var diffAttrs = ruleset.CreateDifficultyCalculator(wb).Calculate(mods);
        var perfCalc = ruleset.CreatePerformanceCalculator();
        var perfAttrs = perfCalc?.Calculate(scoreInfo, diffAttrs);

        Assert.NotNull(perfAttrs);
        Assert.True(perfAttrs!.Total > 0);
    }

    [Fact]
    public void PerformanceCalculation_ManiaRuleset_ReturnsPP()
    {
        var data = getBeatmapData("mania.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new ManiaRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();

        var playableBeatmap = wb.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
        var beatmapMaxCombo = playableBeatmap.GetMaxCombo();

        var statistics = HitResultGenerator.Generate(
            ruleset, playableBeatmap, mods, 100, 0,
            null, null, null, null, 0, 0, null, null
        );
        var accuracy = HitResultGenerator.GetAccuracy(ruleset, playableBeatmap, statistics, mods);

        var scoreInfo = new osu.Game.Scoring.ScoreInfo(playableBeatmap.BeatmapInfo, ruleset.RulesetInfo)
        {
            Accuracy = accuracy,
            MaxCombo = beatmapMaxCombo,
            Statistics = statistics,
            Mods = mods
        };

        var diffAttrs = ruleset.CreateDifficultyCalculator(wb).Calculate(mods);
        var perfCalc = ruleset.CreatePerformanceCalculator();
        var perfAttrs = perfCalc?.Calculate(scoreInfo, diffAttrs);

        Assert.NotNull(perfAttrs);
        Assert.True(perfAttrs!.Total > 0);
    }

    // === HitResultGenerator tests for non-osu rulesets ===

    [Fact]
    public void HitResultGenerator_Taiko_100Accuracy_NoMisses_AllGreat()
    {
        var data = getBeatmapData("taiko.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new TaikoRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();
        var playableBeatmap = wb.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

        var stats = HitResultGenerator.Generate(ruleset, playableBeatmap, mods, 100, 0, null, null, null, null, 0, 0, null, null);

        Assert.Equal(0, stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Miss));
        Assert.Equal(0, stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Meh));
        Assert.Equal(0, stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Ok));
        Assert.True(stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Great) > 0);
    }

    [Fact]
    public void HitResultGenerator_Taiko_WithMisses_HasCorrectMissCount()
    {
        var data = getBeatmapData("taiko.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new TaikoRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();
        var playableBeatmap = wb.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

        var stats = HitResultGenerator.Generate(ruleset, playableBeatmap, mods, 95, 5, null, null, null, null, 0, 0, null, null);

        Assert.Equal(5, stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Miss));
        Assert.True(stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Great) > 0);
    }

    [Fact]
    public void HitResultGenerator_Catch_100Accuracy_NoMisses_AllFruits()
    {
        var data = getBeatmapData("catch.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new CatchRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();
        var playableBeatmap = wb.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

        var stats = HitResultGenerator.Generate(ruleset, playableBeatmap, mods, 100, 0, null, null, null, null, 0, 0, null, null);

        Assert.Equal(0, stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Miss));
        Assert.True(stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Great) > 0);
    }

    [Fact]
    public void HitResultGenerator_Catch_WithMisses_HasCorrectMissCount()
    {
        var data = getBeatmapData("catch.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new CatchRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();
        var playableBeatmap = wb.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

        var stats = HitResultGenerator.Generate(ruleset, playableBeatmap, mods, 90, 3, null, null, null, null, 0, 0, null, null);

        Assert.Equal(3, stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Miss));
    }

    [Fact]
    public void HitResultGenerator_Mania_100Accuracy_NoMisses_AllPerfect()
    {
        var data = getBeatmapData("mania.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new ManiaRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();
        var playableBeatmap = wb.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

        var stats = HitResultGenerator.Generate(ruleset, playableBeatmap, mods, 100, 0, null, null, null, null, 0, 0, null, null);

        Assert.Equal(0, stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Miss));
        Assert.True(stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Perfect) > 0);
    }

    [Fact]
    public void HitResultGenerator_Mania_WithMisses_HasCorrectMissCount()
    {
        var data = getBeatmapData("mania.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new ManiaRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();
        var playableBeatmap = wb.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

        var stats = HitResultGenerator.Generate(ruleset, playableBeatmap, mods, 95, 2, null, null, null, null, 0, 0, null, null);

        Assert.Equal(2, stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Miss));
    }

    [Fact]
    public void HitResultGenerator_Mania_LowAccuracy_ProducesValidStats()
    {
        var data = getBeatmapData("mania.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new ManiaRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();
        var playableBeatmap = wb.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

        // Very low accuracy should not crash and should produce valid stats
        var stats = HitResultGenerator.Generate(ruleset, playableBeatmap, mods, 10, 0, null, null, null, null, 0, 0, null, null);

        var totalHits = stats.Values.Sum();
        Assert.True(totalHits > 0);
        Assert.True(stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Miss) > 0);
    }

    // === Precalculated difficulty passthrough test ===

    [Fact]
    public void HitResultGenerator_Osu_OverSpecifiedMehsGoods_DoesNotProduceNegativeCounts()
    {
        var data = getBeatmapData("osu.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();
        var playableBeatmap = wb.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

        int totalObjects = playableBeatmap.HitObjects.Count;

        // Specify far more mehs and goods than the map has objects
        var stats = HitResultGenerator.Generate(
            ruleset, playableBeatmap, mods,
            accuracy: 50, misses: 5,
            mehs: totalObjects, goods: totalObjects,
            oks: null, greats: null,
            largeTickMisses: 0, sliderTailMisses: 0,
            tinyDroplets: null, droplets: null
        );

        // No count should be negative
        foreach (var kvp in stats)
        {
            Assert.True(kvp.Value >= 0, $"HitResult {kvp.Key} was {kvp.Value}, should be >= 0");
        }

        // Greats should be clamped to 0 (everything is mehs, goods, and misses)
        Assert.Equal(0, stats.GetValueOrDefault(osu.Game.Rulesets.Scoring.HitResult.Great));
    }

    [Fact]
    public void HitResultGenerator_Taiko_OverSpecifiedGoods_DoesNotProduceNegativeCounts()
    {
        var data = getBeatmapData("taiko.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new TaikoRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();
        var playableBeatmap = wb.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

        int maxCombo = playableBeatmap.GetMaxCombo();

        // Specify more goods than the map has combo
        var stats = HitResultGenerator.Generate(
            ruleset, playableBeatmap, mods,
            accuracy: 50, misses: 5,
            mehs: null, goods: maxCombo, oks: null, greats: null,
            largeTickMisses: 0, sliderTailMisses: 0,
            tinyDroplets: null, droplets: null
        );

        foreach (var kvp in stats)
        {
            Assert.True(kvp.Value >= 0, $"HitResult {kvp.Key} was {kvp.Value}, should be >= 0");
        }
    }

    [Fact]
    public void PerformanceCalculation_WithPrecalculatedDifficulty_MatchesDirectCalc()
    {
        var data = getBeatmapData("osu.osu");
        var wb = new MemoryWorkingBeatmap(data);
        var ruleset = new OsuRuleset();
        var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();

        var diffAttrs = ruleset.CreateDifficultyCalculator(wb).Calculate(mods);
        // Use the same serialization settings as CalculatorService (AllPropertiesContractResolver)
        var settings = new Newtonsoft.Json.JsonSerializerSettings
        {
            ContractResolver = new OsuToolsService.Services.AllPropertiesContractResolver()
        };
        var diffJson = JsonConvert.SerializeObject(diffAttrs, settings);

        // Deserialize back (simulating what the API does)
        var deserializedType = typeof(osu.Game.Rulesets.Osu.Difficulty.OsuDifficultyAttributes);
        var restoredAttrs = (osu.Game.Rulesets.Difficulty.DifficultyAttributes)JsonConvert.DeserializeObject(diffJson, deserializedType, settings)!;

        // Calculate performance with both
        var playableBeatmap = wb.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
        var stats = HitResultGenerator.Generate(ruleset, playableBeatmap, mods, 100, 0, null, null, null, null, 0, 0, null, null);
        var accuracy = HitResultGenerator.GetAccuracy(ruleset, playableBeatmap, stats, mods);
        var scoreInfo = new osu.Game.Scoring.ScoreInfo(playableBeatmap.BeatmapInfo, ruleset.RulesetInfo)
        {
            Accuracy = accuracy,
            MaxCombo = playableBeatmap.GetMaxCombo(),
            Statistics = stats,
            Mods = mods
        };

        var perfCalc = ruleset.CreatePerformanceCalculator();
        var ppDirect = perfCalc?.Calculate(scoreInfo, diffAttrs)?.Total ?? 0;
        var ppFromJson = perfCalc?.Calculate(scoreInfo, restoredAttrs)?.Total ?? 0;

        Assert.Equal(ppDirect, ppFromJson, 0.001);
    }
}
