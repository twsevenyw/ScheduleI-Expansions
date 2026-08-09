using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Expansions.Updater.Engine;

namespace Expansions.Updater;

/// <summary>How a request ended. Only <see cref="Failed"/> is worth saying out loud.</summary>
internal enum HttpOutcome
{
    Ok,

    /// <summary>
    /// A 404. On the release-manifest URL this is the ordinary state of a repository with no published
    /// release yet — and of one with only prereleases, since <c>releases/latest</c> skips those. It is
    /// not an error and must never be reported as one.
    /// </summary>
    NotFound,

    /// <summary>No route to the host: no internet, a captive portal, DNS down, a blocking firewall.</summary>
    Offline,

    /// <summary>The server answered, but not with the file.</summary>
    Failed,
}

internal readonly struct HttpTextResult
{
    internal HttpTextResult(HttpOutcome outcome, string body, string failure)
    {
        Outcome = outcome;
        Body = body;
        Failure = failure;
    }

    internal HttpOutcome Outcome { get; }

    internal string Body { get; }

    internal string Failure { get; }

    internal bool Succeeded => Outcome == HttpOutcome.Ok;
}

internal readonly struct HttpFileResult
{
    internal HttpFileResult(HttpOutcome outcome, string failure)
    {
        Outcome = outcome;
        Failure = failure;
    }

    internal HttpOutcome Outcome { get; }

    internal string Failure { get; }

    internal bool Succeeded => Outcome == HttpOutcome.Ok;
}

/// <summary>
/// The updater's one network seam.
/// <para>
/// Everything here is expected to fail. "Offline" and "no release published yet" are both ordinary
/// states that must cost the launch nothing and must never put a line on the player's screen, so
/// failures are classified rather than thrown and the caller decides which ones are worth saying.
/// </para>
/// <para>
/// Every call is bounded twice: a per-request timeout on the client, and a cancellation token the
/// caller sets for the whole check. Nothing here can leave a thread parked for a session.
/// </para>
/// </summary>
internal static class UpdaterHttp
{
    /// <summary>Per request. Short, because none of this is on any critical path.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly object Gate = new();

    private static HttpClient? _client;

    /// <summary>
    /// GitHub's API rejects a request with no user agent outright, and the asset endpoints are happier
    /// with one too. Naming the suite also means the owner can recognise our traffic in a log.
    /// </summary>
    internal static string UserAgentVersion { get; set; } = "1.0.0";

