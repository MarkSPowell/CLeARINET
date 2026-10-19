namespace Violens.ProxyCore;

/// <summary>
/// Who is running the proxy engine: the names ProxyCore puts on the things
/// it creates on the user's machine and shows to them. An app that embeds
/// ProxyCore creates its own with <c>new ProxyHostIdentity("MyApp")</c> and
/// passes it to <see cref="ProxyEngine.Configure"/> once at startup, so it
/// never shares (or fights over) VioLens's root certificate, folders or
/// system-proxy backups.
///
/// <list type="bullet">
/// <item><see cref="ProductName"/>: shown in error pages and messages, and
/// written into HAR files as the creator.</item>
/// <item><see cref="RootCertificateName"/>: the common name of the root
/// certificate authority that HTTPS interception uses. Each app needs its
/// own: two apps sharing one root would each consider it theirs to create,
/// trust and remove.</item>
/// <item><see cref="ResolveDataFolder"/>: where ProxyCore keeps its own files (the
/// system-proxy backup that lets a crashed session's proxy settings be put
/// back, and the default preferences file).</item>
/// <item><see cref="ProxyHostName"/> and <see cref="RootCertificateFileName"/>:
/// the proxy's own page, which a phone or another computer browses to (as
/// <c>http://ProxyHostName/</c>) to download the root certificate.</item>
/// </list>
///
/// The constructor derives every name from the product name; set any of
/// them explicitly with <c>with</c> or an object initializer.
/// </summary>
public sealed record ProxyHostIdentity
{
    /// <summary>
    /// VioLens's own identity, and what ProxyCore uses until
    /// <see cref="ProxyEngine.Configure"/> is called. Its names predate this
    /// type, so they're spelled out rather than derived.
    /// </summary>
    public static ProxyHostIdentity Default { get; } = new("VioLens")
    {
        ProductVersion = ViolensVersion.Current.ToString(),
        RootCertificateName = "DO_NOT_TRUST_ViolensRoot",
        ProxyHostName = "violens",
        RootCertificateFileName = "ViolensRoot.cer",
    };

    /// <summary>
    /// An identity for <paramref name="productName"/>. For "My App 2" that's a
    /// root certificate named <c>DO_NOT_TRUST_MyApp2Root</c>, the proxy page
    /// at <c>http://myapp2/</c> serving <c>MyApp2Root.cer</c>, and files under
    /// the per-user app-data folder's <c>My App 2</c> folder.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The name is empty, has no letters or digits, or can't be a folder name.
    /// </exception>
    public ProxyHostIdentity(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            throw new ArgumentException("A product name is required.", nameof(productName));
        }

        if (productName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || productName is "." or "..")
        {
            throw new ArgumentException($"\"{productName}\" can't be used as a folder name.", nameof(productName));
        }

        var token = new string(productName.Where(char.IsAsciiLetterOrDigit).ToArray());
        if (token.Length == 0)
        {
            throw new ArgumentException($"\"{productName}\" needs at least one letter or digit (A-Z, 0-9).", nameof(productName));
        }

        ProductName = productName.Trim();
        RootCertificateName = $"DO_NOT_TRUST_{token}Root";
        ProxyHostName = token.ToLowerInvariant();
        RootCertificateFileName = $"{token}Root.cer";
    }

    /// <summary>The app's name, as people see it.</summary>
    public string ProductName { get; }

    /// <summary>The app's version, written into HAR files beside <see cref="ProductName"/>. Empty when not set.</summary>
    public string ProductVersion { get; init; } = string.Empty;

    /// <summary>
    /// The root certificate's common name, before the machine name and date
    /// ProxyCore adds. Found again on later runs by this name, so changing it
    /// means a new root (and a new trust prompt).
    /// </summary>
    public string RootCertificateName { get; init; }

    /// <summary>The host name that means "the proxy itself" when requested through it.</summary>
    public string ProxyHostName { get; init; }

    /// <summary>The file name the proxy's page serves the root certificate as.</summary>
    public string RootCertificateFileName { get; init; }

    /// <summary>
    /// Where ProxyCore keeps its own files, or null (the default) for
    /// <see cref="ProductName"/> under the per-user local app-data folder
    /// (<c>%LOCALAPPDATA%</c> on Windows, <c>~/Library/Application Support</c>
    /// on macOS). See <see cref="ResolveDataFolder"/>.
    /// </summary>
    public string? DataFolder { get; init; }

    /// <summary>
    /// <see cref="DataFolder"/>, or the default when it isn't set. Not
    /// created by calling this; whatever writes there creates it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The default is needed and the operating system reports no app-data folder.
    /// </exception>
    public string ResolveDataFolder() =>
        string.IsNullOrWhiteSpace(DataFolder) ? DefaultDataFolder(ProductName) : DataFolder;

    private static string DefaultDataFolder(string productName)
    {
        // On Unix, GetFolderPath returns "" rather than throwing when the
        // folder doesn't exist, and Path.Combine would then quietly produce
        // a path relative to the working directory. Failing loudly is better.
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root))
        {
            throw new InvalidOperationException(
                $"The operating system reported no local application-data folder, so {productName} has nowhere to keep its files.");
        }

        return Path.Combine(root, productName);
    }
}
