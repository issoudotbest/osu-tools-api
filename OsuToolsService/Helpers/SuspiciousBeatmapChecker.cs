using System;
using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace OsuToolsService.Helpers;

/// <summary>
/// Heuristic that flags beatmaps crafted to hang or crash difficulty/performance calculators,
/// ported from rosu-pp's <c>TooSuspicious</c> check. Flagged maps must not be passed to the calculators.
/// </summary>
public static class SuspiciousBeatmapChecker
{
    public const string REASON_OBJECT_COUNT = "object_count";
    public const string REASON_LENGTH = "length";
    public const string REASON_DENSITY = "density";
    public const string REASON_RED_FLAG = "red_flag";
    public const string REASON_SLIDER_POSITIONS = "slider_positions";
    public const string REASON_SLIDER_REPEATS = "slider_repeats";

    private const int OBJECT_COUNT_THRESHOLD = 500_000;
    // Taiko calculation is especially expensive for high object counts
    private const int OBJECT_COUNT_THRESHOLD_TAIKO = 30_000;
    private const double DAY_MS = 24 * 60 * 60 * 1000.0;

    // 200 notes per 1s = 12000 BPM
    private const int DENSITY_THRESHOLD_1S = 200;
    // 500 notes per 10s = 3000 BPM
    private const int DENSITY_THRESHOLD_10S = 500;

    // osu!'s max position value is 131_072 and the playfield is 512x384
    private const float POSITION_THRESHOLD = 10_000f;
    // osu!'s max repeat value is 9000
    private const int REPEAT_THRESHOLD = 1000;
    private const int SLIDER_CUTOFF = 128;

    /// <summary>
    /// Checks the unmodded beatmap against the ruleset it will be calculated with.
    /// Expects hit objects sorted by start time, which the decoders guarantee.
    /// </summary>
    /// <returns>The reason the beatmap is suspicious, or <c>null</c> if it is safe to calculate.</returns>
    public static string? Check(IBeatmap beatmap, int rulesetId)
    {
        var hitObjects = beatmap.HitObjects;

        if (hitObjects.Count > (rulesetId == 1 ? OBJECT_COUNT_THRESHOLD_TAIKO : OBJECT_COUNT_THRESHOLD))
            return REASON_OBJECT_COUNT;

        if (hitObjects.Count >= 2 && hitObjects[^1].StartTime - hitObjects[0].StartTime > DAY_MS)
            return REASON_LENGTH;

        return rulesetId switch
        {
            0 => checkOsu(hitObjects),
            1 => checkDensity(hitObjects, DENSITY_THRESHOLD_1S * 2, DENSITY_THRESHOLD_10S * 2),
            2 => checkSliders(hitObjects),
            3 => checkMania(beatmap, hitObjects),
            _ => null
        };
    }

    private static string? checkOsu(IReadOnlyList<HitObject> hitObjects)
    {
        int repeatsBeyondThreshold = 0, posBeyondThreshold = 0;

        for (int i = 0; i < hitObjects.Count; i++)
        {
            if (tooDense(hitObjects, i, DENSITY_THRESHOLD_1S, DENSITY_THRESHOLD_10S))
                return REASON_DENSITY;

            if (suspiciousSlider(hitObjects[i], ref repeatsBeyondThreshold, ref posBeyondThreshold))
                return REASON_RED_FLAG;
        }

        return evalSliders(posBeyondThreshold, repeatsBeyondThreshold);
    }

    private static string? checkDensity(IReadOnlyList<HitObject> hitObjects, int per1s, int per10s)
    {
        for (int i = 0; i < hitObjects.Count; i++)
        {
            if (tooDense(hitObjects, i, per1s, per10s))
                return REASON_DENSITY;
        }

        return null;
    }

    private static string? checkMania(IBeatmap beatmap, IReadOnlyList<HitObject> hitObjects)
    {
        int keysPerHand = Math.Max(1, (int)beatmap.BeatmapInfo.Difficulty.CircleSize / 2);
        return checkDensity(hitObjects, DENSITY_THRESHOLD_1S * keysPerHand, DENSITY_THRESHOLD_10S * keysPerHand);
    }

    private static string? checkSliders(IReadOnlyList<HitObject> hitObjects)
    {
        int repeatsBeyondThreshold = 0, posBeyondThreshold = 0;

        foreach (var h in hitObjects)
        {
            if (suspiciousSlider(h, ref repeatsBeyondThreshold, ref posBeyondThreshold))
                return REASON_RED_FLAG;
        }

        return evalSliders(posBeyondThreshold, repeatsBeyondThreshold);
    }

    private static bool tooDense(IReadOnlyList<HitObject> hitObjects, int i, int per1s, int per10s)
        => (hitObjects.Count > i + per1s && hitObjects[i + per1s].StartTime - hitObjects[i].StartTime < 1000.0)
           || (hitObjects.Count > i + per10s && hitObjects[i + per10s].StartTime - hitObjects[i].StartTime < 10_000.0);

    private static bool suspiciousSlider(HitObject h, ref int repeatsBeyondThreshold, ref int posBeyondThreshold)
    {
        if (h is not IHasPath)
            return false;

        if (h is IHasRepeats hasRepeats && hasRepeats.RepeatCount > REPEAT_THRESHOLD)
        {
            if (positionBeyondThreshold(h))
                return true; // immediate red flag

            repeatsBeyondThreshold++;
        }
        else if (positionBeyondThreshold(h))
        {
            posBeyondThreshold++;
        }

        return false;
    }

    private static bool positionBeyondThreshold(HitObject h)
    {
        if (h is IHasPosition p)
            return Math.Abs(p.Position.X) > POSITION_THRESHOLD || Math.Abs(p.Position.Y) > POSITION_THRESHOLD;

        return h is IHasXPosition x && Math.Abs(x.X) > POSITION_THRESHOLD;
    }

    private static string? evalSliders(int posBeyondThreshold, int repeatsBeyondThreshold)
    {
        if (posBeyondThreshold > SLIDER_CUTOFF)
            return REASON_SLIDER_POSITIONS;

        if (repeatsBeyondThreshold > SLIDER_CUTOFF)
            return REASON_SLIDER_REPEATS;

        return null;
    }
}
