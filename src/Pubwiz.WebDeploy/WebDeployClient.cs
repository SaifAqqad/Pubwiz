using System.IO.Compression;
using System.Net;
using System.Security.Authentication;
using Pubwiz.WebDeploy.Protocol;

namespace Pubwiz.WebDeploy;

/// <summary>
/// A cross-platform client for IIS Web Deploy (the <c>msdeploy.axd</c> handler of the Web Management
/// Service). Deploys a local folder to an IIS site or application with the <c>iisApp</c> provider,
/// the same operation as <c>msdeploy -verb:sync -source:iisApp=&lt;folder&gt; -dest:iisApp=&lt;site&gt;</c>.
/// </summary>
public sealed class WebDeployClient
{
    // Headers Web Deploy expects on every request. These negotiate the protocol version.
    private const string VersionMin = "7.1.600.0";
    private const string VersionMax = "9.0.2001.0";
    private const string Version = "9.0.0.0";

    private readonly HttpMessageHandler? injectedHandler;

    /// <summary>Creates a client that manages its own HTTP transport.</summary>
    public WebDeployClient()
    {
    }

    /// <summary>Creates a client over a caller-supplied transport (for testing or request capture).</summary>
    internal WebDeployClient(HttpMessageHandler handler) => injectedHandler = handler;

    /// <summary>
    /// Deploys <paramref name="localFolder"/> to the endpoint's site/application: adds and updates files and folders,
    /// and (with <see cref="WebDeploySyncOptions.DeleteExtraFiles"/>) deletes the ones that aren't in the source.
    /// With <see cref="WebDeploySyncOptions.WhatIf"/> it only reports what would change.
    /// </summary>
    /// <exception cref="ArgumentException">An option can't be sent (see <see cref="WebDeploySkipRule.AbsolutePath"/>).</exception>
    /// <exception cref="WebDeployException">The server rejected the sign-in or the sync.</exception>
    public async Task<WebDeployResult> SyncFolderAsync(
        WebDeployEndpoint endpoint,
        WebDeployCredentials credentials,
        string localFolder,
        WebDeploySyncOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WebDeploySyncOptions();
        NrbfOptionsWriter.Validate(options);

        var sourcePath = Path.GetFullPath(localFolder);
        var tree = SourceDirectory.FromDirectory(sourcePath);
        var body = new MsDeployBodyWriter(sourcePath, Environment.MachineName);
        var headers = SyncHeaders.Create(endpoint, options);

        using var httpClient = CreateHttpClient(credentials, options, injectedHandler);
        using var timeout = new CancellationTokenSource(options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = linked.Token;

        await ProbeAsync(httpClient, endpoint, token).ConfigureAwait(false);

        var sid = Guid.NewGuid();
        var progress = new List<WebDeployProgress>();

        // Pass 1: send the tree as metadata; the server tells us which files' content it needs.
        // In what-if mode the server only reports what it would do, and there's no second pass (like msdeploy -whatif).
        // A checksum preview has to send every file's content up front, since the server can't ask for it later.
        var pass1Body = options is { WhatIf: true, UseChecksum: true } ? body.WriteAllContentPass(tree) : body.WriteMetadataPass(tree);
        var pass1 = await RunSyncPassAsync(
            httpClient, endpoint, sid, passId: 1, headers, pass1Body, options, progress, token).ConfigureAwait(false);

        if (!options.WhatIf)
        {
            // Pass 2: resend the tree with the content of exactly those files.
            var pass2Content = options.UseChecksum ? PlanChecksumPass(tree, pass1) : new PassContent(pass1.IncompleteObjectIds, new Dictionary<int, uint>());
            await RunSyncPassAsync(
                httpClient, endpoint, sid, passId: 2, headers,
                body.WriteContentPass(tree, pass2Content), options, progress, token).ConfigureAwait(false);
        }

        return new WebDeployResult(progress) { WhatIf = options.WhatIf };
    }

    /// <summary>
    /// The second pass of a checksum sync, as msdeploy does it: for each file the server asked about, compare the checksum
    /// of its copy (from the first pass) with the local one, and send the content only when they differ. Files the server
    /// doesn't have, and files too large for <see cref="WebDeployChecksum"/>, get their content without a checksum.
    /// </summary>
    private static PassContent PlanChecksumPass(SourceDirectory tree, TraceResults pass1)
    {
        var contentIds = new HashSet<int>();
        var checksums = new Dictionary<int, uint>();
        foreach (var file in tree.Files().Where(f => pass1.IncompleteObjectIds.Contains(f.ObjectId)))
        {
            if (pass1.ServerChecksums.TryGetValue(file.ObjectId, out var serverChecksum) && WebDeployChecksum.Compute(file) is { } localChecksum)
            {
                checksums[file.ObjectId] = localChecksum;
                if (localChecksum == serverChecksum)
                {
                    continue; // unchanged: the checksum alone tells the server so
                }
            }

            contentIds.Add(file.ObjectId);
        }

        return new PassContent(contentIds, checksums);
    }

    /// <summary>The three option headers of a sync, encoded once and sent with both passes.</summary>
    private sealed record SyncHeaders(string ProviderOptions, string BaseOptions, string SyncOptions)
    {
        public static SyncHeaders Create(WebDeployEndpoint endpoint, WebDeploySyncOptions options) => new(
            EncodeHeader(NrbfProviderOptionsWriter.Write("iisApp", endpoint.IisPath)),
            EncodeHeader(NrbfOptionsWriter.WriteBaseOptions(options)),
            EncodeHeader(NrbfOptionsWriter.WriteSyncOptions(options)));
    }

    private static HttpClient CreateHttpClient(WebDeployCredentials credentials, WebDeploySyncOptions options, HttpMessageHandler? injected)
    {
        HttpMessageHandler handler;
        if (injected is not null)
        {
            handler = injected;
        }
        else
        {
            var httpHandler = new HttpClientHandler();
            if (options.AllowUntrusted)
            {
                httpHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            handler = httpHandler;
        }

        var client = new HttpClient(handler, disposeHandler: injected is null) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.TryAddWithoutValidation("MSDeploy.VersionMin", VersionMin);
        client.DefaultRequestHeaders.TryAddWithoutValidation("MSDeploy.VersionMax", VersionMax);
        client.DefaultRequestHeaders.TryAddWithoutValidation("MSDeploy.RequestUICulture", "en-US");
        client.DefaultRequestHeaders.TryAddWithoutValidation("MSDeploy.RequestCulture", "en-US");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Version", Version);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Basic {credentials.ToBasicToken()}");
        return client;
    }

    /// <summary>Authenticates with a HEAD request and confirms the handler is a Web Deploy endpoint.</summary>
    private static async Task ProbeAsync(HttpClient httpClient, WebDeployEndpoint endpoint, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, endpoint.ServiceUrl);
        request.Headers.TryAddWithoutValidation("MSDeploy.Serialization", "All");

        using var response = await SendFirstRequestAsync(httpClient, request, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new WebDeployException("Authentication failed: the server rejected the credentials (401 Unauthorized).");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await BuildErrorAsync(response, "The Web Deploy handshake failed").ConfigureAwait(false);
        }

        if (!response.Headers.Contains("MSDeploy.Response"))
        {
            throw new WebDeployException($"The endpoint at {endpoint.ServiceUrl} is not a Web Deploy handler.");
        }
    }

