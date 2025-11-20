using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MusicDiscoveryAppPOC.Models;

namespace MusicDiscoveryAppPOC.Services;

public class SpotifyService : IDisposable
{
    private const string TokenEndpoint = "https://accounts.spotify.com/api/token";
    private const string ApiBaseUrl = "https://api.spotify.com/v1";

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly HttpClient _httpClient;

    private string? _accessToken;
    private DateTimeOffset _tokenExpiryUtc;
    private bool _disposed;

    public SpotifyService(string clientId, string clientSecret, HttpMessageHandler? handler = null)
    {
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _clientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
        _httpClient = handler == null ? new HttpClient() : new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<List<ArtistInfo>> SearchArtistsAsync(string query, CancellationToken cancellationToken = default)
    {
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/search?type=artist&q={Uri.EscapeDataString(query)}&limit=5");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var artists = new List<ArtistInfo>();
        var artistElements = json.RootElement.GetProperty("artists").GetProperty("items");
        foreach (var item in artistElements.EnumerateArray())
        {
            artists.Add(ParseArtist(item));
        }

        return artists;
    }

    public async Task<ArtistInfo?> GetArtistByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var results = await SearchArtistsAsync(name, cancellationToken).ConfigureAwait(false);
        return results.FirstOrDefault();
    }

    public async Task<List<TrackInfo>> GetTopTracksAsync(string artistId, string market = "US", CancellationToken cancellationToken = default)
    {
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/artists/{artistId}/top-tracks?market={market}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var tracks = new List<TrackInfo>();
        foreach (var item in json.RootElement.GetProperty("tracks").EnumerateArray())
        {
            tracks.Add(ParseTrack(item));
        }

        return tracks;
    }

    private ArtistInfo ParseArtist(JsonElement item)
    {
        var artist = new ArtistInfo
        {
            Name = item.GetProperty("name").GetString() ?? string.Empty,
            SpotifyId = item.GetProperty("id").GetString()
        };

        if (item.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
        {
            artist.ImageUrl = images[0].GetProperty("url").GetString();
        }

        if (item.TryGetProperty("genres", out var genres))
        {
            artist.Metadata["genres"] = string.Join(", ", genres.EnumerateArray().Select(g => g.GetString()));
        }

        if (item.TryGetProperty("followers", out var followers) && followers.TryGetProperty("total", out var total))
        {
            artist.Metadata["followers"] = total.GetInt64().ToString();
        }

        return artist;
    }

    private TrackInfo ParseTrack(JsonElement item)
    {
        var track = new TrackInfo
        {
            Id = item.GetProperty("id").GetString() ?? string.Empty,
            Name = item.GetProperty("name").GetString() ?? string.Empty,
            PreviewUrl = item.TryGetProperty("preview_url", out var preview) ? preview.GetString() : null,
            ExternalUrl = item.TryGetProperty("external_urls", out var urls) && urls.TryGetProperty("spotify", out var spotifyUrl) ? spotifyUrl.GetString() : null,
            Popularity = item.TryGetProperty("popularity", out var popularity) ? popularity.GetInt32() : 0
        };

        if (item.TryGetProperty("album", out var album))
        {
            track.AlbumName = album.TryGetProperty("name", out var albumName) ? albumName.GetString() ?? string.Empty : string.Empty;
            if (album.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
            {
                track.ImageUrl = images[0].GetProperty("url").GetString();
            }
        }

        if (item.TryGetProperty("artists", out var artists) && artists.GetArrayLength() > 0)
        {
            track.ArtistName = string.Join(", ", artists.EnumerateArray().Select(a => a.GetProperty("name").GetString()));
        }

        return track;
    }

    private async Task EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiryUtc)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        _accessToken = json.RootElement.GetProperty("access_token").GetString();
        var expiresIn = json.RootElement.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;
        _tokenExpiryUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);
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


