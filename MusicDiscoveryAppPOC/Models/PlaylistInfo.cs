using System;
using System.Collections.Generic;

namespace MusicDiscoveryAppPOC.Models;

public class PlaylistInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<TrackInfo> Tracks { get; set; } = new();
}

