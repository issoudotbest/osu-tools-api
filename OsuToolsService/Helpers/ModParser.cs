using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using osu.Framework.Bindables;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using OsuToolsService.Grpc;

namespace OsuToolsService.Helpers;

/// <summary>
/// Parses mod acronyms and settings into Mod objects.
/// </summary>
public static class ModParser
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, PropertyInfo>> ModPropertiesCache = new();

    /// <summary>
    /// Parses mods (acronym + optional settings) into Mod objects.
    /// Unknown acronyms are silently skipped.
    /// Setting keys are matched case-insensitively and ignoring underscores (e.g. "speed_change" matches "SpeedChange").
    /// </summary>
    public static osu.Game.Rulesets.Mods.Mod[] ParseMods(Ruleset ruleset, IEnumerable<Grpc.Mod>? grpcMods)
    {
        if (grpcMods == null)
            return [];

        var mods = new List<osu.Game.Rulesets.Mods.Mod>();

        foreach (var grpcMod in grpcMods)
        {
            var mod = ruleset.CreateModFromAcronym(grpcMod.Acronym);
            if (mod == null)
                continue;

            var properties = ModPropertiesCache.GetOrAdd(
                mod.GetType(),
                type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.GetGetMethod(false) is not null)
                    .GroupBy(p => NormalizeKey(p.Name), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            );

            foreach (var setting in grpcMod.Settings)
            {
                var normalizedKey = NormalizeKey(setting.Key);
                if (!properties.TryGetValue(normalizedKey, out var property))
                    continue;

                if (property.GetValue(mod) is IParseable parseable)
                    parseable.Parse(setting.Value, CultureInfo.InvariantCulture);
            }

            mods.Add(mod);
        }

        return mods.ToArray();
    }

    private static string NormalizeKey(string key) => key.Replace("_", "").ToLowerInvariant();
}
