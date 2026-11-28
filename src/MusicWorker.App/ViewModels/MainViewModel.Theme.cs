using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Services;

namespace MusicWorker.App.ViewModels;

/// <summary>Design der Oberfläche (hell, dunkel, wie Windows) – das Fenster setzt es um.</summary>
public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLightTheme), nameof(IsDarkTheme), nameof(IsSystemTheme))]
    private AppTheme _theme;

    public bool IsLightTheme => Theme == AppTheme.Light;

    public bool IsDarkTheme => Theme == AppTheme.Dark;

    public bool IsSystemTheme => Theme == AppTheme.System;

    /// <summary>Design wählen: „Light“, „Dark“ oder „System“.</summary>
    [RelayCommand]
    private void SetTheme(string? name) =>
        Theme = Enum.TryParse<AppTheme>(name, ignoreCase: true, out var theme) ? theme : AppTheme.Light;

    partial void OnThemeChanged(AppTheme value)
    {
        _settings.Settings.Theme = value;
        _settings.Save();
        StatusText = value switch
        {
            AppTheme.Dark => "Dunkles Design",
            AppTheme.System => "Design wie in Windows eingestellt",
            _ => "Helles Design",
        };
    }
}
