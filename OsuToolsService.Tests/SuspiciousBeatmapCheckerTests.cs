using System;
using System.Globalization;
using System.IO;
using System.Text;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using OsuToolsService.Helpers;
using Xunit;

namespace OsuToolsService.Tests;

public class SuspiciousBeatmapCheckerTests
{
    private const double DAY_MS = 24 * 60 * 60 * 1000.0;

    public SuspiciousBeatmapCheckerTests()
    {
        Logger.Enabled = false;
        osu.Game.Beatmaps.Formats.LegacyDifficultyCalculatorBeatmapDecoder.Register();
    }

    /// <summary>
    /// Builds a minimal valid .osu around the given hit object lines and decodes it through
    /// the real decoder, so every case exercises the same path as production.
    /// </summary>
    private static IBeatmap decodeOsu(string hitObjects, int mode = 0, float circleSize = 4)
    {
        var osuFile = new StringBuilder()
            .AppendLine("osu file format v14")
            .AppendLine()
            .AppendLine("[General]")
            .AppendLine("AudioFilename: audio.mp3")
            .AppendLine("AudioLeadIn: 0")
            .AppendLine("Mode: " + mode)
            .AppendLine()
            .AppendLine("[Metadata]")
            .AppendLine("Title: synthetic")
            .AppendLine("Artist: test")
            .AppendLine("Creator: test")
            .AppendLine("Version: synthetic")
            .AppendLine()
            .AppendLine("[Difficulty]")
            .AppendLine("CircleSize: " + circleSize.ToString(CultureInfo.InvariantCulture))
            .AppendLine("OverallDifficulty: 5")
            .AppendLine("ApproachRate: 9")
            .AppendLine("SliderMultiplier: 1.4")
            .AppendLine("SliderTickRate: 1")
            .AppendLine()
            .AppendLine("[TimingPoints]")
            .AppendLine("0,600,4,2,0,60,1,0")
            .AppendLine()
            .AppendLine("[HitObjects]")
            .AppendLine(hitObjects)
            .ToString();

        return new MemoryWorkingBeatmap(Encoding.UTF8.GetBytes(osuFile)).Beatmap;
    }

