using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using OsuToolsService.Grpc;
using OsuToolsService.Helpers;
using GrpcPerformanceAttributes = OsuToolsService.Grpc.PerformanceAttributes;

namespace OsuToolsService.Services;

public class CalculatorService : Calculator.CalculatorBase
{
    private readonly ILogger<CalculatorService> _logger;

    public CalculatorService(ILogger<CalculatorService> logger)
    {
        _logger = logger;
    }

    public override Task<DifficultyResponse> CalculateDifficulty(DifficultyRequest request, ServerCallContext context)
    {
        try
        {
            if (request.BeatmapData.IsEmpty)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Beatmap data is required."));

            var workingBeatmap = new MemoryWorkingBeatmap(request.BeatmapData.ToByteArray());
            var ruleset = getRuleset(request.RulesetId, workingBeatmap);
            var mods = ModParser.ParseMods(ruleset, request.Mods);

            // Refuse to calculate maps crafted to hang or crash the calculator
            var suspiciousReason = SuspiciousBeatmapChecker.Check(workingBeatmap.Beatmap, ruleset.RulesetInfo.OnlineID);
            if (suspiciousReason != null)
            {
                _logger.LogWarning("Beatmap flagged as suspicious ({Reason}); skipping difficulty calculation", suspiciousReason);
                return Task.FromResult(new DifficultyResponse
                {
                    IsSuspicious = true,
                    SuspiciousReason = suspiciousReason,
                    Error = ""
                });
            }

            var attributes = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(mods);

            // GetPlayableBeatmap applies IApplicableToDifficulty mods (HR, EZ, DA, etc.) to the beatmap's difficulty
            var playableBeatmap = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

            var (starRating, approachRate, overallDifficulty, circleSize, drainRate, maxCombo) =
                extractDifficultyAttributes(attributes, playableBeatmap, mods);
            var (bpm, baseBpm, length, drainLength) = computeBpmAndLength(workingBeatmap, mods);

            var attributesJson = serializeDifficultyAttributes(attributes);

            // Unmodded difficulty values come from the raw decoded beatmap, before any mods are applied
            var baseDifficulty = workingBeatmap.BeatmapInfo.Difficulty;

            var (largeBonusCount, smallBonusCount) = HitResultGenerator.CountSpinnerBonuses(playableBeatmap);

            return Task.FromResult(new DifficultyResponse
            {
                StarRating = starRating,
                ApproachRate = approachRate,
                OverallDifficulty = overallDifficulty,
                CircleSize = circleSize,
                DrainRate = drainRate,
                BaseApproachRate = baseDifficulty.ApproachRate,
                BaseOverallDifficulty = baseDifficulty.OverallDifficulty,
                BaseCircleSize = baseDifficulty.CircleSize,
                BaseDrainRate = baseDifficulty.DrainRate,
                MaxCombo = maxCombo,
                Bpm = bpm,
                BaseBpm = baseBpm,
                Length = length,
                DrainLength = drainLength,
                BreakCount = workingBeatmap.Beatmap.Breaks.Count,
                LargeTickCount = HitResultGenerator.CountLargeTicks(playableBeatmap),
                LargeBonusCount = largeBonusCount,
                SmallBonusCount = smallBonusCount,
                IsSuspicious = false,
                SuspiciousReason = "",
                AttributesJson = attributesJson,
                Error = ""
            });
        }
        catch (RpcException)
        {
            throw;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument in difficulty calculation");
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON in difficulty calculation");
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating difficulty");
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    public override Task<PerformanceResponse> CalculatePerformance(PerformanceRequest request, ServerCallContext context)
    {
        try
        {
            if (request.BeatmapData.IsEmpty)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Beatmap data is required."));

            var workingBeatmap = new MemoryWorkingBeatmap(request.BeatmapData.ToByteArray());
            var ruleset = getRuleset(request.RulesetId, workingBeatmap);
            var mods = ModParser.ParseMods(ruleset, request.Mods);

            // Refuse to calculate maps crafted to hang or crash the calculator
            var suspiciousReason = SuspiciousBeatmapChecker.Check(workingBeatmap.Beatmap, ruleset.RulesetInfo.OnlineID);
            if (suspiciousReason != null)
            {
                _logger.LogWarning("Beatmap flagged as suspicious ({Reason}); skipping performance calculation", suspiciousReason);
                return Task.FromResult(new PerformanceResponse
                {
                    Performance = 0,
                    Difficulty = new DifficultyResponse
                    {
                        IsSuspicious = true,
                        SuspiciousReason = suspiciousReason,
                        Error = ""
                    },
                    Score = new ScoreStatistics(),
                    PerformanceAttributes = new GrpcPerformanceAttributes { ExtraJson = "{}" },
                    Error = ""
                });
            }

            var playableBeatmap = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

            int beatmapMaxCombo = playableBeatmap.GetMaxCombo();

            var statistics = HitResultGenerator.Generate(
                ruleset,
                playableBeatmap,
                mods,
                request.Accuracy,
                request.Misses,
                request.Mehs > 0 ? request.Mehs : null,
                request.Goods > 0 ? request.Goods : null,
                request.Oks > 0 ? request.Oks : null,
                request.Greats > 0 ? request.Greats : null,
                request.LargeTickMisses,
                request.SliderTailMisses,
                request.TinyDroplets > 0 ? request.TinyDroplets : null,
                request.Droplets > 0 ? request.Droplets : null,
                request.LargeTickHits > 0 ? request.LargeTickHits : null,
                request.SliderTailHits > 0 ? request.SliderTailHits : null
            );

            var accuracy = HitResultGenerator.GetAccuracy(ruleset, playableBeatmap, statistics, mods);

            var combo = request.Combo > 0
                ? request.Combo
                : (int)Math.Round(request.PercentCombo / 100 * beatmapMaxCombo);

            var scoreInfo = new ScoreInfo(playableBeatmap.BeatmapInfo, ruleset.RulesetInfo)
            {
                Accuracy = accuracy,
                MaxCombo = combo,
                Statistics = statistics,
                LegacyTotalScore = request.LegacyTotalScore > 0 ? request.LegacyTotalScore : null,
                Mods = mods
            };

            // Use precalculated difficulty if provided, otherwise calculate it
            DifficultyAttributes difficultyAttributes;
            if (!string.IsNullOrEmpty(request.PrecalculatedDifficulty))
            {
                difficultyAttributes = deserializeDifficultyAttributes(request.PrecalculatedDifficulty, ruleset);
            }
            else
            {
                var difficultyCalculator = ruleset.CreateDifficultyCalculator(workingBeatmap);
                difficultyAttributes = difficultyCalculator.Calculate(mods);
            }

            var performanceCalculator = ruleset.CreatePerformanceCalculator();
            if (performanceCalculator == null)
                throw new RpcException(new Status(StatusCode.Unimplemented, "Performance calculation is not supported for this ruleset."));

            var performanceAttributes = performanceCalculator.Calculate(scoreInfo, difficultyAttributes);

            // Build difficulty response
            var (starRating, approachRate, overallDifficulty, circleSize, drainRate, maxCombo) =
                extractDifficultyAttributes(difficultyAttributes, playableBeatmap, mods);
            var (bpm, baseBpm, length, drainLength) = computeBpmAndLength(workingBeatmap, mods);

            // Unmodded difficulty values come from the raw decoded beatmap, before any mods are applied
            var baseDifficulty = workingBeatmap.BeatmapInfo.Difficulty;

            var (largeBonusCount, smallBonusCount) = HitResultGenerator.CountSpinnerBonuses(playableBeatmap);

            var difficultyResponse = new DifficultyResponse
            {
                StarRating = starRating,
                ApproachRate = approachRate,
                OverallDifficulty = overallDifficulty,
                CircleSize = circleSize,
                DrainRate = drainRate,
                BaseApproachRate = baseDifficulty.ApproachRate,
                BaseOverallDifficulty = baseDifficulty.OverallDifficulty,
                BaseCircleSize = baseDifficulty.CircleSize,
                BaseDrainRate = baseDifficulty.DrainRate,
                MaxCombo = maxCombo,
                Bpm = bpm,
                BaseBpm = baseBpm,
                Length = length,
                DrainLength = drainLength,
                BreakCount = workingBeatmap.Beatmap.Breaks.Count,
                LargeTickCount = HitResultGenerator.CountLargeTicks(playableBeatmap),
                LargeBonusCount = largeBonusCount,
                SmallBonusCount = smallBonusCount,
                IsSuspicious = false,
                SuspiciousReason = "",
                AttributesJson = serializeDifficultyAttributes(difficultyAttributes),
                Error = ""
            };

            // Build score statistics
            var scoreStats = new ScoreStatistics
            {
                RulesetId = ruleset.RulesetInfo.OnlineID,
                BeatmapId = workingBeatmap.BeatmapInfo.OnlineID,
                BeatmapName = workingBeatmap.BeatmapInfo.ToString() ?? "Unknown beatmap",
                Accuracy = scoreInfo.Accuracy * 100,
                Combo = combo
            };
            foreach (var stat in statistics)
            {
                scoreStats.Statistics[stat.Key.ToString().ToLowerInvariant()] = stat.Value;
            }

            // Build performance attributes
            var perfAttrs = new GrpcPerformanceAttributes();
            var perfJson = JsonConvert.SerializeObject(performanceAttributes);
            var perfDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(perfJson) ?? new Dictionary<string, object>();

            perfAttrs.Total = getDoubleValue(perfDict, "total", "pp");
            perfAttrs.Aim = getDoubleValue(perfDict, "aim");
            perfAttrs.Speed = getDoubleValue(perfDict, "speed");
            perfAttrs.AccuracyPp = getDoubleValue(perfDict, "accuracy", "accuracy_pp");
            perfAttrs.Flashlight = getDoubleValue(perfDict, "flashlight");
            perfAttrs.EffectiveMissCount = getDoubleValue(perfDict, "effective_miss_count", "effectiveMissCount");

            var keysToRemove = new[] { "total", "pp", "aim", "speed", "accuracy", "accuracy_pp", "flashlight", "effective_miss_count", "effectiveMissCount" };
            foreach (var key in keysToRemove)
                perfDict.Remove(key);

            perfAttrs.ExtraJson = perfDict.Count > 0 ? JsonConvert.SerializeObject(perfDict) : "{}";

            return Task.FromResult(new PerformanceResponse
            {
                Performance = perfAttrs.Total,
                Difficulty = difficultyResponse,
                Score = scoreStats,
                PerformanceAttributes = perfAttrs,
                Error = ""
            });
        }
        catch (RpcException)
        {
            throw;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument in performance calculation");
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON in performance calculation");
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating performance");
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    private static Ruleset getRuleset(int rulesetId, WorkingBeatmap beatmap)
    {
        if (rulesetId < 0)
        {
            // Auto-detect from beatmap
            rulesetId = beatmap.BeatmapInfo.Ruleset.OnlineID;
        }

        return rulesetId switch
        {
            0 => new osu.Game.Rulesets.Osu.OsuRuleset(),
            1 => new osu.Game.Rulesets.Taiko.TaikoRuleset(),
            2 => new osu.Game.Rulesets.Catch.CatchRuleset(),
            3 => new osu.Game.Rulesets.Mania.ManiaRuleset(),
            _ => throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid ruleset ID: {rulesetId}"))
        };
    }

    private static (double starRating, double approachRate, double overallDifficulty, double circleSize, double drainRate, long maxCombo)
        extractDifficultyAttributes(DifficultyAttributes attributes, IBeatmap playableBeatmap, osu.Game.Rulesets.Mods.Mod[] mods)
    {
        double starRating = attributes.StarRating;
        long maxCombo = attributes.MaxCombo;
        var difficulty = playableBeatmap.Difficulty;

        // Apply speed mods (DT, HT, etc.) to effective AR and OD using the framework's own conversion methods
        double clockRate = calculateClockRate(mods);

        double preempt = IBeatmapDifficultyInfo.DifficultyRange(difficulty.ApproachRate, 1800, 1200, 450);
        double adjustedPreempt = preempt / clockRate;
        double approachRate = IBeatmapDifficultyInfo.InverseDifficultyRange(adjustedPreempt, 1800, 1200, 450);

        double hitWindowGreat = IBeatmapDifficultyInfo.DifficultyRange(difficulty.OverallDifficulty, 80, 50, 20);
        double adjustedWindow = hitWindowGreat / clockRate;
        double overallDifficulty = IBeatmapDifficultyInfo.InverseDifficultyRange(adjustedWindow, 80, 50, 20);

        double circleSize = difficulty.CircleSize;
        double drainRate = difficulty.DrainRate;

        return (starRating, approachRate, overallDifficulty, circleSize, drainRate, maxCombo);
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

    private static (double bpm, double baseBpm, double length, double drainLength) computeBpmAndLength(WorkingBeatmap beatmap, osu.Game.Rulesets.Mods.Mod[] mods)
    {
        // BeatmapInfo.BPM is not populated when decoding from raw bytes, so compute from timing points
        double baseBpm = 0;
        var timingPoints = beatmap.Beatmap.ControlPointInfo.TimingPoints;
        if (timingPoints.Count > 0)
            baseBpm = 60000 / beatmap.Beatmap.GetMostCommonBeatLength();

        double rate = calculateClockRate(mods);

        double moddedBpm = baseBpm * rate;

        var playableBeatmap = beatmap.Beatmap;
        double moddedLength = playableBeatmap.CalculatePlayableLength() / rate / 1000.0;
        double moddedDrainLength = playableBeatmap.CalculateDrainLength() / rate / 1000.0;

        return (moddedBpm, baseBpm, moddedLength, moddedDrainLength);
    }

    private static double getDoubleValue(Dictionary<string, object> dict, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (dict.TryGetValue(key, out var value))
                return toDouble(value);
        }
        return 0;
    }

    private static double toDouble(object value)
    {
        return value switch
        {
            double d => d,
            long l => l,
            int i => i,
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => Convert.ToDouble(value)
        };
    }

    // DifficultyAttributes uses [JsonObject(MemberSerialization.OptIn)], which skips properties
    // without [JsonProperty] (e.g. HitCircleCount, SliderCount, SpinnerCount). These are needed
    // by performance calculators, so we override to serialize all public properties.
    private static readonly JsonSerializerSettings DifficultyJsonSettings = new JsonSerializerSettings
    {
        ContractResolver = new AllPropertiesContractResolver()
    };

    private static string serializeDifficultyAttributes(DifficultyAttributes attributes)
        => JsonConvert.SerializeObject(attributes, DifficultyJsonSettings);

    private static DifficultyAttributes deserializeDifficultyAttributes(string json, Ruleset ruleset)
    {
        Type? expectedType = ruleset.RulesetInfo.OnlineID switch
        {
            0 => typeof(osu.Game.Rulesets.Osu.Difficulty.OsuDifficultyAttributes),
            1 => typeof(osu.Game.Rulesets.Taiko.Difficulty.TaikoDifficultyAttributes),
            2 => typeof(osu.Game.Rulesets.Catch.Difficulty.CatchDifficultyAttributes),
            3 => typeof(osu.Game.Rulesets.Mania.Difficulty.ManiaDifficultyAttributes),
            _ => null
        };

        if (expectedType != null)
            return (DifficultyAttributes)JsonConvert.DeserializeObject(json, expectedType, DifficultyJsonSettings)!;

        throw new RpcException(new Status(StatusCode.InvalidArgument, "Could not determine difficulty attributes type from ruleset."));
    }
}

/// <summary>
/// Overrides [JsonObject(MemberSerialization.OptIn)] on DifficultyAttributes to serialize all public properties.
/// </summary>
public class AllPropertiesContractResolver : DefaultContractResolver
{
    protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
    {
        // Force OptOut so all public properties are included, ignoring the class-level OptIn
        return base.CreateProperties(type, MemberSerialization.OptOut);
    }
}
