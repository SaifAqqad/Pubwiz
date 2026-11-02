using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Pubwiz.WebDeploy.Tests;

public class WebDeployCertificateTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetAsync_ReportsWhyASelfSignedCertificateIsNotTrusted()
    {
        // Like the certificate WMSVC creates for itself: self-signed, named after the service rather than the host.
        using var serverCertificate = CreateSelfSigned("CN=WMSvc-SHA2-TEST");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = ServeOneHandshakeAsync(listener, serverCertificate);

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var certificate = await WebDeployCertificate.GetAsync(new Uri($"https://127.0.0.1:{port}/msdeploy.axd"), Token);
        await server;

        Assert.NotNull(certificate);
        Assert.False(certificate.IsTrusted);
        Assert.Equal(serverCertificate.Thumbprint, certificate.Thumbprint);
        Assert.Contains("it's self-signed", certificate.Problems);
        Assert.Contains("it's issued to WMSvc-SHA2-TEST, not 127.0.0.1", certificate.Problems);
    }

    [Fact]
    public async Task GetAsync_HasNothingToCheckOverHttp()
    {
        Assert.Null(await WebDeployCertificate.GetAsync(new Uri("http://127.0.0.1:8080/msdeploy.axd"), Token));
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenTheServerCantBeReached()
    {
        // A port that was just free: nothing listens there.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Assert.Null(await WebDeployCertificate.GetAsync(new Uri($"https://127.0.0.1:{port}/msdeploy.axd"), Token));
    }

    private static async Task ServeOneHandshakeAsync(TcpListener listener, X509Certificate2 certificate)
    {
        using var client = await listener.AcceptTcpClientAsync(Token);
        await using var ssl = new SslStream(client.GetStream());
        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, Token);
    }

    private static X509Certificate2 CreateSelfSigned(string subject)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30));

        // Round-trip through PKCS#12 so the private key is usable by SslStream on every OS (Windows rejects ephemeral keys).
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), password: null);
    }
}
