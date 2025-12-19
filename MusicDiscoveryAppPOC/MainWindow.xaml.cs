using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MusicDiscoveryAppPOC.Models;
using MusicDiscoveryAppPOC.Services;

namespace MusicDiscoveryAppPOC
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ArtistInfo> _searchResults = new();
        private readonly ObservableCollection<ArtistInfo> _selectedArtists = new();

        private SpotifyService? _spotifyService;
        private DeezerService? _deezerService;
        private MusicBrainzService? _musicBrainzService;

        private bool _isConfigured;
        private HashSet<string> _selectedGenres = new(StringComparer.OrdinalIgnoreCase);

        // 🔹 Startup seed artists
        private static readonly string[] StartupSeedArtists =
        {
            "Addison Rae",
            "Shy Smith",
            "BJ Lips"
        };

        public MainWindow()
        {
            InitializeComponent();
            SearchResultsList.ItemsSource = _searchResults;
            SelectedArtistsList.ItemsSource = _selectedArtists;

            InitializeServices();

            Loaded += async (_, __) => await AutoSelectStartupArtistsAsync();
        }

        private void InitializeServices()
        {
            var clientId = "dbb1ff804d89446f8c744d200b20e2d8";
            var clientSecret = "57681c65030c4ea49e563f2ca643d1b4";

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                StatusTextBlock.Text = "Spotify credentials missing.";
                ToggleInputAvailability(false);
                return;
            }

            try
            {
                _spotifyService = new SpotifyService(clientId, clientSecret);
                _deezerService = new DeezerService();
                _musicBrainzService = new MusicBrainzService();

                _isConfigured = true;
                StatusTextBlock.Text = "Ready.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Failed to initialize services: {ex.Message}";
                ToggleInputAvailability(false);
            }
        }

        private void ToggleInputAvailability(bool isEnabled)
        {
            SearchTextBox.IsEnabled = isEnabled;
            SearchResultsList.IsEnabled = isEnabled;
            SelectedArtistsList.IsEnabled = isEnabled;
            FindSuggestionsButton.IsEnabled = isEnabled;
        }

        // 🔹 Auto-select artists on startup (logic-level, not UI automation)
        private async Task AutoSelectStartupArtistsAsync()
        {
            if (!_isConfigured || _spotifyService == null)
                return;

            foreach (var artistName in StartupSeedArtists)
            {
                try
                {
                    var results = await _spotifyService.SearchArtistsAsync(artistName);
                    var first = results.FirstOrDefault();

                    if (first == null || string.IsNullOrWhiteSpace(first.SpotifyId))
                        continue;

                    var fullArtist = await _spotifyService.GetArtistByIdAsync(first.SpotifyId);
                    if (fullArtist == null)
                        continue;

                    if (_selectedArtists.Any(a =>
                        string.Equals(a.SpotifyId, fullArtist.SpotifyId, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    bool hasGenres =
                        fullArtist.Metadata.TryGetValue("genres", out var g) &&
                        !string.IsNullOrWhiteSpace(g);

                    if (!hasGenres && _musicBrainzService != null)
                    {
                        var mbGenres = await _musicBrainzService.GetGenresAsync(fullArtist.Name);
                        if (mbGenres.Count > 0)
                            fullArtist.Metadata["genres"] = string.Join(", ", mbGenres);
                    }

                    _selectedArtists.Add(fullArtist);

                    await Task.Delay(300);
                }
                catch
                {
                    // intentionally ignored per artist
                }
            }

            StatusTextBlock.Text = "Startup artists loaded.";
        }

        private async void OnSearchClicked(object sender, RoutedEventArgs e)
        {
            if (!_isConfigured || _spotifyService is null)
                return;

            var query = SearchTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
                return;

            try
            {
                SetBusyState(true, "Searching Spotify artists...");
                var results = await _spotifyService.SearchArtistsAsync(query).ConfigureAwait(true);

                _searchResults.Clear();
                foreach (var artist in results)
                    _searchResults.Add(artist);

                StatusTextBlock.Text = results.Count == 0
                    ? "No artists found."
                    : $"Found {results.Count} artists.";
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private async void OnAddArtistClicked(object sender, RoutedEventArgs e)
        {
            var selected = SearchResultsList.SelectedItems.Cast<ArtistInfo>().ToList();
            var fullArtists = await GetFullSpotifyArtists(selected);
            var artistsWithGenres = await AddMusicBrainzGenretoArtists(fullArtists);

            foreach (var artist in artistsWithGenres)
            {
                if (_selectedArtists.Any(a => a.SpotifyId == artist.SpotifyId))
                    continue;

                _selectedArtists.Add(artist);
                Console.WriteLine($"SELECTED ARTIST : {artist}");
            }
        }

        private async Task<IEnumerable<ArtistInfo>> GetFullSpotifyArtists(IEnumerable<ArtistInfo> artists)
        {
            var list = new List<ArtistInfo>();

            foreach (var artist in artists)
            {
                if (_spotifyService != null && !string.IsNullOrWhiteSpace(artist.SpotifyId))
                {
                    var full = await _spotifyService.GetArtistByIdAsync(artist.SpotifyId);
                    if (full != null)
                        list.Add(full);
                }
            }

            return list;
        }

        private async Task<IEnumerable<ArtistInfo>> AddMusicBrainzGenretoArtists(IEnumerable<ArtistInfo> artists)
        {
            foreach (var artist in artists)
            {
                bool hasGenres =
                    artist.Metadata.TryGetValue("genres", out var g) &&
                    !string.IsNullOrWhiteSpace(g);

                if (!hasGenres && _musicBrainzService != null)
                {
                    var mbGenres = await _musicBrainzService.GetGenresAsync(artist.Name);
                    if (mbGenres.Count > 0)
                        artist.Metadata["genres"] = string.Join(", ", mbGenres);
                }
            }

            return artists;
        }

        private HashSet<string> GetSelectedGenres()
        {
            var genres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var artist in _selectedArtists)
            {
                if (artist.Metadata.TryGetValue("genres", out var g) && g != null)
                {
                    foreach (var genre in g.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        genres.Add(genre.Trim());
                }
            }

            return genres;
        }

        // 🔹 ORIGINAL handler – RESTORED AND PRESERVED
        private async void OnFindSuggestionsClicked(object sender, RoutedEventArgs e)
        {
            if (!_isConfigured || _spotifyService is null || _deezerService is null)
                return;

            if (_selectedArtists.Count == 0)
                return;

            try
            {
                SetBusyState(true, "Finding similar artists via Deezer...");
                _selectedGenres = GetSelectedGenres();

                var similarArtists = new List<ArtistInfo>();
                var aggregator = new SimilarArtistAggregationService(_deezerService);

                foreach (var artist in _selectedArtists)
                {
                    var related = await aggregator.GetSimilarArtistsWithAtLeastOneTrackAsync(artist.Name, 20);
                    foreach (var candidate in related)
                    {
                        if (similarArtists.All(a =>
                            !string.Equals(a.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            similarArtists.Add(candidate);
                        }
                    }
                }

                var allTracks = new ObservableCollection<TrackInfo>();
                TrackReviewWindow? reviewWindow = null;
                bool reviewWindowOpened = false;

                foreach (var similarArtist in similarArtists)
                {

                    await EnrichArtistIdentitiesAsync(similarArtist);

                    if (!await ArtistValdation(_selectedGenres, similarArtist))
                        continue;

                    similarArtist.TopTrackCount = similarArtist.Tracks.Count;

                    foreach (var track in similarArtist.Tracks)
                    {
                        if (string.IsNullOrWhiteSpace(track.PreviewUrl))
                        {
                            try
                            {
                                var preview = await _deezerService.GetTrackPreviewUrlAsync(track.Name, track.ArtistName);
                                if (!string.IsNullOrWhiteSpace(preview))
                                    track.PreviewUrl = preview;
                            }
                            catch { }
                        }
                    }

                    var rng = new Random();
                    similarArtist.Tracks = similarArtist.Tracks.OrderBy(_ => rng.Next()).ToList();

                    foreach (var t in similarArtist.Tracks)
                        allTracks.Add(t);

                    if (!reviewWindowOpened && allTracks.Count > 0)
                    {
                        reviewWindowOpened = true;
                        reviewWindow = new TrackReviewWindow(allTracks);
                        reviewWindow.Owner = this;
                        reviewWindow.Show();
                    }

                    await Task.Delay(50);
                }
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private async Task<bool> ArtistValdation(HashSet<string> selectedGenres, ArtistInfo similarArtist)
        {
            if (_selectedArtists.Any(a =>
                string.Equals(a.Name, similarArtist.Name, StringComparison.OrdinalIgnoreCase)))
                return false;

            bool hasGenres = await PopulateGenresFromAllSourcesAsync(similarArtist);
            if (!hasGenres)
                return false;

            if (!similarArtist.Metadata.TryGetValue("genres", out var genreText))
                return false;

            var candidateGenres = genreText
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(g => g.Trim());

            if (!candidateGenres.Any(g => selectedGenres.Contains(g)))
                return false;

            return true;
        }

        private async Task EnrichArtistIdentitiesAsync(ArtistInfo artist)
        {
            if (artist.SpotifyId == null && _spotifyService != null)
            {
                var match = await _spotifyService.GetArtistByNameAsync(artist.Name);
                if (match != null)
                {
                    artist.SpotifyId = match.SpotifyId;
                    artist.ImageUrl ??= match.ImageUrl;
                }
            }
        }




        private async Task<bool> PopulateGenresFromAllSourcesAsync(ArtistInfo artist)
        {
            var genreSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // --- Spotify ---
            if (_spotifyService != null && !string.IsNullOrWhiteSpace(artist.SpotifyId))
            {
                try
                {
                    var spotifyArtist = await _spotifyService.GetArtistByIdAsync(artist.SpotifyId);
                    if (spotifyArtist?.Metadata.TryGetValue("genres", out var sg) == true &&
                        !string.IsNullOrWhiteSpace(sg))
                    {
                        foreach (var g in sg.Split(',', StringSplitOptions.RemoveEmptyEntries))
                            genreSet.Add(g.Trim());
                    }
                }
                catch
                {
                    // swallow intentionally (same as original behavior)
                }
            }

            // --- MusicBrainz ---
            if (_musicBrainzService != null)
            {
                await Task.Delay(1000); // rate-limit protection
                var mbGenres = await _musicBrainzService.GetGenresAsync(artist.Name);

                foreach (var g in mbGenres)
                    if (!string.IsNullOrWhiteSpace(g))
                        genreSet.Add(g.Trim());
            }

            if (genreSet.Count > 0)
            {
                artist.Metadata["genres"] = string.Join(", ", genreSet);
                return true;
            }

            return false;
        }

        private void SetBusyState(bool isBusy, string? message = null)
        {
            BusyIndicator.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            BusyIndicator.IsIndeterminate = isBusy;
            SearchTextBox.IsEnabled = !isBusy;
            SearchResultsList.IsEnabled = !isBusy;
            SelectedArtistsList.IsEnabled = !isBusy;
            FindSuggestionsButton.IsEnabled = !isBusy;

            if (!string.IsNullOrWhiteSpace(message))
                StatusTextBlock.Text = message;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _spotifyService?.Dispose();
            _deezerService?.Dispose();
        }
    }
}
