using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Linq;
using MusicDiscoveryAppPOC.Models;
using System.Collections.ObjectModel;

namespace MusicDiscoveryAppPOC;

public partial class TrackReviewWindow : Window
{
    private readonly ObservableCollection<TrackInfo> _tracks;

    // This stays! But now it represents our position inside the shuffle-order list.
    private readonly List<int> _shuffleOrder = new();  // list of track indices in the order they should appear
    private bool _isPlaying;



    private readonly List<TrackInfo> _selectedTracks = new();
    private int _currentPosition = 0; // position inside shuffle queue


    public IReadOnlyList<TrackInfo> SelectedTracks => _selectedTracks;

    public TrackReviewWindow(ObservableCollection<TrackInfo> tracks)
    {
        InitializeComponent();

        _tracks = tracks;

        // Initial shuffle order for existing tracks
        for (int i = 0; i < _tracks.Count; i++)
            _shuffleOrder.Add(i);

        ShuffleList(_shuffleOrder);

        // React to new incoming tracks
        _tracks.CollectionChanged += (s, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                var rng = new Random();

                for (int i = 0; i < e.NewItems.Count; i++)
                {
                    int newIndex = e.NewStartingIndex >= 0
                        ? e.NewStartingIndex + i
                        : _tracks.Count - e.NewItems.Count + i;

                    // Shift any stored indices that are >= the new insertion point
                    for (int j = 0; j < _shuffleOrder.Count; j++)
                    {
                        if (_shuffleOrder[j] >= newIndex)
                        {
                            _shuffleOrder[j]++;
                        }
                    }

                    // Insert new item into shuffle order at a random position
                    int pos = rng.Next(_shuffleOrder.Count + 1);
                    _shuffleOrder.Insert(pos, newIndex);

                    // If the insert happens before or at the current track position, shift the cursor
                    if (pos <= _currentPosition)
                    {
                        _currentPosition++;
                    }
                }

                // Refresh UI so bindings stay consistent with the shuffled indices
                UpdateView();
            }
        };

