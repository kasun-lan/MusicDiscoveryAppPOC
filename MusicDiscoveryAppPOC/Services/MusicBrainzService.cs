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
    }
}
