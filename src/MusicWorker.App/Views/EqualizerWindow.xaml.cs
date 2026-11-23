using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MusicWorker.App.Views;

public partial class EqualizerWindow : Window
{
    public EqualizerWindow()
    {
        InitializeComponent();
    }

    private void OnApplyClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnSliderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            // SetCurrentValue behält die Datenbindung bei.
            slider.SetCurrentValue(RangeBase.ValueProperty, 0.0);
            e.Handled = true;
        }
    }
}
