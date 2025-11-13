using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using MusicDiscoveryAppPOC.Models;

namespace MusicDiscoveryAppPOC;

public partial class TrackReviewWindow : Window
{
    private readonly IList<TrackInfo> _tracks;
    private readonly List<TrackInfo> _selectedTracks = new();
    private int _currentIndex;
    private bool _isPlaying;

    public IReadOnlyList<TrackInfo> SelectedTracks => _selectedTracks;

    public TrackReviewWindow(IList<TrackInfo> tracks)
    {
        InitializeComponent();

        if (tracks == null || tracks.Count == 0)
        {
            throw new ArgumentException("Tracks cannot be empty.", nameof(tracks));
        }

        _tracks = tracks;
        _currentIndex = 0;
        UpdateView();
    }

    private void UpdateView()
    {
        if (_currentIndex >= _tracks.Count)
        {
            CompleteSelection();
            return;
        }

        var track = _tracks[_currentIndex];
        ProgressTextBlock.Text = $"{_currentIndex + 1} / {_tracks.Count}";
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
        _selectedTracks.Add(_tracks[_currentIndex]);
        Advance();
    }

    private void OnSkipClicked(object sender, RoutedEventArgs e)
    {
        StopPreview();
        Advance();
    }

    private void Advance()
    {
        _currentIndex++;
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
        var track = _tracks[_currentIndex];
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

