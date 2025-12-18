using MusicDiscoveryAppPOC.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MusicDiscoveryAppPOC.Services
{
    public class MusicBrainzService
    {


        private readonly HttpClient _client;

        private static readonly HashSet<string> KnownGenres = new(StringComparer.OrdinalIgnoreCase)
        {
        "hip hop", "hip-hop", "rap", "pop rap", "trap", "boom bap",
        "rock", "pop", "metal", "jazz", "soul", "blues", "electronic",
        "indie", "alternative", "classical", "r&b", "punk", "house",
        "techno", "folk", "country"
        };

        public MusicBrainzService(HttpClient? httpClient = null)
        {
            _client = httpClient ?? new HttpClient();
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("MusicDiscoveryApp/1.0");
        }

        public async Task<List<string>> GetGenresAsync(string artistName)
        {
            var mbid = await SearchArtistIdAsync(artistName);
            if (mbid == null)
                return new List<string>();

            return await GetGenresByIdAsync(mbid);
        }

        private async Task<string?> SearchArtistIdAsync(string artistName)
        {
            var url = $"https://musicbrainz.org/ws/2/artist/?query={Uri.EscapeDataString(artistName)}&fmt=json";
            var json = await _client.GetStringAsync(url);

            using var doc = JsonDocument.Parse(json);

            var artists = doc.RootElement.GetProperty("artists");
            if (artists.GetArrayLength() == 0)
                return null;

            return artists[0].GetProperty("id").GetString();
        }

        private async Task<List<string>> GetGenresByIdAsync(string mbid)
        {
            var url = $"https://musicbrainz.org/ws/2/artist/{mbid}?inc=tags+genres&fmt=json";
            var json = await _client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            var genres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Official MusicBrainz genres
            if (doc.RootElement.TryGetProperty("genres", out var genreArray))
            {
                foreach (var g in genreArray.EnumerateArray())
                {
                    var name = g.GetProperty("name").GetString();
                    if (!string.IsNullOrWhiteSpace(name) && KnownGenres.Contains(name))
                        genres.Add(name);
                }
            }

            // Crowdsourced tags (fall back)
            if (genres.Count == 0 && doc.RootElement.TryGetProperty("tags", out var tagArray))
            {
                foreach (var t in tagArray.EnumerateArray())
                {
                    var name = t.GetProperty("name").GetString();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // partial match for flexibility
                    if (KnownGenres.Any(kg => name.Contains(kg, StringComparison.OrdinalIgnoreCase)))
                        genres.Add(name);
                }
            }

            return genres.ToList();
        }

        public async Task<string?> GetMusicBrainzArtistIdAsync(
    string artistName,
    CancellationToken cancellationToken = default)
        {
            using var httpClient = new HttpClient();

            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "YourAppName/1.0 (your-email@example.com)");

            var query = Uri.EscapeDataString($"artist:\"{artistName}\"");

            var url =
                $"https://musicbrainz.org/ws/2/artist" +
                $"?query={query}" +
                $"&limit=1" +
                $"&fmt=json";

            using var response = await httpClient
                .GetAsync(url, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            using var json = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!json.RootElement.TryGetProperty("artists", out var artists) ||
                artists.GetArrayLength() == 0)
                return null;

            return artists[0]
                .GetProperty("id")
                .GetString();
        }


        public async Task<List<TrackInfo>> GetMusicBrainzArtistTracksAsync(
    string artistMbid,
    int limit = 3,
    CancellationToken cancellationToken = default)
        {
            using var httpClient = new HttpClient();

            // MusicBrainz REQUIRES a User-Agent
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "YourAppName/1.0 (your-email@example.com)");

            var url =
                $"https://musicbrainz.org/ws/2/recording" +
                $"?artist={artistMbid}" +
                $"&limit={limit}" +
                $"&fmt=json";

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

            if (!json.RootElement.TryGetProperty("recordings", out var recordings))
                return tracks;

            foreach (var item in recordings.EnumerateArray())
            {
                tracks.Add(ParseMusicBrainzRecording(item));
            }

            return tracks;
        }

        private TrackInfo ParseMusicBrainzRecording(JsonElement item)
        {
            var track = new TrackInfo
            {
                Id = item.GetProperty("id").GetString() ?? string.Empty,
                Name = item.GetProperty("title").GetString() ?? string.Empty,
                Popularity = 0,              // MusicBrainz has no popularity concept
                PreviewUrl = null,
                ImageUrl = null,
                ExternalUrl = $"https://musicbrainz.org/recording/{item.GetProperty("id").GetString()}"
            };

            // Artist name
            if (item.TryGetProperty("artist-credit", out var artistCredit) &&
                artistCredit.GetArrayLength() > 0)
            {
                track.ArtistName = artistCredit[0]
                    .GetProperty("name")
                    .GetString() ?? string.Empty;
            }

            // Album (release) name – optional
            if (item.TryGetProperty("releases", out var releases) &&
                releases.GetArrayLength() > 0)
            {
                track.AlbumName = releases[0]
                    .GetProperty("title")
                    .GetString() ?? string.Empty;
            }

            // Metadata (useful later for filtering / diagnostics)
            if (item.TryGetProperty("length", out var length))
                track.Metadata["length_ms"] = length.GetInt32().ToString();

            if (item.TryGetProperty("first-release-date", out var date))
                track.Metadata["first_release_date"] = date.GetString();

            track.Metadata["source"] = "MusicBrainz";
            track.Metadata["entity_type"] = "recording";

            return track;
        }



    }
}
