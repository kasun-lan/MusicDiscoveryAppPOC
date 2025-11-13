using System.Collections.Generic;

namespace MusicDiscoveryAppPOC.Models;

public class ArtistInfo
{
    public string Name { get; set; } = string.Empty;
    public string? SpotifyId { get; set; }
    public string? DeezerId { get; set; }
    public string? ImageUrl { get; set; }
    public string? Source { get; set; }

    public Dictionary<string, string?> Metadata { get; } = new();
}


