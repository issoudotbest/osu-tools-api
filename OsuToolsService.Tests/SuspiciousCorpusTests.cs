using System.IO;
using osu.Framework.Logging;
using OsuToolsService.Helpers;
using Xunit;

namespace OsuToolsService.Tests;

/// <summary>
/// Real beatmaps from sets flagged by rosu-pp's suspicious-beatmap heuristic, kept as
/// real-world anchors on top of the synthetic decoder-path cases in <see cref="SuspiciousBeatmapCheckerTests"/>
/// </summary>
public class SuspiciousCorpusTests
{
    public SuspiciousCorpusTests()
    {
        Logger.Enabled = false;
        osu.Game.Beatmaps.Formats.LegacyDifficultyCalculatorBeatmapDecoder.Register();
    }

    private static string? check(string filename, int rulesetId)
    {
        var workingBeatmap = new MemoryWorkingBeatmap(File.ReadAllBytes($"fixtures/{filename}"));
        return SuspiciousBeatmapChecker.Check(workingBeatmap.Beatmap, rulesetId);
    }

    [Theory]
    [InlineData("suspicious_density_150945.osu", 0, SuspiciousBeatmapChecker.REASON_DENSITY)]
    [InlineData("suspicious_object_count_2529936.osu", 0, SuspiciousBeatmapChecker.REASON_DENSITY)]
    [InlineData("suspicious_object_count_2529936.osu", 1, SuspiciousBeatmapChecker.REASON_OBJECT_COUNT)]
    [InlineData("suspicious_object_count_2529936.osu", 3, SuspiciousBeatmapChecker.REASON_DENSITY)]
    public void RealFlagged_Fixtures_ProduceExpectedReason(string filename, int rulesetId, string expectedReason)
    {
        Assert.Equal(expectedReason, check(filename, rulesetId));
    }

    [Theory]
    [InlineData("suspicious_density_150945.osu", 1)]
    [InlineData("suspicious_density_150945.osu", 2)]
    [InlineData("suspicious_density_150945.osu", 3)]
    [InlineData("suspicious_object_count_2529936.osu", 2)]
    public void RealFlagged_Fixtures_AreCleanForOtherRulesets(string filename, int rulesetId)
    {
        Assert.Null(check(filename, rulesetId));
    }
}
