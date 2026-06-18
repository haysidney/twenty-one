using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;

namespace TwentyOne.Game;

/// <summary>
/// Clears every <see cref="JsonExtensionDataAttribute"/> dictionary reachable from
/// a config object graph.
///
/// Forward-compat (downgrade safety) keeps unknown JSON fields in
/// <c>[JsonExtensionData]</c> dictionaries so a config written by a newer plugin
/// survives a round-trip through an older one. The catch: that machinery cannot
/// tell a genuine future field from an <em>orphan</em> - a field that used to be a
/// real property but was renamed, removed, or became <c>[JsonIgnore]</c>. Orphans
/// get captured and re-emitted forever, which is what ballooned a config to ~1 GB.
///
/// The fix is a single rule applied at load (see Plugin): unknown fields are only
/// meaningful when the config comes from a <em>future</em> schema version; for any
/// config at or below the current version, every captured key is provably an
/// orphan, so we clear them all. This also subsumes the old "removals need a
/// migration step" rule - a removed field's key just drops on the next load.
///
/// Safe by construction: it only clears <c>[JsonExtensionData]</c> dictionaries,
/// which by definition hold unknown keys and never real typed fields, so it can
/// never lose actual config data.
/// </summary>
public static class ExtensionDataCleaner
{
    public static void ClearAll(object? root) =>
        Walk(root, new HashSet<object>(ReferenceEqualityComparer.Instance));

    private static void Walk(object? obj, HashSet<object> seen)
    {
        if (obj is null) return;

        // Collections: recurse into elements regardless of where they're declared.
        if (obj is IDictionary dict)
        {
            if (!seen.Add(obj)) return;
            foreach (var v in dict.Values) Walk(v, seen);
            return;
        }
        if (obj is IEnumerable seq and not string)
        {
            if (!seen.Add(obj)) return;
            foreach (var v in seq) Walk(v, seen);
            return;
        }

        var type = obj.GetType();
        if (type.IsPrimitive || type.IsEnum || obj is string or decimal) return;
        // Only descend into our own model types; never walk Dalamud/system graphs.
        if (type.Namespace is null || !type.Namespace.StartsWith("TwentyOne")) return;
        if (!seen.Add(obj)) return;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0 || !prop.CanRead) continue;
            // [JsonIgnore] proxies are computed views of real properties; skip them
            // (their targets are reached via the real members) so we never invoke a
            // getter that could throw or recurse redundantly.
            if (prop.IsDefined(typeof(JsonIgnoreAttribute), inherit: true)) continue;

            if (prop.IsDefined(typeof(JsonExtensionDataAttribute), inherit: true))
            {
                if (prop.GetValue(obj) is IDictionary ed) ed.Clear();
                continue;
            }

            Walk(prop.GetValue(obj), seen);
        }
    }
}
