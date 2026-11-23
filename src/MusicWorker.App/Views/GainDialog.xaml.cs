using System.Windows;
using System.Windows.Controls;

namespace MusicWorker.App.Views;

public partial class GainDialog : Window
{
    public GainDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => ValueBox.Focus();
    }

    /// <summary>Gewählte Verstärkung in dB.</summary>
    public double GainDecibels => GainSlider.Value;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        // Eingabe im Textfeld übernehmen, auch wenn es noch den Fokus hat.
        ValueBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        DialogResult = true;
    }
}
