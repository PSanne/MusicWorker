using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MusicWorker.App.Services;

namespace MusicWorker.App.Views;

/// <summary>Dialog mit mehreren Einstellungen: Schieberegler (mit Zahlenfeld) und Auswahllisten, aus einer Beschreibung erzeugt.</summary>
public partial class ParametersDialog : Window
{
    private readonly List<Func<double>> _readers = [];

    public ParametersDialog(ParameterRequest request)
    {
        InitializeComponent();
        Title = request.Title;
        IntroText.Text = request.Intro;
        IntroText.Visibility = string.IsNullOrEmpty(request.Intro) ? Visibility.Collapsed : Visibility.Visible;

        foreach (var parameter in request.Parameters)
        {
            Host.Children.Add(new TextBlock { Text = parameter.Label, Margin = new Thickness(0, 10, 0, 4), FontWeight = FontWeights.SemiBold });
            switch (parameter)
            {
                case SliderParameter slider:
                    AddSlider(slider);
                    break;
                case ChoiceParameter choice:
                    var combo = new ComboBox { ItemsSource = choice.Options, SelectedIndex = Math.Clamp(choice.Selected, 0, choice.Options.Count - 1), Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
                    Host.Children.Add(combo);
                    _readers.Add(() => combo.SelectedIndex);
                    break;
            }

            if (!string.IsNullOrEmpty(parameter.Hint))
            {
                Host.Children.Add(new TextBlock { Text = parameter.Hint, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, MaxWidth = 460, Margin = new Thickness(0, 3, 0, 0) });
            }
        }
    }

    /// <summary>Werte in der Reihenfolge der Parameter (Auswahllisten: Index).</summary>
    public double[] Values { get; private set; } = [];

    private void AddSlider(SliderParameter parameter)
    {
        var panel = new DockPanel();
        var unit = new TextBlock { Text = parameter.Unit, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 40 };
        var box = new TextBox { Width = 64, Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = parameter.Minimum,
            Maximum = parameter.Maximum,
            Value = parameter.Value,
            SmallChange = parameter.Step,
            LargeChange = parameter.Step * 10,
            TickFrequency = parameter.Step,
            IsSnapToTickEnabled = true,
            IsMoveToPointEnabled = true,
            Width = 280,
        };
        box.SetBinding(TextBox.TextProperty, new Binding(nameof(Slider.Value))
        {
            Source = slider,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
            StringFormat = parameter.Step < 1 ? "0.##" : "0",
            ConverterCulture = CultureInfo.CurrentCulture,
        });
        DockPanel.SetDock(unit, Dock.Right);
        DockPanel.SetDock(box, Dock.Right);
        panel.Children.Add(unit);
        panel.Children.Add(box);
        panel.Children.Add(slider);
        Host.Children.Add(panel);
        _readers.Add(() =>
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            return slider.Value;
        });
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Values = _readers.Select(read => read()).ToArray();
        DialogResult = true;
    }
}
