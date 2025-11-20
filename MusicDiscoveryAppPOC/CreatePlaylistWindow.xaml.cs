using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using MusicDiscoveryAppPOC.Models;

namespace MusicDiscoveryAppPOC;

public partial class CreatePlaylistWindow : Window, INotifyPropertyChanged
{
    private string _playlistName = "New Playlist";
    private string _playlistDescription = string.Empty;
    private string _selectedCountText = string.Empty;

    public ObservableCollection<SelectableTrack> Tracks { get; } = new();

    public string PlaylistName
    {
        get => _playlistName;
        set
        {
            if (_playlistName != value)
            {
                _playlistName = value;
                OnPropertyChanged();
            }
        }
    }

    public string PlaylistDescription
    {
        get => _playlistDescription;
        set
        {
            if (_playlistDescription != value)
            {
                _playlistDescription = value;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedCountText
    {
        get => _selectedCountText;
        private set
        {
            if (_selectedCountText != value)
            {
                _selectedCountText = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CreatePlaylistWindow(IEnumerable<TrackInfo> tracks)
    {
        InitializeComponent();
        DataContext = this;

        foreach (var track in tracks)
        {
            var selectable = new SelectableTrack(track);
            selectable.PropertyChanged += (_, _) => UpdateSelectedCount();
            Tracks.Add(selectable);
        }

        UpdateSelectedCount();
        PlaylistNameTextBox.Focus();
        PlaylistNameTextBox.CaretIndex = PlaylistNameTextBox.Text.Length;
    }

    private void UpdateSelectedCount()
    {
        var total = Tracks.Count;
        var selected = Tracks.Count(t => t.IsSelected);
        SelectedCountText = total == 0
            ? "No tracks available"
            : $"{selected} of {total} tracks selected";
    }

    private void OnSelectAllClicked(object sender, RoutedEventArgs e)
    {
        foreach (var track in Tracks)
        {
            track.IsSelected = true;
        }
        UpdateSelectedCount();
    }

    private void OnSelectNoneClicked(object sender, RoutedEventArgs e)
    {
        foreach (var track in Tracks)
        {
            track.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        var selected = Tracks.Where(t => t.IsSelected).Select(t => t.Track).ToList();
        if (selected.Count == 0)
        {
            StatusTextBlock.Text = "Select at least one track to copy.";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Playlist: {PlaylistName}");
        if (!string.IsNullOrWhiteSpace(PlaylistDescription))
        {
            builder.AppendLine($"Description: {PlaylistDescription}");
        }
        builder.AppendLine();

        for (var i = 0; i < selected.Count; i++)
        {
            var track = selected[i];
            builder.AppendLine($"{i + 1}. {track.Name} — {track.ArtistName} [{track.AlbumName}]");
            if (!string.IsNullOrWhiteSpace(track.ExternalUrl))
            {
                builder.AppendLine($"    Spotify: {track.ExternalUrl}");
            }
            if (!string.IsNullOrWhiteSpace(track.PreviewUrl))
            {
                builder.AppendLine($"    Preview: {track.PreviewUrl}");
            }
        }

        Clipboard.SetText(builder.ToString());
        StatusTextBlock.Text = "Playlist copied to clipboard.";
        StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var selectedTracks = Tracks.Where(t => t.IsSelected)
                                   .Select(t => t.Track)
                                   .ToList();

        if (string.IsNullOrWhiteSpace(PlaylistName))
        {
            StatusTextBlock.Text = "Enter a playlist name before saving.";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }

        if (selectedTracks.Count == 0)
        {
            StatusTextBlock.Text = "Select at least one track before saving.";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }

        try
        {
            var playlist = new PlaylistInfo
            {
                Name = PlaylistName.Trim(),
                Description = PlaylistDescription.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                Tracks = selectedTracks.ToList()
            };

            var saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "MusicDiscoveryAppPOC", "Playlists");
            Directory.CreateDirectory(saveDirectory);

            var safeName = CreateFileSafeName(playlist.Name);
            var fileName = $"{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            var filePath = Path.Combine(saveDirectory, fileName);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(playlist, options);
            File.WriteAllText(filePath, json);

            StatusTextBlock.Text = $"Playlist saved to {filePath}";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Failed to save playlist: {ex.Message}";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;
        }
    }

    private static string CreateFileSafeName(string name)
    {
        var safe = Regex.Replace(name, @"[^\w\d\-]+", "_");
        return string.IsNullOrWhiteSpace(safe) ? "playlist" : safe.Trim('_');
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class SelectableTrack : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public SelectableTrack(TrackInfo track)
        {
            Track = track;
        }

        public TrackInfo Track { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

