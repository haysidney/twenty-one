using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TwentyOne.Game;

namespace TwentyOne;

/// Persists archived <see cref="PlayerStatsSession"/> entries as one JSON file
/// per session under <c>{ConfigDirectory}/sessions/{venueId}/</c>. Sessions are
/// no longer stored in the main plugin config so disk growth scales gracefully
/// with history and individual sessions can be inspected, exported, or deleted
/// without touching the rest of the config.
public static class SessionStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting       = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };

    private static string Root => Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "sessions");

    private static string VenueFolder(Guid venueId) =>
        Path.Combine(Root, venueId.ToString("N"));

    private static string FilePath(Guid venueId, PlayerStatsSession session)
    {
        var dateStamp = session.Date.ToString("yyyy-MM-dd_HHmmss");
        var idStamp   = session.Id.ToString("N")[..8];
        return Path.Combine(VenueFolder(venueId), $"{dateStamp}-{idStamp}.json");
    }

    /// Loads every persisted session for the given venue, ordered by Date.
    /// Corrupt files are skipped (logged) rather than aborting the load.
    public static List<PlayerStatsSession> LoadAll(Guid venueId)
    {
        var folder = VenueFolder(venueId);
        if (!Directory.Exists(folder)) return [];

        var sessions = new List<PlayerStatsSession>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                var text = File.ReadAllText(path);
                var s    = JsonConvert.DeserializeObject<PlayerStatsSession>(text, JsonSettings);
                if (s != null) sessions.Add(s);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[SessionStore] Failed to load {path}: {ex.Message}");
            }
        }
        return sessions.OrderBy(s => s.Date).ToList();
    }

    /// Writes one session to disk, creating the venue folder if needed.
    public static void Save(Guid venueId, PlayerStatsSession session)
    {
        var folder = VenueFolder(venueId);
        Directory.CreateDirectory(folder);
        var path = FilePath(venueId, session);
        var json = JsonConvert.SerializeObject(session, JsonSettings);
        File.WriteAllText(path, json);
    }

    /// Deletes the file backing a session. No-op if the file is missing.
    public static void Delete(Guid venueId, PlayerStatsSession session)
    {
        var path = FilePath(venueId, session);
        if (File.Exists(path)) File.Delete(path);
    }
}
