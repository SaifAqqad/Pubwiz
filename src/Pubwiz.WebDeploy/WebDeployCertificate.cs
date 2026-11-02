using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Pubwiz.WebDeploy;

/// <summary>
/// The TLS certificate a Web Deploy server presents, and why this machine doesn't trust it (if it doesn't).
/// </summary>
/// <remarks>
/// WMSVC uses a self-signed certificate by default, which fails validation. Check it with <see cref="GetAsync"/> before
/// deploying, so the user can choose to trust it anyway (<see cref="WebDeploySyncOptions.AllowUntrusted"/>, or
/// <c>-allowUntrusted</c> for msdeploy.exe) instead of the deployment failing.
/// </remarks>
/// <param name="Subject">Who the certificate was issued to, e.g. <c>CN=WMSvc-SHA2-IIS01</c>.</param>
/// <param name="Issuer">Who issued it; the same as <paramref name="Subject"/> for a self-signed certificate.</param>
/// <param name="Thumbprint">The SHA-1 thumbprint (what IIS Manager and Windows show).</param>
/// <param name="ExpiresOn">When it stops being valid.</param>
/// <param name="Problems">Why it isn't trusted, e.g. "it's self-signed"; empty when it's trusted.</param>
public sealed record WebDeployCertificate(string Subject, string Issuer, string Thumbprint, DateTimeOffset ExpiresOn, IReadOnlyList<string> Problems)
{
    /// <summary>How long to wait for the server before giving up on the check.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public bool IsTrusted => Problems.Count == 0;

    /// <summary>
    /// Connects to the server and reads its certificate, without sending any Web Deploy request.
    /// </summary>
    /// <param name="serviceUrl">The handler URL, e.g. <see cref="WebDeployEndpoint.ServiceUrl"/>.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>
    /// The certificate, or <c>null</c> when there's none to check: the URL isn't https, or the server can't be reached
    /// (the deployment then reports why).
    /// </returns>
    public static async Task<WebDeployCertificate?> GetAsync(Uri serviceUrl, CancellationToken cancellationToken = default)
    {
        if (serviceUrl.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);

        WebDeployCertificate? result = null;
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(serviceUrl.Host, serviceUrl.Port, timeout.Token).ConfigureAwait(false);

            await using var ssl = new SslStream(tcp.GetStream());
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = serviceUrl.Host,

                // Records what the default validation found, then accepts the certificate so the handshake completes.
                RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
                {
                    if (certificate is not null)
                    {
                        using var certificate2 = new X509Certificate2(certificate);
                        result = Describe(certificate2, chain, errors, serviceUrl.Host);
                    }

                    return true;
                }
            };

            await ssl.AuthenticateAsClientAsync(options, timeout.Token).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // The handshake can still fail after the certificate was read (e.g. no shared TLS version).
            return result;
        }
    }

    internal static WebDeployCertificate Describe(X509Certificate2 certificate, X509Chain? chain, SslPolicyErrors errors, string host)
    {
        var problems = new List<string>();
        var statuses = chain?.ChainStatus.Select(s => s.Status).ToHashSet() ?? [];

        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
        {
            if (statuses.Contains(X509ChainStatusFlags.NotTimeValid))
            {
                problems.Add(certificate.NotAfter < DateTime.Now
                    ? $"it expired on {certificate.NotAfter:d}"
                    : $"it isn't valid until {certificate.NotBefore:d}");
            }

            if (statuses.Contains(X509ChainStatusFlags.UntrustedRoot) || statuses.Contains(X509ChainStatusFlags.PartialChain))
            {
                problems.Add(certificate.SubjectName.RawData.SequenceEqual(certificate.IssuerName.RawData)
                    ? "it's self-signed"
                    : "it isn't issued by an authority this machine trusts");
            }

            if (problems.Count == 0)
            {
                problems.Add("its certificate chain isn't valid");
            }
        }

        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            problems.Add($"it's issued to {certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false)}, not {host}");
        }

        return new WebDeployCertificate(certificate.Subject, certificate.Issuer, certificate.Thumbprint, certificate.NotAfter, problems);
    }
}
