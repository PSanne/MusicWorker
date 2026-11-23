using System.Windows;
using System.Windows.Controls;
using MusicWorker.Core.Effects;

namespace MusicWorker.App.Views;

public partial class TempoPitchDialog : Window
{
    public TempoPitchDialog()
    {
        InitializeComponent();
    }

    public double TempoPercent => Math.Clamp(
        Math.Round(TempoSlider.Value, 1), TempoPitchEffect.MinTempoPercent, TempoPitchEffect.MaxTempoPercent);

    /// <summary>Tonhöhe, gerundet auf halbe Halbtöne.</summary>
    public double Semitones => Math.Clamp(
        Math.Round(PitchSlider.Value * 2, MidpointRounding.AwayFromZero) / 2,
        -TempoPitchEffect.MaxSemitones, TempoPitchEffect.MaxSemitones);

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        TempoSlider.Value = 100;
        PitchSlider.Value = 0;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        TempoBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        PitchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        DialogResult = true;
    }
}
