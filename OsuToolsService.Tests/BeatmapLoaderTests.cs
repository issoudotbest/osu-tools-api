using System.IO;
using OsuToolsService.Helpers;
using Xunit;

namespace OsuToolsService.Tests;

public class BeatmapLoaderTests
{
    private static byte[] getBeatmapData(string filename)
    {
        return File.ReadAllBytes($"fixtures/{filename}");
    }

    [Fact]
    public void Parse_OsuFile_ReturnsBeatmap()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);

        Assert.NotNull(workingBeatmap);
        Assert.Equal("Haru", workingBeatmap.BeatmapInfo.Metadata.Title);
        Assert.Equal("Yorushika", workingBeatmap.BeatmapInfo.Metadata.Artist);
    }

    [Fact]
    public void Parse_OsuFile_DetectsCorrectRuleset()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);

        Assert.Equal(0, workingBeatmap.BeatmapInfo.Ruleset.OnlineID);
    }

    [Fact]
    public void Parse_TaikoFile_DetectsCorrectRuleset()
    {
        var data = getBeatmapData("taiko.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);

        Assert.Equal(1, workingBeatmap.BeatmapInfo.Ruleset.OnlineID);
    }

    [Fact]
    public void Parse_CatchFile_DetectsCorrectRuleset()
    {
        var data = getBeatmapData("catch.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);

        Assert.Equal(2, workingBeatmap.BeatmapInfo.Ruleset.OnlineID);
    }

    [Fact]
    public void Parse_ManiaFile_DetectsCorrectRuleset()
    {
        var data = getBeatmapData("mania.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);

        Assert.Equal(3, workingBeatmap.BeatmapInfo.Ruleset.OnlineID);
    }

    [Fact]
    public void Parse_WithBeatmapId_SetsOnlineId()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data, beatmapId: 42);

        Assert.Equal(42, workingBeatmap.BeatmapInfo.OnlineID);
    }

    [Fact]
    public void Parse_WithDifficultySettings_ReadsValues()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);

        Assert.Equal(4, workingBeatmap.BeatmapInfo.Difficulty.CircleSize);
        Assert.Equal(9.2, workingBeatmap.BeatmapInfo.Difficulty.OverallDifficulty, 1);
        Assert.Equal(9.6, workingBeatmap.BeatmapInfo.Difficulty.ApproachRate, 1);
        Assert.Equal(5.5, workingBeatmap.BeatmapInfo.Difficulty.DrainRate, 1);
    }

    [Fact]
    public void Parse_HitObjects_CountsCorrectly()
    {
        var data = getBeatmapData("osu.osu");
        var workingBeatmap = new MemoryWorkingBeatmap(data);
        var beatmap = workingBeatmap.Beatmap;

        Assert.True(beatmap.HitObjects.Count > 0);
    }
}
