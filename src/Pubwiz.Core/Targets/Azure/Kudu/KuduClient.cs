using Pubwiz.Core.Publishing.Steps;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Pubwiz.Core.Targets.Azure.Kudu;

/// <summary>
/// Thrown when Kudu rejects a deployment.
/// </summary>
public sealed class KuduException(string message) : Exception(message);

/// <summary>
/// Deploys zip packages to Azure App Service through its Kudu (SCM) site.
/// </summary>
/// <seealso href="https://learn.microsoft.com/en-us/azure/app-service/deploy-zip"/>
public sealed class KuduClient(HttpClient httpClient)
{
    /// <summary>How often a running deployment is checked.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>How long to wait for a deployment to finish after the upload.</summary>
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Uploads a zip to <c>/api/publish?type=zip</c> (the "one deploy" API) and waits for the deployment to finish.
    /// </summary>
    /// <param name="scmUrl">The SCM site, e.g. <c>https://contoso.scm.azurewebsites.net</c>.</param>
    /// <param name="zipPath">The package, created with <see cref="ZipPackager"/>.</param>
    /// <param name="credentials">Basic or Bearer credentials.</param>
    /// <param name="onUploadProgress">Upload progress (0 to 1).</param>
    /// <param name="onStatus">Called whenever the deployment status changes.</param>
    /// <param name="cancellationToken">Cancels the upload or the wait.</param>
    /// <exception cref="KuduException">The upload was rejected or the deployment failed.</exception>
    public async Task<KuduDeployment> PublishZipAsync(
        Uri scmUrl,
        string zipPath,
        KuduCredentials credentials,
        Action<double> onUploadProgress,
        Action<KuduDeployment> onStatus,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(scmUrl, "api/publish?type=zip"));
        request.Headers.Authorization = credentials.ToHeader();
        request.Content = new ProgressStreamContent(File.OpenRead(zipPath), onUploadProgress);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureAcceptedAsync(response, cancellationToken);

        // 202 Accepted: the deployment runs in the background and its status lives at the Location URL.
        if (response.Headers.Location is not { } statusUrl)
        {
            return new KuduDeployment { Status = KuduDeploymentStatus.Success };
        }

        return await WaitForDeploymentAsync(new Uri(scmUrl, statusUrl), credentials, onStatus, cancellationToken);
    }

    /// <summary>
    /// Uploads a WebJob: <c>PUT /api/continuouswebjobs/{name}</c> or <c>/api/triggeredwebjobs/{name}</c> with the zip as the body.
    /// Kudu replaces an existing WebJob with the same name.
    /// </summary>
    /// <exception cref="KuduException">The upload was rejected.</exception>
    public async Task DeployWebJobAsync(
        Uri scmUrl,
        string name,
        bool triggered,
        string zipPath,
        KuduCredentials credentials,
        Action<double> onUploadProgress,
        CancellationToken cancellationToken)
    {
        var path = $"api/{(triggered ? "triggeredwebjobs" : "continuouswebjobs")}/{Uri.EscapeDataString(name)}";
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(scmUrl, path));
        request.Headers.Authorization = credentials.ToHeader();
        request.Content = new ProgressStreamContent(File.OpenRead(zipPath), onUploadProgress);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        request.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = $"{name}.zip" };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureAcceptedAsync(response, cancellationToken);
    }

    private async Task<KuduDeployment> WaitForDeploymentAsync(Uri statusUrl, KuduCredentials credentials, Action<KuduDeployment> onStatus, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(MaxWait);

        var lastStatus = KuduDeploymentStatus.Unknown;
        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, statusUrl);
            request.Headers.Authorization = credentials.ToHeader();

            KuduDeployment? deployment;
            try
            {
                using var response = await httpClient.SendAsync(request, timeout.Token);
                deployment = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<KuduDeployment>(timeout.Token) : null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new KuduException($"The deployment didn't finish within {MaxWait.TotalMinutes:0} minutes. Check its status in the Azure portal.");
            }

            if (deployment is not null && deployment.Status != lastStatus)
            {
                lastStatus = deployment.Status;
                onStatus(deployment);
            }

            if (deployment is { IsFinished: true })
            {
                return deployment.IsSuccessful
                    ? deployment
                    : throw new KuduException($"Deployment {deployment.Status}: {deployment.StatusText ?? deployment.Message ?? "no details"}");
            }

            await Task.Delay(PollInterval, timeout.Token);
        }
    }

    private static async Task EnsureAcceptedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var hint = response.StatusCode == HttpStatusCode.Unauthorized
            ? " Check the credentials; basic (publishing) authentication may be disabled for this app, in which case sign in with az instead."
            : string.Empty;

        throw new KuduException($"Kudu returned {(int)response.StatusCode} {response.ReasonPhrase}.{hint} {body}".Trim());
    }
}
