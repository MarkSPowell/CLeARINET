using System.Collections.Generic;
using System.Windows.Forms;

namespace Clearinet.CompatShim;

/// <summary>
/// Real, working WinForms main window standing in for Fiddler Classic's own
/// frmViewer -- see the design doc's "frmViewer/frmPrompt member-level
/// findings" section: extensions reach directly into this window's live
/// menu bar, context menu, status bar, and tab control as raw fields, so
/// those all have to be REAL instances of the (removed-from-.NET-5+, still
/// present here on net48) legacy WinForms control types for old compiled
/// extensions to bind and actually do something when they touch them.
///
/// <b>This window's own session list is still a synthetic, in-process
/// placeholder</b> (see <see cref="LoadDemoSessions"/>) -- <b>separately
/// from</b> the session bridge (see the design doc's "The legacy host's
/// session bridge -- built" section), which is built and does feed real
/// proxied traffic through a loaded extension's <c>IAutoTamper</c> hooks.
/// The two are unconnected: <c>SessionBridgeRunner</c> (sibling
/// <c>Clearinet.LegacyExtensionHost</c> project) talks directly to loaded
/// <c>IAutoTamper</c> instances and never touches this window's own
/// session list, so <see cref="GetSelectedSessions"/> still only ever
/// returns whatever's in <see cref="LoadDemoSessions"/> -- an extension
/// that reads real live traffic via its own <c>IAutoTamper</c> hooks but
/// then calls <c>GetSelectedSessions()</c> expecting to find that same
/// traffic in this window's grid won't. Good enough to prove discovery/
/// loading and exercise these five extensions' UI hooks for real, not to
/// replace the main app's own session list.
/// </summary>
public sealed class frmViewer : Form
{
    // Fields below are exactly what the five inspected extensions reach
    // into directly -- see each field's own remarks for which sample(s).

    /// <summary>metadata: field on frmViewer -- AustralianImages, SAZClipboard.</summary>
    public MenuItem mnuTools;

    /// <summary>metadata: field on frmViewer -- JSFormat.</summary>
    public MenuItem mnuRules;

    /// <summary>metadata: field on frmViewer -- ContentBlock.</summary>
    public MainMenu mnuMain;

    /// <summary>metadata: field on frmViewer -- ContentBlock, JSFormat.</summary>
    public ContextMenu mnuSessionContext;

    /// <summary>metadata: field on frmViewer -- ContentBlock, JSFormat.</summary>
    public StatusBarPanel sbpInfo;

    /// <summary>metadata: field on frmViewer -- Differ.</summary>
    public TabControl tabsViews;

    private readonly ListView _sessionList;
    private readonly StatusBar _statusBar;
    private readonly TextBox _logBox;
    private readonly TabPage _logTab;

    public frmViewer()
    {
        Text = "CLeARINET Legacy Extension Host";
        Width = 900;
        Height = 600;

        mnuTools = new MenuItem("&Tools");
        mnuRules = new MenuItem("&Rules");
        mnuMain = new MainMenu();
        mnuMain.MenuItems.Add(new MenuItem("&File"));
        mnuMain.MenuItems.Add(mnuRules);
        mnuMain.MenuItems.Add(mnuTools);
        mnuMain.MenuItems.Add(new MenuItem("&View"));
        Menu = mnuMain;

        mnuSessionContext = new ContextMenu();

        _sessionList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            ContextMenu = mnuSessionContext,
        };
        _sessionList.Columns.Add("#", 40);
        _sessionList.Columns.Add("Host", 200);
        _sessionList.Columns.Add("URL", 400);
        _sessionList.Columns.Add("Result", 60);

        tabsViews = new TabControl { Dock = DockStyle.Fill };
        var sessionsTab = new TabPage("Sessions");
        sessionsTab.Controls.Add(_sessionList);
        tabsViews.TabPages.Add(sessionsTab);

