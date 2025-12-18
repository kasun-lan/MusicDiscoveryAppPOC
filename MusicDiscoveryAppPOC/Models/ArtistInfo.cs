using System.Collections.Generic;

namespace MusicDiscoveryAppPOC.Models;

public class ArtistInfo
{
    public string Name { get; set; } = string.Empty;
    public string? SpotifyId { get; set; }
    public string? DeezerId { get; set; }
    public string? ImageUrl { get; set; }
    public string? Source { get; set; }
    public int TopTrackCount { get; set; }

    public Dictionary<string, string?> Metadata { get; } = new();

    public List<TrackInfo> Tracks { get; set; } = new List<TrackInfo>();

    public override string ToString()
    {
        var core =
            $"{Name} , Tracks Count: {TopTrackCount}, Spotify Id: {SpotifyId}, Deezer Id : {DeezerId}";

        if (Metadata.Count == 0)
            return core;

        var metadataString = string.Join(
            ", ",
            Metadata.Select(kv => $"{kv.Key}: {kv.Value ?? "null"}")
        );

        return $"{core} | Metadata [{metadataString}]";

    }
}


