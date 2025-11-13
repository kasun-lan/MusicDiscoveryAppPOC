using System.Collections.Generic;

namespace MusicDiscoveryAppPOC.Models;

public class TrackInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public string AlbumName { get; set; } = string.Empty;
    public string? PreviewUrl { get; set; }
    public string? ExternalUrl { get; set; }
    public string? ImageUrl { get; set; }
    public int Popularity { get; set; }
    public Dictionary<string, string?> Metadata { get; } = new();
}


