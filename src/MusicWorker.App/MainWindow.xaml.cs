using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
        viewModel.Mixer.PropertyChanged += OnMixerPropertyChanged;
        Loaded += (_, _) => Waveform.Focus();
        Closed += (_, _) =>
        {
            viewModel.Mixer.PropertyChanged -= OnMixerPropertyChanged;
            viewModel.Dispose();
        };
    }

    private void OnMixerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Neues oder geöffnetes Projekt: ganze Länge zeigen.
        if (e.PropertyName == nameof(MixerViewModel.ProjectPath) || (e.PropertyName == nameof(MixerViewModel.HasTracks) && !_viewModel.Mixer.HasTracks))
        {
            Timeline.ZoomToFit();
        }
    }

    /// <summary>
    /// Leertaste = Abspielen/Pause – aber nicht, während in ein Textfeld (z. B. Spurname) getippt wird.
    /// Im Mischpult werden außerdem die Editor-Tastenkürzel (Strg+Z, Strg+X …) nicht an den
    /// unsichtbaren Editor weitergegeben.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        bool typing = Keyboard.FocusedElement is TextBoxBase;

        if (_viewModel.IsMixerTab && !typing && Keyboard.Modifiers == ModifierKeys.Control &&
            e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.A or Key.T or Key.E)
        {
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None ||
            Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        if (_viewModel.TransportToggleCommand.CanExecute(null))
        {
            _viewModel.TransportToggleCommand.Execute(null);
            e.Handled = true;
        }
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

    private void OnTimelineZoomInClick(object sender, RoutedEventArgs e) => Timeline.ZoomIn();

    private void OnTimelineZoomOutClick(object sender, RoutedEventArgs e) => Timeline.ZoomOut();

    private void OnTimelineZoomFitClick(object sender, RoutedEventArgs e) => Timeline.ZoomToFit();

    /// <summary>Doppelklick auf einen Regler/Fader setzt ihn auf 0 (dB).</summary>
    private void OnEqSliderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            // SetCurrentValue behält die Datenbindung bei.
            slider.SetCurrentValue(RangeBase.ValueProperty, 0.0);
            e.Handled = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Im Mischpult werden abgelegte Dateien zu Spuren, im Editor wird die erste geöffnet.</summary>
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            await _viewModel.HandleDroppedFilesAsync(files);
        }
    }
}
