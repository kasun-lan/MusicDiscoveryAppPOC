using System.Collections.Generic;
using System.Windows;
using MusicDiscoveryAppPOC.Models;

namespace MusicDiscoveryAppPOC;

public partial class SelectedTracksWindow : Window
{
    public SelectedTracksWindow(IReadOnlyList<TrackInfo> tracks)
    {
        InitializeComponent();
        TracksGrid.ItemsSource = tracks;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}


