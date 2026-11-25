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
        Waveform.ViewChanged += (_, _) => Spectrogram.SetView(Waveform.VisibleStart, Waveform.VisibleLength); // gleicher Ausschnitt
        viewModel.Mixer.PropertyChanged += OnMixerPropertyChanged;
        Loaded += (_, _) => Waveform.Focus();
        Deactivated += (_, _) => viewModel.Notes.ReleaseAllKeys(); // keine „hängenden“ Töne beim Fensterwechsel
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
    /// Im Mischpult gelten eigene Kürzel (Strg+Z/Y = Rückgängig/Wiederholen, Strg+T = Clip teilen);
    /// die übrigen Editor-Kürzel werden nicht an den unsichtbaren Editor weitergegeben.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        bool typing = Keyboard.FocusedElement is TextBoxBase;
        if (_viewModel.IsNotesTab && !typing)
        {
            HandleNotesKey(e);
            return;
        }

        // Bild↑ / Bild↓: vorheriger / nächster Marker (Editor und Mischpult).
        if (!typing && Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.PageUp or Key.PageDown &&
            Keyboard.FocusedElement is not ComboBox)
        {
            var command = e.Key == Key.PageUp ? _viewModel.PreviousMarkerKeyCommand : _viewModel.NextMarkerKeyCommand;
            command.Execute(null);
            e.Handled = true;
            return;
        }

        if (!_viewModel.IsMixerTab || typing || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        var mixer = _viewModel.Mixer;
        switch (e.Key)
        {
            case Key.Z:
                Run(mixer.UndoCommand);
                e.Handled = true;
                break;
            case Key.Y:
                Run(mixer.RedoCommand);
                e.Handled = true;
                break;
            case Key.T:
                Run(mixer.SplitClipCommand);
                e.Handled = true;
                break;
            case Key.C:
                Run(mixer.CopyClipsCommand);
                e.Handled = true;
                break;
            case Key.X:
                Run(mixer.CutClipsCommand);
                e.Handled = true;
                break;
            case Key.V:
                Run(mixer.PasteClipsCommand);
                e.Handled = true;
                break;
            case Key.A:
                Run(mixer.SelectAllClipsCommand);
                e.Handled = true;
                break;
            case Key.D:
                Run(mixer.DuplicateClipsCommand);
                e.Handled = true;
                break;
            case Key.E:
                e.Handled = true;
                break;
        }

        static void Run(ICommand command)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (_viewModel.IsNotesTab && _viewModel.Notes.PitchForKey(e.Key.ToString()) is int pitch and >= 0)
        {
            _viewModel.Notes.KeyUp(pitch);
        }
    }

    /// <summary>
    /// Noten-Reiter: Strg+Z/Y/A/C/X/V gelten für die Noten, Pfeil hoch/runter transponiert die Auswahl
    /// (mit Umschalt um eine Oktave), die Buchstabentasten spielen die Bildschirmtastatur.
    /// </summary>
    private void HandleNotesKey(KeyEventArgs e)
    {
        var notes = _viewModel.Notes;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Z:
                    notes.Undo();
                    break;
                case Key.Y:
                    notes.Redo();
                    break;
                case Key.A:
                    notes.SelectAllCommand.Execute(null);
                    break;
                case Key.C:
                    notes.Copy();
                    break;
                case Key.X:
                    notes.Cut();
                    break;
                case Key.V:
                    notes.Paste();
                    break;
                case Key.T or Key.E:
                    break; // Editor-Kürzel hier nicht weitergeben
                default:
                    return;
            }

            e.Handled = true;
            return;
        }

        if (e.Key is Key.Up or Key.Down && notes.Selection.Count > 0 && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            int step = (Keyboard.Modifiers == ModifierKeys.Shift ? 12 : 1) * (e.Key == Key.Up ? 1 : -1);
            notes.TransposeCommand.Execute(step.ToString(System.Globalization.CultureInfo.InvariantCulture));
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && notes.PitchForKey(e.Key.ToString()) is int pitch and >= 0)
        {
            if (!e.IsRepeat)
            {
                notes.KeyDown(pitch);
            }

            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None || Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        if (e.Key == Key.M)
        {
            _viewModel.AddMarkerKeyCommand.Execute(null); // Marker setzen (im Noten-Reiter spielt M einen Ton)
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Space)
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

    private void OnShortcutsClick(object sender, RoutedEventArgs e) => new Views.ShortcutsWindow { Owner = this }.Show();

    private void OnZoomInClick(object sender, RoutedEventArgs e) => Waveform.ZoomIn();

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => Waveform.ZoomOut();

    private void OnZoomFitClick(object sender, RoutedEventArgs e) => Waveform.ZoomToFit();

    private void OnZoomSelectionClick(object sender, RoutedEventArgs e) => Waveform.ZoomToSelection();

    private void OnTimelineZoomInClick(object sender, RoutedEventArgs e) => Timeline.ZoomIn();

    private void OnTimelineZoomOutClick(object sender, RoutedEventArgs e) => Timeline.ZoomOut();

    private void OnTimelineZoomFitClick(object sender, RoutedEventArgs e) => Timeline.ZoomToFit();

    private void OnPianoRollZoomInClick(object sender, RoutedEventArgs e) => PianoRoll.ZoomIn();

    private void OnPianoRollZoomOutClick(object sender, RoutedEventArgs e) => PianoRoll.ZoomOut();

    private void OnPianoRollZoomFitClick(object sender, RoutedEventArgs e) => PianoRoll.ZoomToFit();

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

    /// <summary>Eingabe in einem Textfeld (z. B. Tempo) mit der Eingabetaste übernehmen.</summary>
    private void OnCommitTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            textBox.SelectAll();
            e.Handled = true;
        }
    }

    // ───────────── Spuren per Ziehen umsortieren ─────────────

    private Point? _trackDragStart;
    private TrackViewModel? _trackDragTrack;

    private void OnTrackHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrackViewModel track } element && e.OriginalSource is not ToggleButton)
        {
            _trackDragStart = e.GetPosition(element);
            _trackDragTrack = track;
        }
    }

    private void OnTrackHeaderMouseMove(object sender, MouseEventArgs e)
    {
        if (_trackDragTrack is not { } track || _trackDragStart is not { } start || sender is not FrameworkElement element ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            _trackDragStart = null;
            return;
        }

        var position = e.GetPosition(element);
        if (Math.Abs(position.Y - start.Y) < 6 && Math.Abs(position.X - start.X) < 6)
        {
            return;
        }

        _trackDragStart = null;
        DragDrop.DoDragDrop(element, new DataObject(typeof(TrackViewModel), track), DragDropEffects.Move);
        _trackDragTrack = null;
    }

    private void OnTrackHeaderDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TrackViewModel)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnTrackHeaderDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TrackViewModel)) is TrackViewModel dragged && sender is FrameworkElement { DataContext: TrackViewModel target })
        {
            var command = _viewModel.Mixer.MoveTrackCommand;
            var move = new TrackMove(dragged, _viewModel.Mixer.Tracks.IndexOf(target));
            if (command.CanExecute(move))
            {
                command.Execute(move);
            }

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