    private static string circles(int count, double spacingMs)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
            sb.Append("256,192,").Append((int)(i * spacingMs)).Append(",1,0,0:0:0:0:\n");
        return sb.ToString();
    }

    /// <param name="slides">File-level slide count; repeat count is slides - 1.</param>
    private static string sliders(int count, int slides, int x)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
            sb.Append(x).Append(",192,").Append(i * 1000).Append(",2,0,L|100:0,").Append(slides).Append(",600,0:0:0:0:\n");
        return sb.ToString();
    }

    [Fact]
    public void Check_EmptyBeatmap_IsNotSuspicious()
    {
        Assert.Null(SuspiciousBeatmapChecker.Check(decodeOsu(""), 0));
    }

    [Fact]
    public void Check_SparseBeatmap_IsNotSuspicious()
    {
        // 100 notes spread over 100s
        Assert.Null(SuspiciousBeatmapChecker.Check(decodeOsu(circles(100, 1000.0)), 0));
        Assert.Null(SuspiciousBeatmapChecker.Check(decodeOsu(circles(100, 1000.0)), 3));
    }

    [Fact]
    public void Check_TooManyObjects_Taiko_IsSuspicious()
    {
        var beatmap = decodeOsu(circles(30_001, 1000.0));

        Assert.Equal(SuspiciousBeatmapChecker.REASON_OBJECT_COUNT, SuspiciousBeatmapChecker.Check(beatmap, 1));
        // 30_001 objects is far below the 500k threshold of other rulesets
        Assert.Null(SuspiciousBeatmapChecker.Check(beatmap, 0));
        Assert.Null(SuspiciousBeatmapChecker.Check(beatmap, 3));
    }

    [Fact]
    public void Check_TooLong_IsSuspicious()
    {
        var hitObjects = $"256,192,0,1,0,0:0:0:0:\n256,192,{(int)DAY_MS + 1},1,0,0:0:0:0:\n";

        Assert.Equal(SuspiciousBeatmapChecker.REASON_LENGTH, SuspiciousBeatmapChecker.Check(decodeOsu(hitObjects), 0));
    }

    [Fact]
    public void Check_Density_Osu_IsSuspicious()
    {
        // 201 notes within 800ms
        var beatmap = decodeOsu(circles(201, 4.0));

        Assert.Equal(SuspiciousBeatmapChecker.REASON_DENSITY, SuspiciousBeatmapChecker.Check(beatmap, 0));
    }

    [Fact]
    public void Check_Density_Taiko_UsesDoubleThreshold()
    {
        // 400 notes within 1s is below taiko's doubled threshold (401)
        var beatmap = decodeOsu(circles(400, 2.5));

        Assert.Null(SuspiciousBeatmapChecker.Check(beatmap, 1));

        // 401 notes within 1s trips it
        var denseBeatmap = decodeOsu(circles(401, 2.0));
        Assert.Equal(SuspiciousBeatmapChecker.REASON_DENSITY, SuspiciousBeatmapChecker.Check(denseBeatmap, 1));
    }

    [Fact]
    public void Check_Density_Mania_ScalesWithKeyCount()
    {
        // CS 4 -> 2 keys per hand -> threshold 401 per 1s
        var beatmap = decodeOsu(circles(401, 2.0), mode: 3, circleSize: 4);

        Assert.Equal(SuspiciousBeatmapChecker.REASON_DENSITY, SuspiciousBeatmapChecker.Check(beatmap, 3));
    }

    [Fact]
    public void Check_Density_Mania_HighKeyCount_HasHigherThreshold()
    {
        // CS 7 -> 3 keys per hand -> threshold 601 per 1s; 401 notes is not dense
        var beatmap = decodeOsu(circles(401, 2.0), mode: 3, circleSize: 7);

        Assert.Null(SuspiciousBeatmapChecker.Check(beatmap, 3));
    }

    [Fact]
    public void Check_SliderRepeats_AreSuspicious()
    {
        var beatmap = decodeOsu(sliders(129, slides: 1002, x: 0));

        Assert.Equal(SuspiciousBeatmapChecker.REASON_SLIDER_REPEATS, SuspiciousBeatmapChecker.Check(beatmap, 0));
    }

    [Fact]
    public void Check_SliderPositions_AreSuspicious()
    {
        var beatmap = decodeOsu(sliders(129, slides: 1, x: 10_001));

        Assert.Equal(SuspiciousBeatmapChecker.REASON_SLIDER_POSITIONS, SuspiciousBeatmapChecker.Check(beatmap, 0));
    }

    [Fact]
    public void Check_SliderRepeatsBelowCutoff_AreNotSuspicious()
    {
        var beatmap = decodeOsu(sliders(128, slides: 1002, x: 0));

        Assert.Null(SuspiciousBeatmapChecker.Check(beatmap, 0));
    }

    [Fact]
    public void Check_SliderRepeatsAndPosition_IsRedFlag()
    {
        var beatmap = decodeOsu(sliders(1, slides: 1002, x: 10_001));

        Assert.Equal(SuspiciousBeatmapChecker.REASON_RED_FLAG, SuspiciousBeatmapChecker.Check(beatmap, 0));
    }

    [Fact]
    public void Check_Catch_IgnoresDensity()
    {
        // Catch only checks sliders, so dense notes alone are not suspicious
        var beatmap = decodeOsu(circles(201, 4.0));

        Assert.Null(SuspiciousBeatmapChecker.Check(beatmap, 2));
    }

    [Fact]
    public void Check_Catch_SliderPositions_AreSuspicious()
    {
        var beatmap = decodeOsu(sliders(129, slides: 1, x: 10_001));

        Assert.Equal(SuspiciousBeatmapChecker.REASON_SLIDER_POSITIONS, SuspiciousBeatmapChecker.Check(beatmap, 2));
    }

    [Fact]
    public void Check_Fixtures_AreNotSuspicious()
    {
        foreach (var (filename, rulesetId) in new[] { ("osu.osu", 0), ("taiko.osu", 1), ("catch.osu", 2), ("mania.osu", 3) })
        {
            var workingBeatmap = new MemoryWorkingBeatmap(File.ReadAllBytes($"fixtures/{filename}"));

            Assert.Null(SuspiciousBeatmapChecker.Check(workingBeatmap.Beatmap, rulesetId));
        }
    }
}
