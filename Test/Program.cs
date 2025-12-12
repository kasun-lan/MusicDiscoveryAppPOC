using System.Text.Json;


var x = await SearchMusicBrainzArtistIdAsync("Eminem");
var x1 = await GetMusicBrainzGenresAsync(x);

var sdfd = 0;


async Task<string?> SearchMusicBrainzArtistIdAsync(string artistName)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("YourApp/1.0");

    string url = $"https://musicbrainz.org/ws/2/artist/?query={Uri.EscapeDataString(artistName)}&fmt=json";

    var json = await client.GetStringAsync(url);
    var result = JsonSerializer.Deserialize<MusicBrainzArtistSearchResult>(json);

    return result?.artists?.FirstOrDefault()?.id;
}

 async Task<List<string>> GetMusicBrainzGenresAsync(string mbid)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("YourApp/1.0");

    string url = $"https://musicbrainz.org/ws/2/artist/{mbid}?inc=tags+genres&fmt=json";

    var json = await client.GetStringAsync(url);
    var details = JsonSerializer.Deserialize<MBArtistDetails>(json);

    var genres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (details?.genres != null)
    {
        foreach (var g in details.genres)
            if (!string.IsNullOrWhiteSpace(g.name))
                genres.Add(g.name);
    }

    if (details?.tags != null)
    {
        foreach (var t in details.tags)
            if (!string.IsNullOrWhiteSpace(t.name))
                genres.Add(t.name);
    }

    return genres.ToList();
}

public class MBArtistDetails
{
    public List<MBTag>? tags { get; set; }
    public List<MBTag>? genres { get; set; }
}

public class MBTag
{
    public string? name { get; set; }
}



public class MusicBrainzArtistSearchResult
{
    public List<MBArtist>? artists { get; set; }
}

public class MBArtist
{
    public string? id { get; set; }
    public string? name { get; set; }
}
