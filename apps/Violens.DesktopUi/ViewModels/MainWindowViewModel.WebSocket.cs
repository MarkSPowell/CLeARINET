using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia.Threading;
using Violens.AppCore.Compare;
using Violens.ProxyCore.Sessions;
using Violens.AppCore.Text;
using Violens.ProxyCore.WebSockets;

namespace Violens.DesktopUi.ViewModels;

/// <summary>One line in the WebSocket tab's message list.</summary>
public sealed class WebSocketMessageRow(WebSocketMessage message, DateTimeOffset openedAt)
{
    public WebSocketMessage Message { get; } = message;

    public int Number => Message.Number;

    /// <summary>↑ sent by the client, ↓ received from the server, as browsers' developer tools show them.</summary>
    public string Direction => Message.Direction == WebSocketDirection.Sent ? "↑" : "↓";

    /// <summary>Seconds since the connection opened.</summary>
    public string Time =>
        "+" + Math.Max(0, (Message.Time - openedAt).TotalSeconds).ToString("0.000", CultureInfo.InvariantCulture) + " s";

    public string Kind => Message.Kind switch
    {
        WebSocketMessageKind.Text => "text",
        WebSocketMessageKind.Binary => "binary",
        WebSocketMessageKind.Close => Message.CloseCode is { } code ? $"close {code}" : "close",
        WebSocketMessageKind.Ping => "ping",
        WebSocketMessageKind.Pong => "pong",
        _ => Message.Kind.ToString(),
    };

    public string Size =>
        FormatSize(Message.Length) + (Message.Compressed ? $" → {FormatSize(Message.Payload.Length)}{(Message.Truncated ? "+" : string.Empty)}" : string.Empty);

