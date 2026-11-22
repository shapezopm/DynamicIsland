using System.IO;
using Microsoft.Data.Sqlite;

namespace DynamicIsland;

internal sealed class TrackIdentityDatabase(string? directory = null) : ITrackIdentityCatalogue
{
    readonly string _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "TrackIdentity");

    public TrackIdentity? FindTrack(TrackSnapshot track)
    {
        try
        {
            using var connection = Open("tracks.sqlite");
            if (connection is null) return null;
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT title, artist, evidence FROM track_hints WHERE raw_title = $title AND raw_artist = $artist AND ($duration < 1 OR abs(duration - $duration) <= 2) ORDER BY (evidence = 'UserConfirmed') DESC LIMIT 8";
            command.Parameters.AddWithValue("$title", TrackIdentityParser.Key(track.Title));
            command.Parameters.AddWithValue("$artist", TrackIdentityParser.Key(track.Artist));
            command.Parameters.AddWithValue("$duration", track.Duration.CanMatch ? track.Duration.Value.TotalSeconds : 0);
            using var reader = command.ExecuteReader();
            var identities = new List<TrackIdentity>();
            while (reader.Read()) identities.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            var confirmed = identities.Where(item => item.Evidence == "UserConfirmed").ToList();
            if (confirmed.Count > 0) identities = confirmed;
            if (identities.Count >= 8) return null; // A row limit must not hide a competing identity at this authority.
            var unique = identities.DistinctBy(item => (TrackIdentityParser.Key(item.Title), TrackIdentityParser.Key(item.Artist))).ToArray();
            if (unique.Length != 1) return null;
            var hint = unique[0];
            var cleaned = TrackIdentityParser.Parse(track with { Title = hint.Title, Artist = hint.Artist }, this);
            return cleaned with { Evidence = hint.Evidence, RequiresDuration = !track.Duration.CanMatch };
        }
        catch (Exception) { return null; }
    }

    public string[] ArtistNames(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 512) return [];
        try
        {
            using var connection = Open("artists.sqlite");
            if (connection is null) return [];
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT artist_id FROM artist_names WHERE key = $key LIMIT 2";
            command.Parameters.AddWithValue("$key", TrackIdentityParser.Key(name));
            string? id = null;
            using (var reader = command.ExecuteReader())
            {
                if (reader.Read()) id = reader.GetString(0);
                if (reader.Read()) return []; // Homonymous artists must not be merged.
            }
            if (id is null) return [];
            command.Parameters.Clear();
            command.CommandText = "SELECT name FROM artist_names WHERE artist_id = $id ORDER BY is_primary DESC, name LIMIT 24";
            command.Parameters.AddWithValue("$id", id);
            using var names = command.ExecuteReader();
            var result = new List<string>();
            while (names.Read()) result.Add(names.GetString(0));
            return result.ToArray();
        }
        catch (Exception) { return []; }
    }

    SqliteConnection? Open(string name)
    {
        string path = Path.Combine(_directory, name);
        if (!File.Exists(path)) return null;
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1 }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }
}
