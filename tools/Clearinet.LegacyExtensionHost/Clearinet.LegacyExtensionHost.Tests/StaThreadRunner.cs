using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Clearinet.LegacyExtensionHost.Tests;

/// <summary>
/// Runs an action on a dedicated STA thread and rethrows anything it
/// throws back on the calling (xunit worker) thread -- xunit doesn't run
/// tests on an STA thread by default, and this repo doesn't already
/// depend on a package (e.g. Xunit.StaFact) that would do this
/// automatically. Needed here because these tests construct and touch
/// real <see cref="System.Windows.Forms"/> objects
/// (<see cref="Clearinet.CompatShim.frmViewer"/>, its <c>MenuItem</c>/<c>MainMenu</c>/
/// <c>ContextMenu</c>/<c>StatusBarPanel</c> fields) -- the same real
/// WinForms types real extensions bind against, which is the whole point
/// of this shim -- and WinForms is only reliably correct on STA.
/// </summary>
internal static class StaThreadRunner
{
    public static void Run(Action action)
    {
        ExceptionDispatchInfo capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        capturedException?.Throw();
    }
}
