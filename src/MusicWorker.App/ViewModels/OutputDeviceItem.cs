using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MusicWorker.App.ViewModels;

/// <summary>Ein Eintrag im Menü „Wiedergabegerät“.</summary>
public sealed partial class OutputDeviceItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    private bool _isSelected;

    public OutputDeviceItem(int number, string name, ICommand select)
    {
        Number = number;
        Name = name;
        SelectCommand = select;
    }

    public int Number { get; }

    public string Name { get; }

    public ICommand SelectCommand { get; }

    /// <summary>Menütext mit Häkchen vor dem gewählten Gerät.</summary>
    public string Header => (IsSelected ? "✓  " : "     ") + Name.Replace("_", "__");
}
