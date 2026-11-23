using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MusicWorker.App.Services;

namespace MusicWorker.App.Views;

/// <summary>Allgemeiner Dialog zur Eingabe einer Zahl per Schieberegler oder Textfeld.</summary>
public partial class ValueDialog : Window
{
    public ValueDialog(ValueRequest request)
    {
        InitializeComponent();

        Title = request.Title;
        PromptText.Text = request.Prompt;
        HintText.Text = request.Hint;
        HintText.Visibility = string.IsNullOrEmpty(request.Hint) ? Visibility.Collapsed : Visibility.Visible;
        UnitText.Text = request.Unit;

        ValueSlider.Minimum = request.Minimum;
        ValueSlider.Maximum = request.Maximum;
        ValueSlider.SmallChange = request.SmallChange;
        ValueSlider.LargeChange = request.SmallChange * 10;
        ValueSlider.TickFrequency = (request.Maximum - request.Minimum) / 8;
        ValueSlider.Value = request.Value;

        ValueBox.SetBinding(TextBox.TextProperty, new Binding(nameof(Slider.Value))
        {
            Source = ValueSlider,
            StringFormat = request.Format,
        });

        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public double Value => ValueSlider.Value;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        // Eingabe im Textfeld übernehmen, auch wenn es noch den Fokus hat.
        ValueBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        DialogResult = true;
    }
}
