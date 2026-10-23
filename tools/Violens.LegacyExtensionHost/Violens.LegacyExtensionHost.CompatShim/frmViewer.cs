using System.Collections.Generic;
using System.Windows.Forms;

namespace Violens.CompatShim;

/// <summary>
/// A WinForms window standing in for Fiddler's main window. Extensions
/// reach into its menus, status bar and tabs directly, so those are real
/// WinForms controls (still available on net48).
///
/// Its session list holds demo sessions only. Bridged traffic goes
/// straight to extensions' <c>IAutoTamper</c> hooks and never appears here,
/// so <see cref="GetSelectedSessions"/> returns demo sessions.
/// </summary>
public sealed class frmViewer : Form
{
    // The fields the five sample extensions use directly.

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
        Text = "VioLens Legacy Extension Host";
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

        // A Log tab, since Debug.WriteLine isn't visible when the exe is run
        // normally.
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

    /// <summary>Used by Differ. Shows a placeholder message rather than a real comparison.</summary>
    public void actDoCompareSessions(Session first, Session second)
    {
        MessageBox.Show(
            "Compare requested:\r\n#" + first?.id + " " + first?.url + "\r\nvs.\r\n#" + second?.id + " " + second?.url +
            "\r\n\r\n(Placeholder -- no real diff view built yet in this Phase 1 pass.)",
            "Compare Sessions",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <summary>Adds a line to the Log tab. Safe before the window exists and from any thread.</summary>
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

    /// <summary>Shows the Log tab; used at startup when there were load errors.</summary>
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
