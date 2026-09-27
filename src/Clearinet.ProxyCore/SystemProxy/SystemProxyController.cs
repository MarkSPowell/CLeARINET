namespace Clearinet.ProxyCore.SystemProxy;

/// <summary>
/// Dispatches to whichever platform's own system-proxy implementation
/// applies -- <see cref="WinInetSystemProxy"/> on Windows,
/// <see cref="MacOSSystemProxy"/> on macOS -- so call sites like
/// <c>MainWindowViewModel</c> don't carry their own
/// <c>OperatingSystem.IsWindows()</c>/<c>IsMacOS()</c> branching for this.
/// See the Interception Certificate Design doc's "Platform status" section
/// for the reasoning behind each platform's own approach.
///
/// Deliberately a plain dispatch with no "unsupported platform" throw, on
/// either end: both <see cref="WinInetSystemProxy"/> and
/// <see cref="MacOSSystemProxy"/> already no-op on any platform other than
/// their own, and every method here is called unconditionally (not gated
/// behind an <c>IsWindows()</c>/<c>IsMacOS()</c> check at the call site --
/// see <c>MainWindowViewModel</c>'s constructor, which calls
/// <see cref="RecoverFromCrash"/> before any platform check happens at
/// all). Matching that no-op posture here, rather than throwing
/// <see cref="PlatformNotSupportedException"/> the way
/// <c>Certificates.CertificateAuthority</c> does, keeps this facade safe to
/// call from code that isn't itself platform-gated. The one place platform
/// support is actually enforced is <c>MainWindowViewModel.IsSupported</c>,
/// which keeps <c>Enable</c> from ever being reached on a platform neither
/// of these handles in the first place.
/// </summary>
public static class SystemProxyController
{
    /// <summary>
    /// Points the system proxy at <c>127.0.0.1:port</c>, on whichever
    /// platform this is running on.
    /// </summary>
    public static void Enable(int port)
    {
        if (OperatingSystem.IsWindows())
        {
            WinInetSystemProxy.Enable(port);
        }
        else if (OperatingSystem.IsMacOS())
        {
            MacOSSystemProxy.Enable(port);
        }
    }

    /// <summary>
    /// Restores whatever the system proxy was set to before the last
    /// <see cref="Enable"/> call, on whichever platform this is running on.
    /// </summary>
    /// <summary>
    /// The proxy this machine was set to use before CLeARINET registered
    /// itself, for CLeARINET to forward its own connections through. Call
    /// before <see cref="Enable"/>. Never throws: a failure to read the
    /// settings is reported in the description and means "connect directly".
    /// </summary>
    public static UpstreamProxyDetection DetectUpstream(int ownPort)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return WinInetSystemProxy.DetectUpstream(ownPort);
            }

            if (OperatingSystem.IsMacOS())
            {
                return MacOSSystemProxy.DetectUpstream(ownPort);
            }
        }
        catch (Exception ex)
        {
            return new UpstreamProxyDetection(null, $"couldn't read the system proxy settings ({ex.Message}), so connecting directly");
        }

        return UpstreamProxyDetection.None;
    }

    public static void Disable()
    {
        if (OperatingSystem.IsWindows())
        {
            WinInetSystemProxy.Disable();
        }
        else if (OperatingSystem.IsMacOS())
        {
            MacOSSystemProxy.Disable();
        }
    }

    /// <summary>
    /// Call once, early, on every launch, before anything else touches the
    /// system proxy -- see <see cref="WinInetSystemProxy.RecoverFromCrash"/>'s
    /// and <see cref="MacOSSystemProxy.RecoverFromCrash"/>'s own remarks,
    /// which this just dispatches to.
    /// </summary>
    public static void RecoverFromCrash()
    {
        if (OperatingSystem.IsWindows())
        {
            WinInetSystemProxy.RecoverFromCrash();
        }
        else if (OperatingSystem.IsMacOS())
        {
            MacOSSystemProxy.RecoverFromCrash();
        }
    }
}