    /// <summary>
    /// Sends the first request of a sync, which is the one that does the TLS handshake, so an untrusted server
    /// certificate fails here. That failure is turned into a clear error.
    /// </summary>
    private static async Task<HttpResponseMessage> SendFirstRequestAsync(HttpClient httpClient, HttpRequestMessage request, CancellationToken token)
    {
        try
        {
            return await httpClient.SendAsync(request, token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            throw new WebDeployException($"The server's certificate isn't trusted: {ex.InnerException.Message}", inner: ex) { IsUntrustedCertificate = true };
        }
    }

    private static async Task<TraceResults> RunSyncPassAsync(
        HttpClient httpClient, WebDeployEndpoint endpoint, Guid sid, int passId, SyncHeaders headers,
        byte[] syncBody, WebDeploySyncOptions options, List<WebDeployProgress> progress, CancellationToken token)
    {
        var requestId = Guid.NewGuid();

        // The progress long-poll shares the sync's RequestId and must be in flight while the sync runs,
        // so start it first, then send the sync, then read the trace it collected.
        var traceTask = GetTraceStatusAsync(httpClient, endpoint, requestId, token);

        using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint.ServiceUrl))
        {
            request.Headers.TryAddWithoutValidation("MSDeploy.Serialization", "Binary");
            request.Headers.TryAddWithoutValidation("MSDeploy.Method", "Sync");
            request.Headers.TryAddWithoutValidation("MSDeploy.RequestId", requestId.ToString());
            request.Headers.TryAddWithoutValidation("MSDeploy.sid", sid.ToString());
            request.Headers.TryAddWithoutValidation("User-Agent", $"PubwizWebDeploy;sid={sid};op=Sync");
            request.Headers.TryAddWithoutValidation("MSDeploy.PassId", passId.ToString());
            request.Headers.TryAddWithoutValidation("MSDeploy.ProviderOptions", headers.ProviderOptions);
            request.Headers.TryAddWithoutValidation("MSDeploy.BaseOptions", headers.BaseOptions);
            request.Headers.TryAddWithoutValidation("MSDeploy.SyncOptions", headers.SyncOptions);
            request.Headers.ExpectContinue = true;
            request.Content = new ByteArrayContent(syncBody);
            request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/msdeploy");

            using var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw await BuildErrorAsync(response, $"The sync failed on pass {passId}").ConfigureAwait(false);
            }
        }

        var trace = await traceTask.ConfigureAwait(false);
        foreach (var action in trace.Actions)
        {
            progress.Add(action);
            options.OnProgress?.Invoke(action);
        }

        return trace;
    }

    private static async Task<TraceResults> GetTraceStatusAsync(
        HttpClient httpClient, WebDeployEndpoint endpoint, Guid requestId, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.ServiceUrl);
        request.Headers.TryAddWithoutValidation("MSDeploy.Serialization", "Binary");
        request.Headers.TryAddWithoutValidation("MSDeploy.Method", "GetTraceStatus");
        request.Headers.TryAddWithoutValidation("MSDeploy.RequestId", requestId.ToString());
        request.Content = new ByteArrayContent([]);

        using var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildErrorAsync(response, "The progress poll failed").ConfigureAwait(false);
        }

        var xml = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        return TraceResults.Parse(xml);
    }

    private static string EncodeHeader(byte[] nrbf)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(nrbf, 0, nrbf.Length);
        }

        return Convert.ToBase64String(output.ToArray());
    }

    private static async Task<WebDeployException> BuildErrorAsync(HttpResponseMessage response, string context)
    {
        if (response.Headers.TryGetValues("MSDeploy.Exception", out var values))
        {
            var error = WebDeployErrorDecoder.Decode(values.First());
            if (error is not null)
            {
                return new WebDeployException($"{context}: {error.Message}", error);
            }
        }

        var status = (int)response.StatusCode;
        return new WebDeployException($"{context}: the server returned HTTP {status} {response.ReasonPhrase}.");
    }
}
