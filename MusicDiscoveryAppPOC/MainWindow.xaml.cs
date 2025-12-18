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

        public MainWindow()
        {
            InitializeComponent();
            SearchResultsList.ItemsSource = _searchResults;
            SelectedArtistsList.ItemsSource = _selectedArtists;
            InitializeServices();
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

            _spotifyService = new SpotifyService(clientId, clientSecret);
            _deezerService = new DeezerService();
            _musicBrainzService = new MusicBrainzService();
            _isConfigured = true;
            StatusTextBlock.Text = "Ready.";
        }

        private void ToggleInputAvailability(bool isEnabled)
        {
            SearchTextBox.IsEnabled = isEnabled;
            SearchResultsList.IsEnabled = isEnabled;
            SelectedArtistsList.IsEnabled = isEnabled;
            FindSuggestionsButton.IsEnabled = isEnabled;
        }

        private async void OnSearchClicked(object sender, RoutedEventArgs e)
        {
            if (!_isConfigured || _spotifyService == null)
                return;

            var query = SearchTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
                return;

            SetBusyState(true, "Searching Spotify artists...");
            var results = await _spotifyService.SearchArtistsAsync(query);
            _searchResults.Clear();
            foreach (var artist in results)
                _searchResults.Add(artist);

            SetBusyState(false);
        }

        private async void OnAddArtistClicked(object sender, RoutedEventArgs e)
        {
            var selected = SearchResultsList.SelectedItems.Cast<ArtistInfo>().ToList();
            foreach (var artist in selected)
            {
                if (_spotifyService != null && !string.IsNullOrWhiteSpace(artist.SpotifyId))
                {
                    var full = await _spotifyService.GetArtistByIdAsync(artist.SpotifyId);
                    if (full != null && !_selectedArtists.Any(a => a.SpotifyId == full.SpotifyId))
                        _selectedArtists.Add(full);
                }
            }
        }

        private HashSet<string> GetSelectedGenres()
        {
            var genres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var artist in _selectedArtists)
            {
                if (artist.Metadata.TryGetValue("genres", out var g))
                {
                    foreach (var genre in SplitGenres(g))
                        genres.Add(genre);
                }
            }
            return genres;
        }

        private static IEnumerable<string> SplitGenres(string? genreText)
        {
            if (string.IsNullOrWhiteSpace(genreText))
                return Enumerable.Empty<string>();

            return genreText
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(g => g.Trim());
        }

        // 🔹 NEW LOGIC: Populate genres using Spotify → MusicBrainz → Deezer (future)
        private async Task<bool> PopulateGenresAsync(ArtistInfo artist)
        {
            // 1️⃣ Spotify
            if (_spotifyService != null && !string.IsNullOrWhiteSpace(artist.SpotifyId))
            {
                var spotifyArtist = await _spotifyService.GetArtistByIdAsync(artist.SpotifyId);
                if (spotifyArtist?.Metadata.TryGetValue("genres", out var sg) == true &&
                    !string.IsNullOrWhiteSpace(sg))
                {
                    artist.Metadata["genres"] = sg;
                    return true;
                }
            }

            // 2️⃣ MusicBrainz
            if (_musicBrainzService != null)
            {
                var mbGenres = await _musicBrainzService.GetGenresAsync(artist.Name);
                if (mbGenres.Count > 0)
                {
                    artist.Metadata["genres"] = string.Join(", ", mbGenres);
                    return true;
                }
            }

            // 3️⃣ Deezer → no explicit genres (skip for now)

            return false;
        }

        // 🔹 UPDATED VALIDATION LOGIC
        private async Task<bool> ArtistValdation(
            HashSet<string> selectedGenres,
            ArtistInfo similarArtist)
        {
            if (_selectedArtists.Any(a =>
                string.Equals(a.Name, similarArtist.Name, StringComparison.OrdinalIgnoreCase)))
                return false;

            bool hasGenres = await PopulateGenresAsync(similarArtist);
            if (!hasGenres)
                return false;

            if (!similarArtist.Metadata.TryGetValue("genres", out var genreText))
                return false;

            var candidateGenres = SplitGenres(genreText);
            if (!candidateGenres.Any(g => selectedGenres.Contains(g)))
                return false;

            return true;
        }

        private async void OnFindSuggestionsClicked(object sender, RoutedEventArgs e)
        {
            if (_deezerService == null || _spotifyService == null)
                return;

            _selectedGenres = GetSelectedGenres();
            var aggregator = new SimilarArtistAggregationService(_deezerService);
            var similarArtists = new List<ArtistInfo>();

            foreach (var artist in _selectedArtists)
            {
                var related = await aggregator.GetSimilarArtistsWithAtLeastOneTrackAsync(artist.Name, 10);
                similarArtists.AddRange(related);
            }

            similarArtists = similarArtists
                .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var allTracks = new ObservableCollection<TrackInfo>();
            TrackReviewWindow? reviewWindow = null;

            foreach (var artist in similarArtists)
            {
                if (!await ArtistValdation(_selectedGenres, artist))
                    continue;

                foreach (var track in artist.Tracks)
                    allTracks.Add(track);

                if (reviewWindow == null && allTracks.Count > 0)
                {
                    reviewWindow = new TrackReviewWindow(allTracks);
                    reviewWindow.Owner = this;
                    reviewWindow.Show();
                }
            }
        }

        private void SetBusyState(bool isBusy, string? message = null)
        {
            BusyIndicator.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            if (!string.IsNullOrWhiteSpace(message))
                StatusTextBlock.Text = message;
        }

        protected override void OnClosed(EventArgs e)
        {
            _spotifyService?.Dispose();
            _deezerService?.Dispose();
            base.OnClosed(e);
        }
    }
}
