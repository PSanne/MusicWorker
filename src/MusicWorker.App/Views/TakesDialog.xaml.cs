using System.Windows;
using System.Windows.Threading;
using MusicWorker.App.Audio;
using MusicWorker.App.Services;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Recording;
using NAudio.Wave;

namespace MusicWorker.App.Views;

/// <summary>
/// Takes &amp; Comping: zeigt alle Aufnahmen eines Clips übereinander. Über einen Take ziehen = diesen Abschnitt
/// verwenden; „Clear-Comp“ setzt die Zusammenstellung auf den gewählten Take zurück.
/// </summary>
public partial class TakesDialog : Window
{
    private readonly PlaybackService _playback = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private IReadOnlyList<AudioBuffer> _takes;
    private long _position;

    public TakesDialog(TakesRequest request)
    {
        InitializeComponent();
        _takes = request.Takes;
        _playback.DeviceNumber = request.OutputDevice;
        IntroText.Text = $"Takes von „{request.TrackName}“: Ziehe mit der Maus über einen Take, um diesen Abschnitt in die Zusammenstellung " +
            "(hell hinterlegt) zu übernehmen. Ein Klick wählt den Take und setzt die Abspielposition.";
        Lanes.Takes = request.Takes;
        Lanes.Comp = request.Comp;
        Lanes.SelectedTake = request.Takes.Count - 1;
        Lanes.CompChanged += (_, _) => Restart();
        Lanes.PositionClicked += (_, frame) =>
        {
            _position = frame;
            Lanes.PlayheadFrame = frame;
            Lanes.InvalidateVisual();
            Restart();
        };
        _timer.Tick += (_, _) =>
        {
            if (_playback.State == PlaybackState.Playing)
            {
                Lanes.PlayheadFrame = _playback.CurrentFrame;
                Lanes.InvalidateVisual();
            }
            else
            {
                StopPlayback();
            }
        };
    }

    public TakesResult? Result { get; private set; }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (_playback.State == PlaybackState.Playing)
        {
            StopPlayback();
            return;
        }

        StartPlayback();
    }

    private void StartPlayback()
    {
        try
        {
            var audio = TakeTools.Compose(_takes, Lanes.Comp);
            int start = (int)Math.Clamp(_position, 0, Math.Max(0, audio.FrameCount - 1));
            _playback.Play(audio, start, audio.FrameCount);
            PlayButton.Content = "■ Stopp";
            _timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Die Wiedergabe konnte nicht gestartet werden.\n\n{ex.Message}", "MusicWorker");
        }
    }

    private void StopPlayback()
    {
        _timer.Stop();
        _playback.Stop();
        PlayButton.Content = "▶ Anhören";
    }

    /// <summary>Läuft die Wiedergabe, mit der geänderten Zusammenstellung neu starten.</summary>
    private void Restart()
    {
        if (_playback.State == PlaybackState.Playing)
        {
            _position = _playback.CurrentFrame;
            StartPlayback();
        }
    }

    private void OnClearCompClick(object sender, RoutedEventArgs e)
    {
        Lanes.Comp = TakeTools.ClearComp(TakeTools.LengthOf(_takes), Lanes.SelectedTake);
        Restart();
    }

    private void OnDeleteTakeClick(object sender, RoutedEventArgs e)
    {
        if (_takes.Count <= 1)
        {
            return;
        }

        var (takes, comp) = TakeTools.RemoveTake(_takes, Lanes.Comp, Lanes.SelectedTake);
        _takes = takes;
        Lanes.Takes = takes;
        Lanes.Comp = comp;
        Restart();
    }

    private void OnFlattenClick(object sender, RoutedEventArgs e)
    {
        Result = new TakesResult(_takes, Lanes.Comp, Flatten: true);
        DialogResult = true;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Result = new TakesResult(_takes, Lanes.Comp, Flatten: false);
        DialogResult = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _playback.Dispose();
    }
}
