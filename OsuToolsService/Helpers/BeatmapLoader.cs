using System;
using System.IO;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Skinning;
using osu.Game.Rulesets;

namespace OsuToolsService.Helpers;

/// <summary>
/// A WorkingBeatmap that reads from raw .osu file bytes (in memory).
/// </summary>
public class MemoryWorkingBeatmap : WorkingBeatmap
{
    private readonly Beatmap beatmap;

    public MemoryWorkingBeatmap(byte[] data, int? beatmapId = null)
        : this(decodeBeatmap(data), beatmapId)
    {
    }

    private MemoryWorkingBeatmap(Beatmap beatmap, int? beatmapId = null)
        : base(beatmap.BeatmapInfo, null)
    {
        this.beatmap = beatmap;

        // Ensure the ruleset is properly resolved
        beatmap.BeatmapInfo.Ruleset = getRulesetFromLegacyID(beatmap.BeatmapInfo.Ruleset.OnlineID).RulesetInfo;

        if (beatmapId.HasValue)
            beatmap.BeatmapInfo.OnlineID = beatmapId.Value;
    }

    private static Beatmap decodeBeatmap(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new LineBufferedReader(stream);
        return Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
    }

    private static Ruleset getRulesetFromLegacyID(int id)
    {
        return id switch
        {
            0 => new osu.Game.Rulesets.Osu.OsuRuleset(),
            1 => new osu.Game.Rulesets.Taiko.TaikoRuleset(),
            2 => new osu.Game.Rulesets.Catch.CatchRuleset(),
            3 => new osu.Game.Rulesets.Mania.ManiaRuleset(),
            _ => throw new ArgumentException($"Invalid ruleset ID: {id}")
        };
    }

    protected override IBeatmap GetBeatmap() => beatmap;
    public override Texture GetBackground() => throw new NotImplementedException();
    protected override Track GetBeatmapTrack() => throw new NotImplementedException();
    protected override ISkin GetSkin() => throw new NotImplementedException();
    public override Stream GetStream(string storagePath) => throw new NotImplementedException();
}
