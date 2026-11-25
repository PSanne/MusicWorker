using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MusicWorker.App.Services;
using MusicWorker.Core.Notes;

namespace MusicWorker.App.Views;

/// <summary>
/// Schlagzeug-Raster: Zeilen = Instrumente (Bass-Trommel, Snare, Hi-Hat …), Spalten = Sechzehntel eines Takts.
/// Ein Klick setzt bzw. entfernt einen Schlag; fertige Rhythmen lassen sich als Ausgangspunkt wählen.
/// </summary>
public sealed class DrumGridWindow : Window
{
    private static readonly Brush OnBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x3D)));
    private static readonly Brush BeatBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xEC)));
    private static readonly Brush OffBeatBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF6, 0xF7, 0xF9)));

    private readonly DrumPattern _pattern;
    private readonly ToggleButton[,] _cells;
    private readonly ComboBox _repeats;
    private readonly CheckBox _replace;
    private bool _loading;

    public DrumGridWindow(DrumGridRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        int steps = Math.Max(1, request.BeatsPerBar) * 4;
        _pattern = Adapt(request.Pattern, steps);
        _cells = new ToggleButton[DrumPattern.Sounds.Count, steps];

        Title = "Schlagzeug-Raster";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = $"Klicke in die Kästchen, um Schläge zu setzen – jede Spalte ist eine Sechzehntelnote, vier Spalten sind ein Schlag. Eingefügt wird ab Takt {request.StartBar}.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
        });

        if (!request.IsDrumTrack)
        {
            root.Children.Add(new TextBlock
            {
                Text = "Hinweis: Diese Spur hat noch kein Schlagzeug als Instrument. Wähle im Noten-Editor unter „Instrument“ das Schlagzeug, damit die Schläge auch so klingen.",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 640,
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = Brushes.DarkOrange,
                Margin = new Thickness(0, 0, 0, 10),
            });
        }

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(new TextBlock { Text = "Fertiger Rhythmus:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var presets = new ComboBox { MinWidth = 180 };
        presets.Items.Add("(eigener Rhythmus)");
        foreach (var (name, _) in DrumPattern.Presets)
        {
            presets.Items.Add(name);
        }

        presets.SelectedIndex = 0;
        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedIndex > 0)
            {
                Load(Adapt(DrumPattern.Presets[presets.SelectedIndex - 1].Pattern, steps));
            }
        };
        top.Children.Add(presets);
        var clear = new Button { Content = "Leeren", Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        clear.Click += (_, _) =>
        {
            presets.SelectedIndex = 0;
            Load(new DrumPattern(steps));
        };
        top.Children.Add(clear);
        root.Children.Add(top);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int c = 0; c < steps; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < steps; c += 4)
        {
            var beat = new TextBlock { Text = ((c / 4) + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 2) };
            Grid.SetColumn(beat, c + 1);
            grid.Children.Add(beat);
        }

        for (int s = 0; s < DrumPattern.Sounds.Count; s++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = DrumPattern.Sounds[s].Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetRow(label, s + 1);
            grid.Children.Add(label);
            for (int c = 0; c < steps; c++)
            {
                int sound = s;
                int step = c;
                var cell = new ToggleButton
                {
                    Width = 26,
                    Height = 24,
                    Margin = new Thickness(c % 4 == 0 ? 4 : 1, 1, 0, 1),
                    Background = (c / 4) % 2 == 0 ? BeatBrush : OffBeatBrush,
                    BorderBrush = Brushes.Silver,
                    ToolTip = $"{DrumPattern.Sounds[s].Name} – Schlag {(c / 4) + 1}, {(c % 4) + 1}. Sechzehntel",
                };
                cell.Checked += (_, _) => Toggle(cell, sound, step, true, presets);
                cell.Unchecked += (_, _) => Toggle(cell, sound, step, false, presets);
                _cells[s, c] = cell;
                Grid.SetRow(cell, s + 1);
                Grid.SetColumn(cell, c + 1);
                grid.Children.Add(cell);
            }
        }

        root.Children.Add(grid);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        bottom.Children.Add(new TextBlock { Text = "Wiederholen für", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _repeats = new ComboBox { Width = 60 };
        foreach (int n in new[] { 1, 2, 4, 8, 12, 16, 32 })
        {
            _repeats.Items.Add(n);
        }

        _repeats.SelectedItem = _repeats.Items.Contains(request.Repeats) ? request.Repeats : 4;
        bottom.Children.Add(_repeats);
        bottom.Children.Add(new TextBlock { Text = "Takte", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 16, 0) });
        _replace = new CheckBox
        {
            Content = "Vorhandene Schläge in diesem Bereich ersetzen",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        bottom.Children.Add(_replace);
        root.Children.Add(bottom);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "Einfügen", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) =>
        {
            Result = new DrumGridResult(_pattern.Clone(), _repeats.SelectedItem is int r ? r : 4, _replace.IsChecked == true);
            DialogResult = true;
        };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 90 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
        Load(_pattern);
    }

    public DrumGridResult? Result { get; private set; }

    /// <summary>Muster auf eine andere Schrittzahl bringen (z. B. 4/4-Rhythmus in einem 3/4-Takt) – es wird wiederholt bzw. gekürzt.</summary>
    private static DrumPattern Adapt(DrumPattern source, int steps)
    {
        var pattern = new DrumPattern(steps);
        for (int s = 0; s < DrumPattern.Sounds.Count; s++)
        {
            for (int i = 0; i < steps; i++)
            {
                pattern[s, i] = source[s, i % source.StepCount];
            }
        }

        return pattern;
    }

    private void Load(DrumPattern pattern)
    {
        _loading = true;
        for (int s = 0; s < DrumPattern.Sounds.Count; s++)
        {
            for (int i = 0; i < _pattern.StepCount; i++)
            {
                _pattern[s, i] = pattern[s, i];
                _cells[s, i].IsChecked = pattern[s, i];
                Paint(_cells[s, i], i, pattern[s, i]);
            }
        }

        _loading = false;
    }

    private void Toggle(ToggleButton cell, int sound, int step, bool on, ComboBox presets)
    {
        _pattern[sound, step] = on;
        Paint(cell, step, on);
        if (!_loading)
        {
            presets.SelectedIndex = 0; // eigenes Muster
        }
    }

    private static void Paint(ToggleButton cell, int step, bool on) =>
        cell.Background = on ? OnBrush : (step / 4) % 2 == 0 ? BeatBrush : OffBeatBrush;

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
