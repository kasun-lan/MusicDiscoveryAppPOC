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
        private HashSet<string> _selectedGenres;

        public MainWindow()
        {
            InitializeComponent();
            SearchResultsList.ItemsSource = _searchResults;
            SelectedArtistsList.ItemsSource = _selectedArtists;



            InitializeServices();
        }

        private void InitializeServices()
        {
            //var clientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
            //var clientSecret = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET");

            var clientId = "dbb1ff804d89446f8c744d200b20e2d8";
            var clientSecret = "57681c65030c4ea49e563f2ca643d1b4";

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                StatusTextBlock.Text = "Set SPOTIFY_CLIENT_ID and SPOTIFY_CLIENT_SECRET environment variables and restart the app.";
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

        private async void OnSearchClicked(object sender, RoutedEventArgs e)
        {
            if (!_isConfigured || _spotifyService is null)
            {
                MessageBox.Show("Spotify is not configured. Please set your environment variables and restart.", "Configuration Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var query = SearchTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                MessageBox.Show("Enter an artist name to search.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                SetBusyState(true, "Searching Spotify artists...");
                var results = await _spotifyService.SearchArtistsAsync(query).ConfigureAwait(true);

                _searchResults.Clear();
                foreach (var artist in results)
                {
                    _searchResults.Add(artist);
                }

                StatusTextBlock.Text = results.Count == 0 ? "No artists found." : $"Found {results.Count} artists.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Spotify search failed.";
                MessageBox.Show($"Unable to search Spotify. {ex.Message}", "Spotify Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private void OnClearClicked(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            _searchResults.Clear();
            StatusTextBlock.Text = "Cleared search results.";
        }

        private async void OnAddArtistClicked(object sender, RoutedEventArgs e)
        {
            var selected = SearchResultsList.SelectedItems.Cast<ArtistInfo>().ToList();
            var FullSpotifyArtists = await GetFullSpotifyArtists(selected);
            var ArtistsWithGenres = await AddMusicBrainzGenretoArtists(FullSpotifyArtists);

            foreach (var artist in ArtistsWithGenres)
            {
                if (_selectedArtists.Any(a => a.SpotifyId == artist.SpotifyId))
                    continue;
                _selectedArtists.Add(artist);
                Console.WriteLine($"SELECTED ARTIST : {artist.ToString()}");

            }

        }

        private void OnRemoveArtistClicked(object sender, RoutedEventArgs e)
        {
            var toRemove = SelectedArtistsList.SelectedItems.Cast<ArtistInfo>().ToList();
            foreach (var artist in toRemove)
            {
                _selectedArtists.Remove(artist);
            }

            if (toRemove.Count > 0)
            {
                StatusTextBlock.Text = $"Removed {toRemove.Count} artist(s).";
            }
        }

        private void OnSearchResultDoubleClicked(object sender, MouseButtonEventArgs e)
        {
            if (SearchResultsList.SelectedItem is ArtistInfo artist)
            {
                AddArtistsToSelection(new[] { artist });
            }
        }

        private async Task<IEnumerable<ArtistInfo>> AddMusicBrainzGenretoArtists(IEnumerable<ArtistInfo> artists)
        {


            foreach (var artist in artists)
            {
                bool hasSpotifyGenres =
                   artist.Metadata.TryGetValue("genres", out var g)
                   && !string.IsNullOrWhiteSpace(g);

                if (!hasSpotifyGenres && _musicBrainzService != null)
                {
                    var mbGenres = await _musicBrainzService.GetGenresAsync(artist.Name);

                    if (mbGenres.Count > 0)
                        artist.Metadata["genres"] = string.Join(", ", mbGenres);
                }
            }

            return artists;
        }

        private async Task<IEnumerable<ArtistInfo>> GetFullSpotifyArtists(IEnumerable<ArtistInfo> artists)
        {
            List<ArtistInfo> FullArtists = new List<ArtistInfo>();

            foreach (var artist in artists)
            {
                ArtistInfo fullArtist = null;

                if (_spotifyService != null && !string.IsNullOrWhiteSpace(artist.SpotifyId))
                {
                    var spotifyFull = await _spotifyService.GetArtistByIdAsync(artist.SpotifyId);
                    if (spotifyFull != null)
                    {
                        fullArtist = spotifyFull;
                        FullArtists.Add(fullArtist);
                    }
                }
            }

            return FullArtists;
        }

        private async void AddArtistsToSelection(IEnumerable<ArtistInfo> artists)
        {
            foreach (var artist in artists)
            {
                if (_selectedArtists.Any(a => a.SpotifyId == artist.SpotifyId))
                    continue;

                ArtistInfo fullArtist = artist;

                // Step 1: Get full Spotify details
                if (_spotifyService != null && !string.IsNullOrWhiteSpace(artist.SpotifyId))
                {
                    var spotifyFull = await _spotifyService.GetArtistByIdAsync(artist.SpotifyId);
                    if (spotifyFull != null)
                        fullArtist = spotifyFull;
                }

                // Step 2: If genres missing → fallback to MusicBrainz
                bool hasSpotifyGenres =
                    fullArtist.Metadata.TryGetValue("genres", out var g)
                    && !string.IsNullOrWhiteSpace(g);

                if (!hasSpotifyGenres && _musicBrainzService != null)
                {
                    var mbGenres = await _musicBrainzService.GetGenresAsync(fullArtist.Name);

                    if (mbGenres.Count > 0)
                        fullArtist.Metadata["genres"] = string.Join(", ", mbGenres);
                }

                _selectedArtists.Add(fullArtist);
            }

            StatusTextBlock.Text = "Added artist(s) to selection.";
        }


        private HashSet<string> GetSelectedGenres()
        {
            var genres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var artist in _selectedArtists)
            {
                if (artist.Metadata.TryGetValue("genres", out var g) && g is not null)
                {
                    foreach (var genre in g.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        genres.Add(genre.Trim());
                    }
                }
            }

            return genres;
        }


       


        private async void OnFindSuggestionsClicked(object sender, RoutedEventArgs e)
        {

            if (!_isConfigured || _spotifyService is null || _deezerService is null)
            {
                MessageBox.Show("Services are not configured. Check your Spotify credentials.", "Configuration Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_selectedArtists.Count == 0)
            {
                MessageBox.Show("Select at least one artist before continuing.", "No Artists Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                SetBusyState(true, "Finding similar artists via Deezer...");
                var similarArtists = new List<ArtistInfo>();
                _selectedGenres = GetSelectedGenres();


                var aggregator = new SimilarArtistAggregationService(_deezerService);

                foreach (var artist in _selectedArtists)
                {
                    var related = await aggregator.GetSimilarArtistsWithAtLeastOneTrackAsync(artist.Name, 10);
                    foreach (var candidate in related)
                    {
                        if (similarArtists.All(a => !string.Equals(a.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            similarArtists.Add(candidate);
                        }
                    }
                }

                #region non critical
                Console.WriteLine("Deezer similarArtists");
                foreach (var artist in similarArtists)
                {
                    Console.WriteLine($"{artist.ToString()}");
                }
                

                if (similarArtists.Count == 0)
                {
                    StatusTextBlock.Text = "Deezer did not return similar artists.";
                    MessageBox.Show("Unable to find similar artists on Deezer for the selected artists.", "No Similar Artists", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                StatusTextBlock.Text = $"Found {similarArtists.Count} similar artist(s). Fetching top tracks from Spotify...";


                #endregion




                #region non critical
                string strSelectedGenres = string.Join(",", _selectedGenres);
                Console.WriteLine();
                Console.WriteLine("Selected Genres: ");
                Console.WriteLine(strSelectedGenres);
                #endregion

                // Final list we will fill gradually
                var allTracks = new ObservableCollection<TrackInfo>();

                TrackReviewWindow? reviewWindow = null;
                bool reviewWindowOpened = false;

                int artistIndex = 0;

                int totalTrackCount = 0;

                foreach (var similarArtist in similarArtists)
                {

                    try
                    {
                        if (await ArtistValdation(_selectedGenres, similarArtist) == false)
                            continue;

                        artistIndex++;


                        // Fetch top tracks
                        //var tracks = await _musicBrainzService.GetMusicBrainzArtistTracksAsync(MName).ConfigureAwait(true);


                        similarArtist.TopTrackCount = similarArtist.Tracks.Count;
                        totalTrackCount += similarArtist.Tracks.Count;

                        // Fetch missing previews via Deezer
                        foreach (var track in similarArtist.Tracks)
                        {
                            if (string.IsNullOrWhiteSpace(track.PreviewUrl))
                            {
                                try
                                {
                                    var deezerPreview = await _deezerService.GetTrackPreviewUrlAsync(track.Name, track.ArtistName).ConfigureAwait(true);
                                    if (!string.IsNullOrWhiteSpace(deezerPreview))
                                    {
                                        track.PreviewUrl = deezerPreview;
                                    }
                                }
                                catch { }
                            }
                        }

                        // SHUFFLE this artist's tracks to reduce clustering
                        var rng = new Random();
                        similarArtist.Tracks = similarArtist.Tracks.OrderBy(_ => rng.Next()).ToList();

                        // ADD THEM TO THE MASTER STREAM
                        var rng1 = new Random();

                        foreach (var t in similarArtist.Tracks)
                        {
                            int safeStart = (reviewWindow is null) ? 0 : reviewWindow.CurrentReviewIndex + 1;
                            int insertIndex = rng1.Next(safeStart, allTracks.Count + 1);

                            allTracks.Insert(insertIndex, t);
                        }


                         //OPEN THE TRACK REVIEW WINDOW IMMEDIATELY AFTER FIRST ARTIST
                        if (!reviewWindowOpened && allTracks.Count > 0)
                        {
                            reviewWindowOpened = true;

                            // Open TrackReviewWindow with streaming collection
                            reviewWindow = new TrackReviewWindow(allTracks);
                            reviewWindow.Owner = this;
                            reviewWindow.Show();

                            StatusTextBlock.Text = "Starting track review… loading more in background.";
                        }

                        //Continue processing remaining artists WITHOUT blocking UI
                       await Task.Delay(50);
                    }
                    catch (Exception ex)
                    {

                    }
                }

                Console.WriteLine();
                Console.WriteLine("Selected Genres: ");
                foreach (var artist in similarArtists)
                {
                    Console.WriteLine($"{artist.ToString()}");
                }

                Console.WriteLine();
                Console.WriteLine($"Total Track Count : {totalTrackCount}");



                // After loop ends
                if (!reviewWindowOpened)
                {
                    StatusTextBlock.Text = "No matching tracks found.";
                    MessageBox.Show("No top tracks were found on Spotify for the similar artists.", "No Tracks Found",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // When user finishes and closes ReviewWindow:
                reviewWindow!.Closed += (_, __) =>
                {
                    if (reviewWindow.SelectedTracks.Any())
                    {
                        var window = new SelectedTracksWindow(reviewWindow.SelectedTracks);
                        window.Owner = this;
                        window.ShowDialog();
                    }
                    else
                    {
                        StatusTextBlock.Text = "No tracks selected.";
                    }
                };

            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Failed to load suggestions.";
                MessageBox.Show($"Unable to gather suggestions. {ex.Message}", "Suggestion Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private async Task<bool> ArtistValdation(HashSet<string> selectedGenres, ArtistInfo similarArtist)
        {
            // ❗ Skip if this artist is one of the originally selected artists
            if (_selectedArtists.Any(a =>
                    string.Equals(a.Name, similarArtist.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }


            // Look up Spotify version of this artist
            //var spotifyArtist = await _spotifyService.GetArtistByNameAsync(similarArtist.Name).ConfigureAwait(true);
            //if (spotifyArtist?.SpotifyId is null)
            //    continue;

            //bool hasSpotifyGenres =
            //similarArtist.Metadata.TryGetValue("genres", out var g)
            //&& !string.IsNullOrWhiteSpace(g);

            //if (!hasSpotifyGenres && _musicBrainzService != null)
            //{
            await Task.Delay(1000);
            var mbGenres = await _musicBrainzService.GetGenresAsync(similarArtist.Name);

            if (mbGenres.Count > 0)
                similarArtist.Metadata["genres"] = string.Join(", ", mbGenres);
            else
            {
                //TODO : this is not logical but ok for now.
                return true; 

            }

            


            // GENRE FILTERING
            if (similarArtist.Metadata.TryGetValue("genres", out var genreText) && genreText is not null)
            {
                var candidateGenres = genreText
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(g => g.Trim());

                if (!candidateGenres.Any(g => selectedGenres.Contains(g)))
                    return false;
            }
            else
            {
                return false;
            }

            return true ;
        }





        //private HashSet<string> GetSelectedGenres()
        //{
        //    var genres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        //    foreach (var artist in _selectedArtists)
        //    {
        //        if (artist.Metadata.TryGetValue("genres", out var g) && g is not null)
        //        {
        //            foreach (var genre in g.Split(',', StringSplitOptions.RemoveEmptyEntries))
        //            {
        //                genres.Add(genre.Trim());
        //            }
        //        }
        //    }

        //    return genres;
        //}


        private void SetBusyState(bool isBusy, string? message = null)
        {
            BusyIndicator.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            BusyIndicator.IsIndeterminate = isBusy;
            SearchTextBox.IsEnabled = !isBusy;
            SearchResultsList.IsEnabled = !isBusy;
            SelectedArtistsList.IsEnabled = !isBusy;
            FindSuggestionsButton.IsEnabled = !isBusy;

            if (!string.IsNullOrWhiteSpace(message))
            {
                StatusTextBlock.Text = message;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _spotifyService?.Dispose();
            _deezerService?.Dispose();
        }
    }
}