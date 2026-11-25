using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MusicWorker.App.Views;

/// <summary>Kleines Eingabefenster für einen Text (z. B. den Namen eines Markers).</summary>
public sealed class TextInputDialog : Window
{
    private readonly TextBox _box;

    public TextInputDialog(string title, string prompt, string initialText)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _box = new TextBox { Text = initialText, Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(3) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 80 };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_box);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
            Keyboard.Focus(_box);
        };
    }

    public string Text => _box.Text;
}