    internal static HttpTextResult GetText(string url, long maxBytes, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // GitHub serves release assets as application/octet-stream, so this is a preference and the
            // response content type is deliberately never branched on.
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("*/*");

            using var response = Client().Send(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new HttpTextResult(HttpOutcome.NotFound, string.Empty, "nothing is published at that URL");

            if (!response.IsSuccessStatusCode)
            {
                return new HttpTextResult(
                    HttpOutcome.Failed,
                    string.Empty,
                    $"the server answered {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            using var stream = response.Content.ReadAsStream(cancellation);
            var body = ReadCapped(stream, maxBytes, cancellation, out var truncated);

            return truncated
                ? new HttpTextResult(HttpOutcome.Failed, string.Empty, $"the response is longer than {maxBytes} bytes")
                : new HttpTextResult(HttpOutcome.Ok, body, string.Empty);
        }
        catch (Exception ex)
        {
            return new HttpTextResult(Classify(ex), string.Empty, Describe(ex));
        }
    }

    /// <summary>
    /// Streams a file to <paramref name="destination"/>, refusing to write more than
    /// <paramref name="expectedBytes"/> plus a small margin. The manifest declares the exact size, so a
    /// response that keeps going is not the file we asked for.
    /// </summary>
    internal static HttpFileResult Download(
        string url,
        string destination,
        long expectedBytes,
        CancellationToken cancellation)
    {
        var limit = expectedBytes > 0 ? expectedBytes + 4096 : 256L * 1024 * 1024;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            request.Headers.Accept.ParseAdd("*/*");

            using var response = Client().Send(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new HttpFileResult(HttpOutcome.NotFound, "the release asset is no longer there");

            if (!response.IsSuccessStatusCode)
            {
                return new HttpFileResult(
                    HttpOutcome.Failed,
                    $"the server answered {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            using var stream = response.Content.ReadAsStream(cancellation);

            long total;
            using (var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                total = 0;
                int read;

                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    total += read;

                    if (total > limit)
                    {
                        file.Dispose();
                        Delete(destination);
                        return new HttpFileResult(
                            HttpOutcome.Failed,
                            $"the download went past the {expectedBytes} bytes the manifest declared");
                    }

                    file.Write(buffer, 0, read);
                }
            }

            if (expectedBytes > 0 && total != expectedBytes)
            {
                Delete(destination);
                return new HttpFileResult(
                    HttpOutcome.Failed, $"the download is {total} bytes and the manifest says {expectedBytes}");
            }

            return new HttpFileResult(HttpOutcome.Ok, string.Empty);
        }
        catch (Exception ex)
        {
            Delete(destination);
            return new HttpFileResult(Classify(ex), Describe(ex));
        }
    }

    internal static void Shutdown()
    {
        HttpClient? client;

        lock (Gate)
        {
            client = _client;
            _client = null;
        }

        try
        {
            client?.Dispose();
        }
        catch
        {
            // Disposing a client that never made a request cannot matter.
        }
    }

    private static HttpClient Client()
    {
        lock (Gate)
        {
            if (_client is not null)
                return _client;

            // The stable manifest URL is a 302 to objects.githubusercontent.com, so redirects have to be
            // followed. The default handler does; it is set explicitly because the contract depends on it.
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 8,
            };

            var client = new HttpClient(handler, disposeHandler: true) { Timeout = RequestTimeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"ScheduleI-Expansions-Updater/{UserAgentVersion} (+https://github.com/twsevenyw/ScheduleI-Expansions)");
            client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };

            _client = client;
            return client;
        }
    }

    private static string ReadCapped(Stream stream, long maxBytes, CancellationToken cancellation, out bool truncated)
    {
        var builder = new StringBuilder();
        var buffer = new byte[8192];
        long total = 0;
        int read;

        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            total += read;

            if (total > maxBytes)
            {
                truncated = true;
                return string.Empty;
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }

        truncated = false;
        return builder.ToString();
    }

    /// <summary>
    /// Anything that means "the request never reached a server" is <see cref="HttpOutcome.Offline"/>,
    /// which the caller keeps to the log.
    /// </summary>
    private static HttpOutcome Classify(Exception ex) => ex switch
    {
        TaskCanceledException => HttpOutcome.Offline,
        OperationCanceledException => HttpOutcome.Offline,
        HttpRequestException { InnerException: System.Net.Sockets.SocketException } => HttpOutcome.Offline,
        HttpRequestException { StatusCode: null } => HttpOutcome.Offline,
        _ => HttpOutcome.Failed,
    };

    private static string Describe(Exception ex)
    {
        var innermost = ex;
        while (innermost.InnerException is not null)
            innermost = innermost.InnerException;

        return $"{innermost.GetType().Name}: {innermost.Message}";
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A half-written download in our own scratch folder is harmless; the next attempt overwrites it.
        }
    }
}

/// <summary>
/// Where this machine looks for a release manifest, worked out from <c>update_manifest_url</c> and
/// <c>update_channel</c>.
/// <para>
/// The default is the publisher's stable asset URL, which always redirects to the newest non-draft,
/// non-prerelease release. That URL is why the stable path needs no GitHub API call and therefore has no
/// rate limit: one unauthenticated GET, redirects followed.
/// </para>
/// </summary>
internal sealed class ReleaseSource
{
    internal const string ManifestAssetName = "update-manifest.json";

    private const int MaxReleasesConsidered = 20;