        // Added so scan/load results are visible without a debugger or
        // DebugView attached -- Debug.WriteLine alone (the original Phase 1
        // approach) is invisible when the .exe is just double-clicked or
        // run directly from a shell, which is exactly how this is normally
        // going to be run. See AppendLog below and Program.cs's local Log function.
        _logBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new System.Drawing.Font("Consolas", 9f),
            WordWrap = false,
        };
        _logTab = new TabPage("Log");
        _logTab.Controls.Add(_logBox);
        tabsViews.TabPages.Add(_logTab);

        Controls.Add(tabsViews);

        sbpInfo = new StatusBarPanel { AutoSize = StatusBarPanelAutoSize.Spring, Text = "Ready" };
        _statusBar = new StatusBar { ShowPanels = true };
        _statusBar.Panels.Add(sbpInfo);
        Controls.Add(_statusBar);

        LoadDemoSessions();
    }

    /// <summary>
    /// metadata: <c>Fiddler.frmViewer.Fiddler.Session[] GetSelectedSessions()</c>
    /// -- ContentBlock, JSFormat.
    /// </summary>
    public Session[] GetSelectedSessions()
    {
        var result = new List<Session>();
        foreach (ListViewItem item in _sessionList.SelectedItems)
        {
            if (item.Tag is Session session)
            {
                result.Add(session);
            }
        }
        return result.ToArray();
    }

    /// <summary>
    /// metadata: <c>Fiddler.frmViewer.void actDoCompareSessions(Fiddler.Session, Fiddler.Session)</c>
    /// -- Differ. Real Fiddler's own diff-view behavior isn't reproduced
    /// (unknown, and this project's clean-room policy doesn't read Differ's
    /// IL to find out) -- shows a minimal, honest placeholder instead of
    /// pretending to a real diff.
    /// </summary>
    public void actDoCompareSessions(Session first, Session second)
    {
        MessageBox.Show(
            "Compare requested:\r\n#" + first?.id + " " + first?.url + "\r\nvs.\r\n#" + second?.id + " " + second?.url +
            "\r\n\r\n(Placeholder -- no real diff view built yet in this Phase 1 pass.)",
            "Compare Sessions",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <summary>
    /// Appends a line to the on-screen "Log" tab so extension scan/load
    /// results are visible without DebugView. Safe to call before the
    /// window's handle exists yet (Program.cs logs during startup, before
    /// <see cref="Application.Run(Form)"/>) and safe to call from a
    /// non-UI thread, in case a future extension or IPC hook ever logs
    /// from one.
    /// </summary>
    public void AppendLog(string line)
    {
        if (_logBox is null)
        {
            return;
        }

        if (_logBox.IsHandleCreated && _logBox.InvokeRequired)
        {
            _logBox.BeginInvoke(new System.Action(() => AppendLogOnUiThread(line)));
            return;
        }

        AppendLogOnUiThread(line);
    }

    private void AppendLogOnUiThread(string line)
    {
        _logBox.AppendText("[" + System.DateTime.Now.ToString("HH:mm:ss") + "] " + line + System.Environment.NewLine);
    }

    /// <summary>
    /// Switches to the "Log" tab -- called by Program.cs when there were
    /// load errors, so a problem (e.g. an extension that failed to load) is
    /// the first thing visible on startup rather than something Mark has to
    /// go looking for.
    /// </summary>
    public void SelectLogTab()
    {
        if (tabsViews != null && _logTab != null)
        {
            tabsViews.SelectedTab = _logTab;
        }
    }

    /// <summary>
    /// Synthetic sessions so the host is a real, clickable app to smoke-test
    /// extension loading against, without a live proxy connection yet.
    /// </summary>
    private void LoadDemoSessions()
    {
        var demoSessions = new[]
        {
            new Session { id = 1, host = "example.com", url = "http://example.com/", fullUrl = "http://example.com/", PathAndQuery = "/", responseCode = 200 },
            new Session { id = 2, host = "example.com", url = "http://example.com/style.css", fullUrl = "http://example.com/style.css", PathAndQuery = "/style.css", responseCode = 200 },
        };

        foreach (var session in demoSessions)
        {
            var item = new ListViewItem(new[] { session.id.ToString(), session.host, session.url, session.responseCode.ToString() })
            {
                Tag = session,
            };
            _sessionList.Items.Add(item);
        }
    }
}
