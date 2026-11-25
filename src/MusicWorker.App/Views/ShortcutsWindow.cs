using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MusicWorker.App.Views;

/// <summary>Übersicht aller Tastenkürzel und Mausaktionen (Menü „Hilfe → Tastenkürzel“).</summary>
public sealed class ShortcutsWindow : Window
{
    private static readonly (string Group, (string Keys, string Action)[] Items)[] Shortcuts =
    [
        ("Allgemein",
        [
            ("Strg+O", "Datei öffnen"),
            ("Strg+E", "Exportieren (Editor)"),
            ("Strg+S", "Projekt speichern (Mischpult) bzw. exportieren (Editor)"),
            ("Strg+Z / Strg+Y", "Rückgängig / Wiederholen – gilt für den sichtbaren Reiter"),
            ("F1", "Handbuch öffnen"),
            ("Datei hineinziehen", "Im Editor öffnen bzw. im Mischpult als Spur anlegen"),
        ]),
        ("Wiedergabe",
        [
            ("Leertaste", "Abspielen / Pause"),
            ("Esc", "Stopp"),
            ("Strg+← / Strg+→", "5 Sekunden zurück / vor"),
            ("Strg+L", "Schleife (Loop) ein/aus"),
            ("Pos1 / Ende", "Zum Anfang / zum Ende (Editor)"),
            ("Klick in die Zeitleiste", "Abspielposition setzen (roten Griff ▼ ziehen)"),
            ("Strg+R", "Aufnahme starten/beenden (Mischpult)"),
        ]),
        ("Marker",
        [
            ("M", "Marker an der aktuellen Position setzen"),
            ("Bild↑ / Bild↓", "Zum vorherigen / nächsten Marker"),
            ("Fähnchen anklicken", "Zum Marker springen"),
            ("Fähnchen ziehen", "Marker verschieben"),
            ("Doppelklick aufs Fähnchen", "Marker umbenennen"),
            ("Rechtsklick aufs Fähnchen", "Menü: springen, umbenennen, löschen"),
        ]),
        ("Editor",
        [
            ("Klick / Ziehen", "Cursor setzen / Bereich markieren"),
            ("Umschalt+Klick", "Auswahl erweitern"),
            ("Strg+X / Strg+C / Strg+V", "Ausschneiden / Kopieren / Einfügen"),
            ("Entf", "Auswahl löschen"),
            ("Strg+A", "Alles auswählen"),
            ("Strg+T", "Auf Auswahl zuschneiden"),
            ("Strg+Umschalt+E", "Live-Effekte ein-/ausblenden"),
            ("Mausrad / Strg+Mausrad", "Scrollen / Zoomen"),
            ("Doppelklick auf einen Regler", "Regler auf den Standardwert zurücksetzen"),
        ]),
        ("Mischpult",
        [
            ("Clip ziehen", "Clip verschieben (auch auf eine andere Spur)"),
            ("Strg+Klick auf Clips", "Mehrere Clips wählen – Ziehen verschiebt dann alle"),
            ("Ziehen in eine freie Fläche", "Auswahlrahmen: alle Clips im Rahmen wählen"),
            ("Strg+A", "Alle Clips wählen"),
            ("Strg+C / Strg+X / Strg+V", "Clips kopieren / ausschneiden / an der Abspielposition einfügen"),
            ("Strg+D", "Gewählte Clips direkt dahinter wiederholen"),
            ("Weiße Griffe oben am Clip", "Einblenden (links) / Ausblenden (rechts) ziehen"),
            ("Rechtsklick auf einen Clip", "Clip-Menü: kopieren, einfügen, Fades zurücksetzen, löschen …"),
            ("Doppelklick auf einen Clip", "Clip im Editor bzw. Noten-Editor öffnen"),
            ("Entf", "Gewählte Clips löschen"),
            ("Strg+T", "Clip an der Abspielposition teilen"),
            ("Umschalt+Ziehen", "Loop-Bereich festlegen"),
            ("Spurname ziehen", "Reihenfolge der Spuren ändern"),
            ("Rechtsklick auf den Spurnamen", "Farbe, Höhe, nach oben/unten"),
            ("Hüllkurven-Modus: Klick", "Punkt setzen (Doppel- oder Rechtsklick löscht ihn)"),
        ]),
        ("Noten",
        [
            ("Y S X D C V G B H N J M", "Töne der unteren Oktave spielen"),
            ("Q 2 W 3 E R 5 T 6 Z 7 U", "Töne der oberen Oktave spielen"),
            ("Pfeil ↑ / ↓", "Auswahl um einen Halbton verschieben (mit Umschalt: Oktave)"),
            ("Strg+A / Strg+C / Strg+X / Strg+V", "Noten auswählen, kopieren, ausschneiden, einfügen"),
            ("Entf", "Gewählte Noten löschen"),
        ]),
    ];

    public ShortcutsWindow()
    {
        Title = "Tastenkürzel – MusicWorker";
        Width = 640;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(16) };
        foreach (var (group, items) in Shortcuts)
        {
            panel.Children.Add(new TextBlock
            {
                Text = group,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 14, 0, 6),
            });

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < items.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var keys = new TextBlock
                {
                    Text = items[i].Keys,
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 2, 12, 2),
                    TextWrapping = TextWrapping.Wrap,
                };
                var action = new TextBlock { Text = items[i].Action, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(keys, i);
                Grid.SetRow(action, i);
                Grid.SetColumn(action, 1);
                grid.Children.Add(keys);
                grid.Children.Add(action);
            }

            panel.Children.Add(grid);
        }

        var close = new Button { Content = "Schließen", IsCancel = true, IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
