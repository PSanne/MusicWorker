using System.Windows.Threading;
using MusicWorker.App.ViewModels;
using MusicWorker.Core.Persistence;

namespace MusicWorker.App.Services;

/// <summary>Löst in regelmäßigen Abständen die automatische Sicherung aus (im UI-Thread, schreibt im Hintergrund).</summary>
public sealed class AutosaveService : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly RecoverySession _session;
    private readonly DispatcherTimer _timer;
    private bool _running;

    public AutosaveService(MainViewModel viewModel, RecoverySession session, TimeSpan interval)
    {
        _viewModel = viewModel;
        _session = session;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
        _timer.Tick += async (_, _) => await SaveNowAsync();
        _timer.Start();
    }

    public async Task SaveNowAsync()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        try
        {
            await _viewModel.AutosaveAsync(_session);
        }
        catch (Exception)
        {
            // Die Sicherung darf die Arbeit nie unterbrechen.
        }
        finally
        {
            _running = false;
        }
    }

    public void Dispose() => _timer.Stop();
}
