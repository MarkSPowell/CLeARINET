using System.Security.Cryptography.X509Certificates;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Proxy;
using Clearinet.ProxyCore.Sessions;

namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// The Composer tab and Replay (Fiddler Classic's "Reissue"). Both send
/// through CLeARINET's own listener with <see cref="ComposerClient"/>, so
/// what they send shows up as a new session and goes through the
/// AutoResponder, breakpoints, FiddlerScript and extensions like any other
/// traffic. Capture has to be running.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private const string DefaultComposerText =
        "GET https://example.com/ HTTP/1.1\r\n" +
        "User-Agent: CLeARINET Composer\r\n" +
        "Accept: */*\r\n" +
        "\r\n";

    private string _composerText = DefaultComposerText;
    private string _composerStatus = "Type or paste a request, then Execute. Start capture first.";
    private bool _isSending;

    /// <summary>Raised when something asks for the Composer tab to be shown (Edit in Composer).</summary>
    public event Action? ComposerShowRequested;

    /// <summary>The request being composed, as raw HTTP: a request line with an https:// URL, headers, a blank line, then any body.</summary>
    public string ComposerText
    {
        get => _composerText;
        set => SetField(ref _composerText, value ?? string.Empty);
    }

    /// <summary>What happened to the last request sent from the Composer or by Replay.</summary>
    public string ComposerStatus
    {
        get => _composerStatus;
        private set => SetField(ref _composerStatus, value);
    }

    /// <summary>Sends <see cref="ComposerText"/>.</summary>
    public RelayCommand ExecuteComposerCommand { get; }

    /// <summary>Sends the selected session's request again, unchanged.</summary>
    public RelayCommand ReplaySelectedSessionCommand { get; }

    /// <summary>Copies the selected session's request into the Composer to edit.</summary>
    public RelayCommand EditInComposerCommand { get; }

    private bool CanSend => IsRunning && !_isSending;

    private void RaiseComposerCanExecuteChanged()
    {
        ExecuteComposerCommand?.RaiseCanExecuteChanged();
        ReplaySelectedSessionCommand?.RaiseCanExecuteChanged();
        EditInComposerCommand?.RaiseCanExecuteChanged();
    }

    private async void ExecuteComposer()
    {
        if (!HttpMessageText.TryParseRequest(ComposerText, out var typed, out var parseError))
        {
            ComposerStatus = $"Not sent: {parseError}";
            return;
        }

        await SendAsync(typed!, "Composer");
    }

    private async void ReplaySelectedSession()
    {
        if (SelectedSessionRow?.Session is not { } session)
        {
            return;
        }

        var request = session.Request with { Target = SessionUrl.Of(session) };
        await SendAsync(request, $"Replay of #{session.Id}");
        StatusText = ComposerStatus;
    }

    private void EditInComposer()
    {
        if (SelectedSessionRow?.Session is not { } session)
        {
            return;
        }

        ComposerText = HttpMessageText.Format(session.Request with { Target = SessionUrl.Of(session) });
        ComposerStatus = $"Loaded #{session.Id}. Edit it, then Execute.";
        ComposerShowRequested?.Invoke();
    }

    private async Task SendAsync(CapturedRequest typed, string what)
    {
        if (_proxy is not { } listener || _authority is null)
        {
            ComposerStatus = $"{what} not sent: start capture first. Requests are sent through CLeARINET so they're captured.";
            return;
        }

        if (!ComposerClient.TryResolve(typed, out var url, out var toSend, out var error))
        {
            ComposerStatus = $"{what} not sent: {error}";
            return;
        }

        _isSending = true;
        RaiseComposerCanExecuteChanged();
        ComposerStatus = $"{what}: sending {toSend!.Method} {url}...";
        try
        {
            var rootDer = _authority.RootCertificate.Export(X509ContentType.Cert);
            // Generous: a breakpoint may hold the request while someone edits it.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var response = await Task.Run(() => ComposerClient.SendAsync(listener.Port, rootDer, url!, toSend, timeout.Token));
            ComposerStatus = $"{what}: {response.StatusCode} {response.ReasonPhrase} from {toSend.Method} {url}. It's the newest session in the list.";
        }
        catch (Exception ex)
        {
            ComposerStatus = $"{what} failed: {ex.Message}";
        }
        finally
        {
            _isSending = false;
            RaiseComposerCanExecuteChanged();
        }
    }
}
