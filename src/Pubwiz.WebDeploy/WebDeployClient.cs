using System.Net;
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
    /// Deploys <paramref name="localFolder"/> to the endpoint's site/application. Existing files that
    /// are not in the source are left in place (like <c>-enableRule:DoNotDeleteRule</c> is not set, the
    /// default rule set is used; deletes are not performed by this client).
    /// </summary>
    public async Task<WebDeployResult> SyncFolderAsync(
        WebDeployEndpoint endpoint,
        WebDeployCredentials credentials,
        string localFolder,
        WebDeploySyncOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WebDeploySyncOptions();
        var sourcePath = Path.GetFullPath(localFolder);
        var tree = SourceDirectory.FromDirectory(sourcePath);
        var body = new MsDeployBodyWriter(sourcePath, Environment.MachineName);

        using var httpClient = CreateHttpClient(credentials, options, injectedHandler);
        using var timeout = new CancellationTokenSource(options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = linked.Token;

        await ProbeAsync(httpClient, endpoint, token).ConfigureAwait(false);

        var sid = Guid.NewGuid();
        var providerOptions = EncodeHeader(NrbfProviderOptionsWriter.Write("iisApp", endpoint.IisPath));
        var progress = new List<WebDeployProgress>();

        // Pass 1: send the tree as metadata; the server tells us which files' content it needs.
        var pass1 = await RunSyncPassAsync(
            httpClient, endpoint, sid, passId: 1, providerOptions,
            body.WriteMetadataPass(tree), options, progress, token).ConfigureAwait(false);

        // Pass 2: resend the tree with the content of exactly those files.
        await RunSyncPassAsync(
            httpClient, endpoint, sid, passId: 2, providerOptions,
            body.WriteContentPass(tree, pass1.IncompleteObjectIds), options, progress, token).ConfigureAwait(false);

        return new WebDeployResult(progress);
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

        using var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);
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

    private static async Task<TraceResults> RunSyncPassAsync(
        HttpClient httpClient, WebDeployEndpoint endpoint, Guid sid, int passId, string providerOptions,
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
            request.Headers.TryAddWithoutValidation("MSDeploy.ProviderOptions", providerOptions);
            request.Headers.TryAddWithoutValidation("MSDeploy.BaseOptions", MsDeployProtocol.BaseOptionsHeader);
            request.Headers.TryAddWithoutValidation("MSDeploy.SyncOptions", MsDeployProtocol.SyncOptionsHeader);
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
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
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
