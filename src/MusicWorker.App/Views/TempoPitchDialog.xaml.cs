using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MusicWorker.App.Helpers;
using MusicWorker.App.Services;
using MusicWorker.Core.Effects;

namespace MusicWorker.App.Views;

/// <summary>Tempo und Tonhöhe – für beide Kanäle gemeinsam oder links/rechts getrennt.</summary>
public partial class TempoPitchDialog : Window
{
    private readonly TempoPitchOptions? _options;

    public TempoPitchDialog(TempoPitchOptions options)
    {
        InitializeComponent();
        _options = options;

        PerChannelBox.Visibility = options.IsStereo ? Visibility.Visible : Visibility.Collapsed;
        PerChannelBox.IsChecked = options.IsStereo && options.PerChannel;
        UpdateMode();
    }

    private bool IsPerChannel => _options is { IsStereo: true } && PerChannelBox.IsChecked == true;

    /// <summary>Die gewählten Werte (ohne Pro-Kanal gelten die linken Werte für alles).</summary>
    public TempoPitchRequest Result => new(
        IsPerChannel,
        Tempo(TempoLeftSlider), Semitones(PitchLeftSlider),
        Tempo(TempoRightSlider), Semitones(PitchRightSlider));

    private static double Tempo(Slider slider) => Math.Clamp(
        Math.Round(slider.Value, 1), TempoPitchEffect.MinTempoPercent, TempoPitchEffect.MaxTempoPercent);

    /// <summary>Tonhöhe, gerundet auf halbe Halbtöne.</summary>
    private static double Semitones(Slider slider) => Math.Clamp(
        Math.Round(slider.Value * 2, MidpointRounding.AwayFromZero) / 2,
        -TempoPitchEffect.MaxSemitones, TempoPitchEffect.MaxSemitones);

    private void OnModeChanged(object sender, RoutedEventArgs e) => UpdateMode();

    private void OnValuesChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateLengthText();

    private void UpdateMode()
    {
        if (_options is null || RightGroup is null || LeftGroup is null)
        {
            return; // noch in InitializeComponent
        }

        bool perChannel = IsPerChannel;
        RightGroup.Visibility = perChannel ? Visibility.Visible : Visibility.Collapsed;
        LeftGroup.Header = perChannel
            ? new TextBlock { Text = "Links", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x02, 0x77, 0xBD)) }
            : _options.IsStereo ? "Beide Kanäle" : "Tempo & Tonhöhe";
        UpdateLengthText();
    }

    /// <summary>Zeigt vorab, wie lang der Bereich danach ist – und wie viel Stille angehängt wird.</summary>
    private void UpdateLengthText()
    {
        if (_options is null || LengthText is null)
        {
            return;
        }

        var duration = _options.RangeDuration;
        var left = TimeSpan.FromSeconds(duration.TotalSeconds * 100 / Tempo(TempoLeftSlider));
        if (!IsPerChannel)
        {
            LengthText.Text = $"Länge des Bereichs: {TimeFormatter.Format(duration)} → {TimeFormatter.Format(left)}";
            return;
        }

        var right = TimeSpan.FromSeconds(duration.TotalSeconds * 100 / Tempo(TempoRightSlider));
        string text = $"Länge des Bereichs: {TimeFormatter.Format(duration)} → links {TimeFormatter.Format(left)}, " +
                      $"rechts {TimeFormatter.Format(right)}.";
        var difference = (left - right).Duration();
        if (difference.TotalMilliseconds >= 1)
        {
            string shorter = left < right ? "linke" : "rechte";
            text += $"\nDer {shorter} Kanal wird am Ende des Stücks um {TimeFormatter.Format(difference)} mit Stille verlängert.";
        }

        LengthText.Text = text;
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        TempoLeftSlider.Value = 100;
        PitchLeftSlider.Value = 0;
        TempoRightSlider.Value = 100;
        PitchRightSlider.Value = 0;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        // Eingaben in den Textfeldern übernehmen, auch wenn eines noch den Fokus hat.
        foreach (var box in new[] { TempoLeftBox, PitchLeftBox, TempoRightBox, PitchRightBox })
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        DialogResult = true;
    }
}
