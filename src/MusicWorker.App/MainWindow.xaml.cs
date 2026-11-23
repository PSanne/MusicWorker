using System.ComponentModel;
using System.Windows;
using MusicWorker.App.ViewModels;

namespace MusicWorker.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.DocumentLoaded += (_, _) => Waveform.ZoomToFit();
        Loaded += (_, _) => Waveform.Focus();
        Closed += (_, _) => viewModel.Dispose();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.ConfirmClose())
        {
            e.Cancel = true;
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnZoomInClick(object sender, RoutedEventArgs e) => Waveform.ZoomIn();

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => Waveform.ZoomOut();

    private void OnZoomFitClick(object sender, RoutedEventArgs e) => Waveform.ZoomToFit();

    private void OnZoomSelectionClick(object sender, RoutedEventArgs e) => Waveform.ZoomToSelection();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            await _viewModel.OpenFileAsync(files[0]);
        }
    }
}
