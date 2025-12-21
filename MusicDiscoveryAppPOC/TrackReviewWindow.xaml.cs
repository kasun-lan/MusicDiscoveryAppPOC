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
using System.Threading.Tasks;

namespace MusicDiscoveryAppPOC;

public partial class TrackReviewWindow : Window
{
    private readonly ObservableCollection<TrackInfo> _tracks;

    private bool _isPlaying;
    private readonly List<TrackInfo> _selectedTracks = new();
    private int _currentPosition = 0;

    public int CurrentReviewIndex => _currentPosition;
    public IReadOnlyList<TrackInfo> SelectedTracks => _selectedTracks;

    // 🔵 ADDED: event to notify MainWindow when a track is liked
    public event Func<TrackInfo, Task>? TrackLiked;

    public TrackReviewWindow(ObservableCollection<TrackInfo> tracks)
    {
        InitializeComponent();

        _tracks = tracks;

        // React to new incoming tracks
        _tracks.CollectionChanged += (_, __) =>
        {
            // No shuffling, no index remapping.
            // Just refresh if we're currently displaying the last known item.
            UpdateView();
        };

        UpdateView();
    }

    private void UpdateView()
    {
        if (_tracks.Count == 0)
            return;

        if (_currentPosition >= _tracks.Count)
        {
            CompleteSelection();
            return;
        }

        var track = _tracks[_currentPosition];

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

        StopPreview();

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
            return null;

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

    private async void OnKeepClicked(object sender, RoutedEventArgs e)
    {
        StopPreview();

        var current = GetCurrentTrackOrNull();
        if (current != null)
        {
            if (!_selectedTracks.Any(t =>
                string.Equals(t.Id, current.Id, StringComparison.OrdinalIgnoreCase)))
            {
                _selectedTracks.Add(current);
            }

            if (TrackLiked != null)
            {
                _ = TrackLiked.Invoke(current); // fire-and-forget
            }
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
        if (_selectedTracks.Count == 0)
        {
            MessageBox.Show(
                "There are no tracks available to add to a playlist yet. Keep at least one track first.",
                "No Tracks Selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var playlistWindow = new CreatePlaylistWindow(new List<TrackInfo>(_selectedTracks))
        {
            Owner = this
        };

        playlistWindow.Show();
    }

    private TrackInfo? GetCurrentTrackOrNull()
    {
        if (_currentPosition < 0 || _currentPosition >= _tracks.Count)
            return null;

        return _tracks[_currentPosition];
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
        var track = GetCurrentTrackOrNull();
        if (track == null || string.IsNullOrWhiteSpace(track.PreviewUrl))
            return;

        if (_isPlaying)
            PausePreview();
        else
            PlayPreview(track.PreviewUrl);
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
        catch { }
    }

    private void StopPreview()
    {
        try
        {
            PreviewMediaElement.Stop();
            PreviewMediaElement.Source = null;
            _isPlaying = false;
        }
        catch { }
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