    private static readonly Regex RepoShorthand =
        new(@"^[A-Za-z0-9][A-Za-z0-9._-]*/[A-Za-z0-9][A-Za-z0-9._-]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RepoInUrl =
        new(@"^https://(?:www\.)?github\.com/([A-Za-z0-9][A-Za-z0-9._-]*)/([A-Za-z0-9][A-Za-z0-9._-]*)(?:/|$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private ReleaseSource(string manifestUrl, string repo, string channel, bool includesPreReleases)
    {
        ManifestUrl = manifestUrl;
        Repo = repo;
        Channel = channel;
        IncludesPreReleases = includesPreReleases;
    }

    internal string ManifestUrl { get; }

    internal string Repo { get; }

    internal string Channel { get; }

    /// <summary>
    /// True when the configured channel is not <c>stable</c>. There is no channel field in the manifest —
    /// the publisher expresses "not for everyone" with GitHub's prerelease flag, which
    /// <c>releases/latest</c> skips — so a non-stable channel means listing the releases and taking the
    /// newest one including prereleases.
    /// </summary>
    internal bool IncludesPreReleases { get; }

    internal string Describe() =>
        IncludesPreReleases ? $"{Repo} on the '{Channel}' channel (prereleases included)" : ManifestUrl;

    internal static bool TryResolve(UpdaterSettings settings, out ReleaseSource? source, out string reason)
    {
        source = null;

        var configured = settings.ManifestUrl;
        if (configured.Length == 0)
        {
            reason = "'update_manifest_url' is empty in Expansions.cfg, so nothing is checked";
            return false;
        }

        string manifestUrl;
        string repo;

        if (RepoShorthand.IsMatch(configured))
        {
            repo = configured;
            manifestUrl = $"https://github.com/{repo}/releases/latest/download/{ManifestAssetName}";
        }
        else if (Uri.TryCreate(configured, UriKind.Absolute, out var uri) &&
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            manifestUrl = configured;

            var match = RepoInUrl.Match(configured);
            repo = match.Success ? $"{match.Groups[1].Value}/{match.Groups[2].Value}" : string.Empty;
        }
        else
        {
            reason = $"'{configured}' is neither an owner/repo shorthand nor an https URL";
            return false;
        }

        // A bare manifest URL carries no notion of a release list, so there is nothing to widen.
        var wantsPreReleases = settings.WantsPreReleases && repo.Length > 0;

        source = new ReleaseSource(manifestUrl, repo, settings.Channel, wantsPreReleases);
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Fetches the manifest text. On the stable channel that is one GET; on any other channel it lists
    /// the repository's releases first and takes the newest that carries a manifest asset.
    /// </summary>
    internal HttpTextResult Fetch(CancellationToken cancellation)
    {
        if (!IncludesPreReleases)
            return UpdaterHttp.GetText(ManifestUrl, Json.MaxLength, cancellation);

        var listing = UpdaterHttp.GetText(
            $"https://api.github.com/repos/{Repo}/releases?per_page={MaxReleasesConsidered}",
            Json.MaxLength * 4,
            cancellation);

        if (!listing.Succeeded)
            return listing;

        if (!Json.TryParse(listing.Body, out var releases, out var parseFailure) || !releases.IsArray)
        {
            return new HttpTextResult(
                HttpOutcome.Failed, string.Empty, $"the release list is not readable JSON ({parseFailure})");
        }

        // GitHub returns releases newest first, so the first match is the newest one that has a manifest.
        foreach (var release in releases.Items)
        {
            if (!release.IsObject || release.BooleanOr("draft", false))
                continue;

            foreach (var asset in release.ArrayOr("assets"))
            {
                if (!string.Equals(asset.StringOr("name"), ManifestAssetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var url = asset.StringOr("browser_download_url");
                if (url.Length == 0)
                    continue;

                return UpdaterHttp.GetText(url, Json.MaxLength, cancellation);
            }
        }

        return new HttpTextResult(
            HttpOutcome.NotFound, string.Empty, "no release in that repository carries an update manifest");
    }
}
