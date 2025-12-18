using MusicDiscoveryAppPOC.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicDiscoveryAppPOC.Services
{
    internal class SimilarArtistAggregationService
    {
        private readonly DeezerService _deezerService;

        public SimilarArtistAggregationService(DeezerService deezerService)
        {
            _deezerService = deezerService;
        }

        public async Task<List<ArtistInfo>> GetSimilarArtistsWithAtLeastOneTrackAsync(
            string artistName,
            int maxArtists,
            CancellationToken cancellationToken = default)
        {
            var result = new List<ArtistInfo>();

            // 1. Get similar artists from Deezer
            var similarArtists =
                await _deezerService.GetSimilarArtistsByNameAsync(artistName, cancellationToken);

            foreach (var artist in similarArtists)
            {
                if (result.Count >= maxArtists)
                    break;

                if (string.IsNullOrWhiteSpace(artist.DeezerId))
                    continue;

                // 2. Fetch tracks for this artist
                var tracks = await _deezerService
                    .GetDeezerArtistTracksAsync(artist.DeezerId, limit: 3, cancellationToken);

                // 3. Keep only artists with at least ONE track
                if (tracks.Count >= 1)
                {
                    artist.Tracks.AddRange(tracks);
                    artist.TopTrackCount = tracks.Count;
                    artist.Metadata["track_count"] = tracks.Count.ToString();

                    result.Add(artist);
                }
            }

            return result;
        }
    }
}
