using System.IO;
using System.Net.Http;
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Service;

/// <summary>
/// Downloads the signed blocklist. The network is untrusted: the only thing it can do here is
/// deliver bytes, which <see cref="BlocklistUpdater"/> then verifies, validates and version-checks
/// before anything is cached or enforced.
/// </summary>
public static class BlocklistFetcher
{
    private const string BaseUrl = "https://raw.githubusercontent.com/KaktusBR/CloveParentalGuard/main/";
    private const string AllowedHost = "raw.githubusercontent.com";

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 3,
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public static async Task<BlocklistUpdateResult> UpdateAsync(
        BlocklistCache cache, long currentVersion, CancellationToken ct = default)
    {
        byte[] json;
        string sig;
        try
        {
            json = await DownloadAsync(BaseUrl + "blocklist.json", BlocklistValidator.MaxBytes, ct);
            sig = System.Text.Encoding.ASCII.GetString(await DownloadAsync(BaseUrl + "blocklist.sig", 4096, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
        {
            return new(BlocklistOutcome.Rejected, "Could not download the list: " + ex.Message);
        }

        var result = BlocklistUpdater.Evaluate(json, sig, BuiltInBlocklist.TrustedKeys(), currentVersion);
        if (result.Outcome == BlocklistOutcome.Updated)
            cache.Save(json, sig);
        return result;
    }

    private static async Task<byte[]> DownloadAsync(string url, int maxBytes, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // A redirect (e.g. after the repo was renamed) is fine, but only within GitHub's raw host over HTTPS.
        var final = response.RequestMessage?.RequestUri;
        if (final is null || final.Scheme != Uri.UriSchemeHttps
            || !final.Host.Equals(AllowedHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("unexpected redirect target");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > maxBytes) throw new InvalidDataException("download too large");
        }
        return ms.ToArray();
    }
}