        UpdateView();
    }


    private void ShuffleList(List<int> list)
    {
        var rng = new Random();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }


    private void UpdateView()
    {
        if (_currentPosition >= _shuffleOrder.Count)
        {
            CompleteSelection();
            return;
        }

        if (_shuffleOrder.Count == 0)
            return;

        int trackIndex = _shuffleOrder[_currentPosition];
        var track = _tracks[trackIndex];

        ProgressTextBlock.Text = $"{_currentPosition + 1} / {_tracks.Count}";
        TrackTitleTextBlock.Text = track.Name;
        ArtistTextBlock.Text = track.ArtistName;
        AlbumTextBlock.Text = $"Album: {track.AlbumName}";
        PopularityTextBlock.Text = $"Popularity: {track.Popularity}";

        AlbumCoverImage.Source = LoadImage(track.ImageUrl);

        if (!string.IsNullOrWhiteSpace(track.ExternalUrl))
        {
            SpotifyLink.IsEnabled = true;
            SpotifyLink.NavigateUri = new Uri(track.ExternalUrl);
        }
        else
        {
            SpotifyLink.IsEnabled = false;
            SpotifyLink.NavigateUri = null;
        }

        // Stop any currently playing preview
        StopPreview();

        // Update preview controls
        if (!string.IsNullOrWhiteSpace(track.PreviewUrl))
        {
            PlayPauseButton.IsEnabled = true;
            PlayPauseButton.Content = "▶ Play";
            PreviewStatusTextBlock.Text = "Ready to play";
            PreviewStatusTextBlock.Foreground = Brushes.SteelBlue;
            _isPlaying = false;
        }
        else
        {
            PlayPauseButton.IsEnabled = false;
            PlayPauseButton.Content = "▶ Play";
            PreviewStatusTextBlock.Text = "Unavailable";
            PreviewStatusTextBlock.Foreground = Brushes.Gray;
            _isPlaying = false;
        }
    }

    private ImageSource? LoadImage(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imageUrl, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void OnKeepClicked(object sender, RoutedEventArgs e)
    {
        StopPreview();

        var current = GetCurrentTrackOrNull();
        if (current != null)
        {
            // Avoid duplicates in selected list by track Id
            if (!_selectedTracks.Any(t => string.Equals(t.Id, current.Id, StringComparison.OrdinalIgnoreCase)))
                _selectedTracks.Add(current);
        }

        Advance();
    }

    private void OnSkipClicked(object sender, RoutedEventArgs e)
    {
        StopPreview();
        Advance();
    }

    private void Advance()
    {
        _currentPosition++;
        if (_currentPosition >= _shuffleOrder.Count)
        {
            CompleteSelection();
            return;
        }

        UpdateView();

    }

    private void CompleteSelection()
    {
        StopPreview();
        DialogResult = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopPreview();
        base.OnClosed(e);
    }

    private void OnCreatePlaylistClicked(object sender, RoutedEventArgs e)
    {
        // Start with a copy of selected tracks (these are already actual TrackInfo objects)
        var playlistCandidates = new List<TrackInfo>(_selectedTracks);

        // Also consider the currently shown track (if not already selected)
        var currentTrack = GetCurrentTrackOrNull();
        if (currentTrack != null)
        {
            if (!playlistCandidates.Any(t => string.Equals(t.Id, currentTrack.Id, StringComparison.OrdinalIgnoreCase)))
            {
                playlistCandidates.Add(currentTrack);
            }
        }

        if (playlistCandidates.Count == 0)
        {
            MessageBox.Show("There are no tracks available to add to a playlist yet. Keep at least one track first.", "No Tracks Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var playlistWindow = new CreatePlaylistWindow(playlistCandidates);
        playlistWindow.Owner = this;
        playlistWindow.Show();
    }


    private TrackInfo? GetCurrentTrackOrNull()
    {
        if (_shuffleOrder.Count == 0) return null;
        if (_currentPosition < 0 || _currentPosition >= _shuffleOrder.Count) return null;

        int trackIndex = _shuffleOrder[_currentPosition];
        if (trackIndex < 0 || trackIndex >= _tracks.Count) return null;

        return _tracks[trackIndex];
    }


    private void OnHyperlinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri != null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }

    private void OnPlayPauseClicked(object sender, RoutedEventArgs e)
    {
        if (_shuffleOrder.Count == 0)
            return;

        int trackIndex = _shuffleOrder[_currentPosition];
        var track = GetCurrentTrackOrNull();
        if (track == null) return;


        if (string.IsNullOrWhiteSpace(track.PreviewUrl))
        {
            return;
        }

        if (_isPlaying)
        {
            PausePreview();
        }
        else
        {
            PlayPreview(track.PreviewUrl);
        }
    }

    private void PlayPreview(string previewUrl)
    {
        try
        {
            PreviewMediaElement.Source = new Uri(previewUrl);
            PreviewMediaElement.Play();
            _isPlaying = true;
            PlayPauseButton.Content = "⏸ Pause";
            PreviewStatusTextBlock.Text = "Playing...";
            PreviewStatusTextBlock.Foreground = Brushes.Green;
        }
        catch (Exception ex)
        {
            PreviewStatusTextBlock.Text = $"Error: {ex.Message}";
            PreviewStatusTextBlock.Foreground = Brushes.Red;
            _isPlaying = false;
            PlayPauseButton.Content = "▶ Play";
        }
    }

    private void PausePreview()
    {
        try
        {
            PreviewMediaElement.Pause();
            _isPlaying = false;
            PlayPauseButton.Content = "▶ Play";
            PreviewStatusTextBlock.Text = "Paused";
            PreviewStatusTextBlock.Foreground = Brushes.Orange;
        }
        catch
        {
            // Ignore errors when pausing
        }
    }

    private void StopPreview()
    {
        try
        {
            PreviewMediaElement.Stop();
            PreviewMediaElement.Source = null;
            _isPlaying = false;
        }
        catch
        {
            // Ignore errors when stopping
        }
    }

    private void OnPreviewEnded(object sender, RoutedEventArgs e)
    {
        _isPlaying = false;
        PlayPauseButton.Content = "▶ Play";
        PreviewStatusTextBlock.Text = "Finished";
        PreviewStatusTextBlock.Foreground = Brushes.Gray;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Right)
        {
            OnKeepClicked(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            OnSkipClicked(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }
}

