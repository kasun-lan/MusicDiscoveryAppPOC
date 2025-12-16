using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MusicDiscoveryAppPOC.Models;

namespace MusicDiscoveryAppPOC.Services;

public class DeezerService : IDisposable
{
    private const string ApiBaseUrl = "https://api.deezer.com";
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public DeezerService(HttpMessageHandler? handler = null)
    {
        _httpClient = handler == null ? new HttpClient() : new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<List<ArtistInfo>> GetSimilarArtistsByNameAsync(string artistName, CancellationToken cancellationToken = default)
    {
        var deezerArtist = await FindArtistByNameAsync(artistName, cancellationToken).ConfigureAwait(false);
        if (deezerArtist is null)
        {
            return new List<ArtistInfo>();
        }

        return await GetSimilarArtistsByIdAsync(deezerArtist.DeezerId!, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ArtistInfo?> FindArtistByNameAsync(string artistName, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"{ApiBaseUrl}/search/artist?q={Uri.EscapeDataString(artistName)}&limit=1", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!json.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
        {
            return null;
        }

        var item = data[0];
        return ParseArtist(item, "Search");
    }

    private async Task<List<ArtistInfo>> GetSimilarArtistsByIdAsync(string deezerArtistId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"{ApiBaseUrl}/artist/{deezerArtistId}/related?limit=7", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new List<ArtistInfo>();
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!json.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
        {
            return new List<ArtistInfo>();
        }

        return data.EnumerateArray()
                   .Select(item => ParseArtist(item, "Related"))
                   .ToList();
    }

    private static ArtistInfo ParseArtist(JsonElement item, string source)
    {
        var artist = new ArtistInfo
        {
            Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            DeezerId = item.TryGetProperty("id", out var id) ? id.GetInt32().ToString() : null,
            Source = $"Deezer:{source}"
        };

        if (item.TryGetProperty("picture_medium", out var picture))
        {
            artist.ImageUrl = picture.GetString();
        }

        if (item.TryGetProperty("nb_fan", out var fans))
        {
            artist.Metadata["deezer_fans"] = fans.GetInt32().ToString();
        }

        return artist;
    }


    public async Task<List<TrackInfo>> GetDeezerArtistTracksAsync(
    string artistId,
    int limit = 100,
    CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();

        var url = $"https://api.deezer.com/artist/{artistId}/top?limit={limit}";

        using var response = await httpClient
            .GetAsync(url, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using var json = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var tracks = new List<TrackInfo>();

        foreach (var item in json.RootElement
                                 .GetProperty("data")
                                 .EnumerateArray())
        {
            tracks.Add(ParseDeezerTrack(item));
        }

        return tracks;
    }



    private TrackInfo ParseDeezerTrack(JsonElement item)
    {
        var track = new TrackInfo
        {
            Id = item.GetProperty("id").GetInt64().ToString(),
            Name = item.GetProperty("title").GetString() ?? string.Empty,
            ArtistName = item.GetProperty("artist")
                             .GetProperty("name")
                             .GetString() ?? string.Empty,
            AlbumName = item.GetProperty("album")
                            .GetProperty("title")
                            .GetString() ?? string.Empty,
            PreviewUrl = item.TryGetProperty("preview", out var preview)
                ? preview.GetString()
                : null,
            ExternalUrl = item.TryGetProperty("link", out var link)
                ? link.GetString()
                : null,

            // Deezer does not expose a Spotify-style popularity metric
            Popularity = 0
        };

        // Image (prefer album cover)
        if (item.GetProperty("album").TryGetProperty("cover_medium", out var cover))
        {
            track.ImageUrl = cover.GetString();
        }

        // Optional metadata — keep Deezer-specific facts here
        track.Metadata["source"] = "deezer";
        track.Metadata["rank"] = item.TryGetProperty("rank", out var rank)
            ? rank.GetInt32().ToString()
            : null;
        track.Metadata["explicit"] = item.TryGetProperty("explicit_lyrics", out var explicitLyrics)
            ? explicitLyrics.GetBoolean().ToString()
            : null;

        return track;
    }



    public async Task<string?> GetTrackPreviewUrlAsync(string trackName, string artistName, CancellationToken cancellationToken = default)
    {
        var query = $"{artistName} {trackName}";
        using var response = await _httpClient.GetAsync($"{ApiBaseUrl}/search/track?q={Uri.EscapeDataString(query)}&limit=1", cancellationToken).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!json.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
        {
            return null;
        }

        var track = data[0];
        if (track.TryGetProperty("preview", out var preview) && preview.ValueKind == JsonValueKind.String)
        {
            var previewUrl = preview.GetString();
            if (!string.IsNullOrWhiteSpace(previewUrl))
            {
                return previewUrl;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _httpClient.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

