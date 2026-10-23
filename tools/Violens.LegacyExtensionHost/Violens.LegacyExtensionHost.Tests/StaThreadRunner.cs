using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Violens.LegacyExtensionHost.Tests;

/// <summary>
/// Runs an action on an STA thread and rethrows its exception on the
/// caller's. These tests use real WinForms objects, which need STA, and
/// xUnit doesn't provide one.
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