    /// <summary>The start of the message, on one line.</summary>
    public string Preview
    {
        get
        {
            if (Message.Note is not null && Message.Payload.Length == 0)
            {
                return Message.Note;
            }

            var text = Message.Kind switch
            {
                WebSocketMessageKind.Binary => Convert.ToHexString(Message.Payload.AsSpan(0, Math.Min(32, Message.Payload.Length))),
                WebSocketMessageKind.Close => Message.CloseReason ?? WebSocketMessage.CloseCodeName(Message.CloseCode ?? 0) ?? string.Empty,
                _ => Message.Payload.Length > 600 ? Encoding.UTF8.GetString(Message.Payload, 0, 600) : Message.Text,
            };

            text = text.ReplaceLineEndings(" ");
            return text.Length > 200 ? text[..200] + "…" : text;
        }
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB",
        _ => (bytes / (1024.0 * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + " MB",
    };
}

/// <summary>
/// The WebSocket tab: the selected session's WebSocket messages, live while
/// the connection is open, with the selected message shown in full below.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private WebSocketLog? _shownWebSocket;
    private int _webSocketShownUpTo;
    private string _webSocketHeader = "Select a WebSocket session (status 101) to see its messages.";
    private WebSocketMessageRow? _selectedWebSocketMessage;
    private string _webSocketMessageText = string.Empty;
    private string _webSocketMessageNote = string.Empty;
    private bool _hideWebSocketControlMessages;

    public ObservableCollection<WebSocketMessageRow> WebSocketMessages { get; } = [];

    /// <summary>The WebSocket tab's heading: the connection, whether it's open, and message counts.</summary>
    public string WebSocketHeader
    {
        get => _webSocketHeader;
        private set => SetField(ref _webSocketHeader, value);
    }

    public WebSocketMessageRow? SelectedWebSocketMessage
    {
        get => _selectedWebSocketMessage;
        set
        {
            if (SetField(ref _selectedWebSocketMessage, value))
            {
                ShowWebSocketMessage(value?.Message);
            }
        }
    }

    /// <summary>The selected message in full: text (JSON indented), or hex for binary.</summary>
    public string WebSocketMessageText
    {
        get => _webSocketMessageText;
        private set => SetField(ref _webSocketMessageText, value);
    }

    public string WebSocketMessageNote
    {
        get => _webSocketMessageNote;
        private set => SetField(ref _webSocketMessageNote, value);
    }

    /// <summary>Hides pings and pongs, which keep-alives can send by the hundred.</summary>
    public bool HideWebSocketControlMessages
    {
        get => _hideWebSocketControlMessages;
        set
        {
            if (SetField(ref _hideWebSocketControlMessages, value) && _shownWebSocket is { } log)
            {
                SelectedWebSocketMessage = null;
                WebSocketMessages.Clear();
                _webSocketShownUpTo = 0;
                AddWebSocketRows(log.Messages);
            }
        }
    }

    /// <summary>Called when the selected session changes.</summary>
    private void RefreshWebSocket() => ShowWebSocketLog(SelectedSessionRow?.Session.WebSocket);

    private void ShowWebSocketLog(WebSocketLog? log)
    {
        if (ReferenceEquals(log, _shownWebSocket) && log is not null)
        {
            return;
        }

        if (_shownWebSocket is not null)
        {
            _shownWebSocket.MessageAdded -= OnWebSocketMessageAdded;
            _shownWebSocket.Closed -= OnWebSocketClosed;
        }

        _shownWebSocket = log;
        _webSocketShownUpTo = 0;
        SelectedWebSocketMessage = null;
        WebSocketMessages.Clear();
        if (log is null)
        {
            WebSocketHeader = SelectedSessionRow is null
                ? "Select a WebSocket session (status 101) to see its messages."
                : "This session isn't a WebSocket connection. WebSocket sessions have status 101.";
            return;
        }

        // Subscribe first, then add what's there: a message that arrives in
        // between is added once (see _webSocketShownUpTo).
        log.MessageAdded += OnWebSocketMessageAdded;
        log.Closed += OnWebSocketClosed;
        AddWebSocketRows(log.Messages);
        UpdateWebSocketHeader();
    }

    private void OnWebSocketMessageAdded(WebSocketMessage message)
    {
        var log = _shownWebSocket;
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(log, _shownWebSocket) && log is not null)
            {
                AddWebSocketRows([message]);
                UpdateWebSocketHeader();
            }
        });
    }

    private void OnWebSocketClosed()
    {
        var log = _shownWebSocket;
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(log, _shownWebSocket))
            {
                UpdateWebSocketHeader();
            }
        });
    }

    private void AddWebSocketRows(IEnumerable<WebSocketMessage> messages)
    {
        if (_shownWebSocket is not { } log)
        {
            return;
        }

        foreach (var message in messages)
        {
            if (message.Number <= _webSocketShownUpTo)
            {
                continue;
            }

            _webSocketShownUpTo = message.Number;
            if (_hideWebSocketControlMessages && message.Kind is WebSocketMessageKind.Ping or WebSocketMessageKind.Pong)
            {
                continue;
            }

            WebSocketMessages.Add(new WebSocketMessageRow(message, log.OpenedAt));
        }
    }

    private void UpdateWebSocketHeader()
    {
        if (_shownWebSocket is not { } log || SelectedSessionRow?.Session is not { } session)
        {
            return;
        }

        var messages = log.Messages;
        var sent = messages.Count(m => m.Direction == WebSocketDirection.Sent);
        var url = SessionUrl.Of(session);
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "wss://" + url[8..];
        }
        else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            url = "ws://" + url[7..];
        }

        var state = log.ClosedAt is { } closedAt
            ? $"closed after {FormatDuration(closedAt - log.OpenedAt)}"
            : $"open for {FormatDuration(DateTimeOffset.Now - log.OpenedAt)}";
        var header = new StringBuilder($"{url}: {state}. ")
            .Append(CultureInfo.InvariantCulture, $"{messages.Count} {(messages.Count == 1 ? "message" : "messages")} ({sent} sent, {messages.Count - sent} received)");
        if (WebSocketCompression.FromHeader(log.Extensions) is not null)
        {
            header.Append(", compressed (permessage-deflate)");
        }

        header.Append('.');
        if (log.Problem is { } problem)
        {
            header.Append(' ').Append(problem);
        }

        WebSocketHeader = header.ToString();
    }

    private void ShowWebSocketMessage(WebSocketMessage? message)
    {
        if (message is null)
        {
            WebSocketMessageText = string.Empty;
            WebSocketMessageNote = string.Empty;
            return;
        }

        var notes = new List<string>();
        if (message.Note is { } note)
        {
            notes.Add(note);
        }

        if (message.Truncated)
        {
            notes.Add($"Only the first {WebSocketMessageRow.FormatSize(message.Payload.Length)} is kept.");
        }

        switch (message.Kind)
        {
            case WebSocketMessageKind.Text:
                WebSocketMessageText = SessionComparison.Format(message.Text, string.Empty);
                break;
            case WebSocketMessageKind.Close:
                var code = message.CloseCode;
                WebSocketMessageText = code is null
                    ? "Close, with no status code."
                    : $"Close {code}{(WebSocketMessage.CloseCodeName(code.Value) is { } name ? $" ({name})" : string.Empty)}" +
                      (string.IsNullOrEmpty(message.CloseReason) ? string.Empty : $": {message.CloseReason}");
                break;
            default:
                var shown = TextTransforms.BytesToText(message.Payload, Encoding.UTF8);
                WebSocketMessageText = message.Payload.Length == 0 ? "(empty)" : shown.Output;
                if (message.Kind == WebSocketMessageKind.Binary && shown.Note is null)
                {
                    notes.Add("Binary, but it's UTF-8 text, so it's shown as text.");
                }

                break;
        }

        WebSocketMessageNote = string.Join(' ', notes);
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalSeconds switch
    {
        < 1 => $"{Math.Max(0, duration.TotalMilliseconds):0} ms",
        < 120 => duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s",
        < 7200 => duration.TotalMinutes.ToString("0", CultureInfo.InvariantCulture) + " min",
        _ => duration.TotalHours.ToString("0.0", CultureInfo.InvariantCulture) + " h",
    };
}
