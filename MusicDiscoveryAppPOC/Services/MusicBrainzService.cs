using MusicDiscoveryAppPOC.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicDiscoveryAppPOC.Services
{
    public class MusicBrainzService
    {
        private readonly HttpClient _client;

        // ============================
        // Rate limiting (1 req / sec)
        // ============================
        private readonly SemaphoreSlim _rateLock = new(1, 1);
        private DateTime _lastRequestUtc = DateTime.MinValue;
        private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

        private static readonly HashSet<string> KnownGenres = new(StringComparer.OrdinalIgnoreCase)
        {
            "hip hop", "hip-hop", "rap", "pop rap", "trap", "boom bap",
            "rock", "pop", "metal", "jazz", "soul", "blues", "electronic",
            "indie", "alternative", "classical", "r&b", "punk", "house",
            "techno", "folk", "country"
        };

        public MusicBrainzService(HttpClient? httpClient = null)
        {
            // 🔴 CRITICAL: Force TLS 1.2 (fixes EOF / SSL failures on Windows)
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            _client = httpClient ?? new HttpClient();

            // 🔴 REQUIRED by MusicBrainz
            _client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "MusicDiscoveryAppPOC/1.0 (contact: nadeeshancooray@gmail.com)");

            _client.DefaultRequestVersion = HttpVersion.Version11;
            _client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;


            _client.Timeout = TimeSpan.FromSeconds(30);
        }

        // ============================
        // Rate-limited HTTP helper
        // ============================
        private async Task<string> GetStringRateLimitedAsync(string url)
        {
            await _rateLock.WaitAsync();
            try
            {
                var elapsed = DateTime.UtcNow - _lastRequestUtc;
                if (elapsed < MinInterval)
                    await Task.Delay(MinInterval - elapsed);

                _lastRequestUtc = DateTime.UtcNow;

                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    try
                    {
                        using var response = await _client.GetAsync(url);
                        response.EnsureSuccessStatusCode();
                        return await response.Content.ReadAsStringAsync();
                    }
                    catch (HttpRequestException) when (attempt == 1)
                    {
                        await Task.Delay(500); // short backoff
                    }
                }

                throw new HttpRequestException("MusicBrainz request failed after retry.");
            }
            finally
            {
                _rateLock.Release();
            }
        }


        // ============================
        // Public API
        // ============================
        public async Task<List<string>> GetGenresAsync(string artistName)
        {
            var mbid = await SearchArtistIdAsync(artistName);
            if (mbid == null)
                return new List<string>();

            return await GetGenresByIdAsync(mbid);
        }

        // ============================
        // Artist lookup
        // ============================
        private async Task<string?> SearchArtistIdAsync(string artistName)
        {
            var url =
                $"https://musicbrainz.org/ws/2/artist/" +
                $"?query={Uri.EscapeDataString(artistName)}&limit=1&fmt=json";

            var json = await GetStringRateLimitedAsync(url);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("artists", out var artists) ||
                artists.GetArrayLength() == 0)
                return null;

            return artists[0].GetProperty("id").GetString();
        }

        // ============================
        // Genre extraction
        // ============================
        private async Task<List<string>> GetGenresByIdAsync(string mbid)
        {
            var url =
                $"https://musicbrainz.org/ws/2/artist/{mbid}" +
                $"?inc=tags+genres&fmt=json";

            var json = await GetStringRateLimitedAsync(url);
            using var doc = JsonDocument.Parse(json);

            var genres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Official genres
            if (doc.RootElement.TryGetProperty("genres", out var genreArray))
            {
                foreach (var g in genreArray.EnumerateArray())
                {
                    var name = g.GetProperty("name").GetString();
                    if (!string.IsNullOrWhiteSpace(name) &&
                        KnownGenres.Contains(name))
                    {
                        genres.Add(name);
                    }
                }
            }

            // Fallback to tags
            if (genres.Count == 0 &&
                doc.RootElement.TryGetProperty("tags", out var tagArray))
            {
                foreach (var t in tagArray.EnumerateArray())
                {
                    var name = t.GetProperty("name").GetString();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (KnownGenres.Any(kg =>
                        name.Contains(kg, StringComparison.OrdinalIgnoreCase)))
                    {
                        genres.Add(name);
                    }
                }
            }

            return genres.ToList();
        }

        // ============================
        // Optional: recordings / tracks
        // ============================
        public async Task<List<TrackInfo>> GetMusicBrainzArtistTracksAsync(
            string artistMbid,
            int limit = 3,
            CancellationToken cancellationToken = default)
        {
            var url =
                $"https://musicbrainz.org/ws/2/recording" +
                $"?artist={artistMbid}&limit={limit}&fmt=json";

            await _rateLock.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                var elapsed = now - _lastRequestUtc;
                if (elapsed < MinInterval)
                    await Task.Delay(MinInterval - elapsed, cancellationToken);

                _lastRequestUtc = DateTime.UtcNow;

                using var response = await _client.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                using var stream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);
                using var json =
                    await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                var tracks = new List<TrackInfo>();

                if (!json.RootElement.TryGetProperty("recordings", out var recordings))
                    return tracks;

                foreach (var item in recordings.EnumerateArray())
                    tracks.Add(ParseRecording(item));

                return tracks;
            }
            finally
            {
                _rateLock.Release();
            }
        }

        private TrackInfo ParseRecording(JsonElement item)
        {
            var track = new TrackInfo
            {
                Id = item.GetProperty("id").GetString() ?? string.Empty,
                Name = item.GetProperty("title").GetString() ?? string.Empty,
                Popularity = 0,
                PreviewUrl = null,
                ImageUrl = null,
                ExternalUrl =
                    $"https://musicbrainz.org/recording/{item.GetProperty("id").GetString()}"
            };

            if (item.TryGetProperty("artist-credit", out var artistCredit) &&
                artistCredit.GetArrayLength() > 0)
            {
                track.ArtistName =
                    artistCredit[0].GetProperty("name").GetString() ?? string.Empty;
            }

            if (item.TryGetProperty("releases", out var releases) &&
                releases.GetArrayLength() > 0)
            {
                track.AlbumName =
                    releases[0].GetProperty("title").GetString() ?? string.Empty;
            }

            track.Metadata["source"] = "MusicBrainz";
            return track;
        }
    }
}
