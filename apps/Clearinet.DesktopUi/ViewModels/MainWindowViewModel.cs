using System.Collections.ObjectModel;
using Avalonia.Threading;
using Clearinet.DesktopUi.Models;
using Clearinet.ProxyCore.Certificates;
using Clearinet.ProxyCore.Proxy;
using Clearinet.ProxyCore.Sessions;

namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// Hosts the same proxy core Clearinet.DevHost's console spike proved out
/// in Phase 1 (root CA, leaf certificate provider, session store,
/// intercepting listener) and projects <see cref="SessionStore"/>'s live
/// change feed into an <see cref="ObservableCollection{T}"/> the session
/// list binds to. The proxy is started once, from the constructor, for the
/// lifetime of the window -- there's no start/stop toggle yet; that's a
/// natural next step once this basic list is confirmed working end to end.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const int Port = 8888;

    private readonly SessionStore _sessionStore = new();
    private readonly InterceptingProxyListener? _proxy;
    private string _statusText = "Starting...";

    public ObservableCollection<SessionRow> Sessions { get; } = [];

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public RelayCommand SaveSazCommand { get; }

    public MainWindowViewModel()
    {
        SaveSazCommand = new RelayCommand(SaveSaz, () => Sessions.Count > 0);

        // SessionAdded fires on whichever proxy connection thread just
        // finished a request/response pair -- never the UI thread -- so
        // every touch of Sessions (an Avalonia-bound collection) has to be
        // marshaled through Dispatcher.UIThread first.
        _sessionStore.SessionAdded += session => Dispatcher.UIThread.Post(() =>
        {
            Sessions.Add(SessionRow.From(session));
            SaveSazCommand.RaiseCanExecuteChanged();
        });

        if (!OperatingSystem.IsWindows())
        {
            StatusText =
                "This build only implements the Windows certificate trust-store path so far " +
                "-- see the Interception Certificate Design doc's open risks.";
            return;
        }

        try
        {
            var authority = new CertificateAuthority();
            var leafProvider = new LeafCertificateProvider(authority.RootCertificate);
            _proxy = new InterceptingProxyListener(Port, leafProvider, _sessionStore);
            _proxy.Start();
            StatusText =
                $"Listening on 127.0.0.1:{Port}. Point a browser's HTTPS proxy here " +
                "(not the Windows system proxy -- that routes this machine's other apps through it too).";
        }
        catch (Exception ex)
        {
            // Most likely cause in practice: another CLeARINET process (the
            // console dev host, or a previous run of this app) is already
            // bound to the same port.
            StatusText = $"Failed to start the proxy on port {Port}: {ex.Message}";
        }
    }

    private void SaveSaz()
    {
        var sessions = _sessionStore.Snapshot();
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"clearinet-capture-{DateTime.Now:yyyyMMdd-HHmmss}.saz");
            SazWriter.Write(path, sessions);
            StatusText = $"Saved {sessions.Count} session(s) to {path}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to save SAZ: {ex.Message}";
        }
    }

    public void Dispose() => _proxy?.Stop();
}
