using Pubwiz.Core.Azure.Kudu;
using Pubwiz.Core.Tests.TestHelpers;
using System.Net;

namespace Pubwiz.Core.Tests.Azure;

public sealed class KuduClientTests : IDisposable
{
    private static readonly Uri ScmUrl = new("https://contoso.scm.azurewebsites.net/");

    private readonly TempDirectory _temp = new();
    private readonly FakeHttpHandler _handler = new();

    private KuduClient CreateClient() => new(new HttpClient(_handler)) { PollInterval = TimeSpan.Zero };

    private string CreateZip() => _temp.CreateFile("app.zip", new string('x', 1000));

    [Fact]
    public async Task PublishZipAsync_UploadsAndPollsUntilSuccess()
    {
        _handler
            .Respond(HttpStatusCode.Accepted, location: new Uri("/api/deployments/42", UriKind.Relative))
            .Respond(HttpStatusCode.OK, """{"id":"42","status":1}""")
            .Respond(HttpStatusCode.OK, """{"id":"42","status":4}""");

        var progress = new List<double>();
        var statuses = new List<KuduDeploymentStatus>();
        var result = await CreateClient().PublishZipAsync(ScmUrl, CreateZip(), new KuduCredentials.Basic("$contoso", "secret"),
            progress.Add, d => statuses.Add(d.Status), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccessful);
        Assert.Equal([KuduDeploymentStatus.Building, KuduDeploymentStatus.Success], statuses);
        Assert.Equal(1, progress[^1]);

        var upload = _handler.Requests[0];
        Assert.Equal(HttpMethod.Post, upload.Method);
        Assert.Equal("https://contoso.scm.azurewebsites.net/api/publish?type=zip", upload.Url.AbsoluteUri);
        Assert.StartsWith("Basic ", upload.Authorization);
        Assert.Equal(1000, upload.BodyLength);
        Assert.Equal("https://contoso.scm.azurewebsites.net/api/deployments/42", _handler.Requests[1].Url.AbsoluteUri);
    }

    [Fact]
    public async Task PublishZipAsync_ThrowsWhenTheDeploymentFails()
    {
        _handler
            .Respond(HttpStatusCode.Accepted, location: new Uri("https://contoso.scm.azurewebsites.net/api/deployments/7"))
            .Respond(HttpStatusCode.OK, """{"id":"7","status":3,"status_text":"Disk full"}""");

        var error = await Assert.ThrowsAsync<KuduException>(() => CreateClient().PublishZipAsync(ScmUrl, CreateZip(),
            new KuduCredentials.Bearer("token"), _ => { }, _ => { }, TestContext.Current.CancellationToken));

        Assert.Contains("Disk full", error.Message);
    }

    [Fact]
    public async Task PublishZipAsync_ExplainsUnauthorized()
    {
        _handler.Respond(HttpStatusCode.Unauthorized);

        var error = await Assert.ThrowsAsync<KuduException>(() => CreateClient().PublishZipAsync(ScmUrl, CreateZip(),
            new KuduCredentials.Basic("u", "p"), _ => { }, _ => { }, TestContext.Current.CancellationToken));

        Assert.Contains("basic (publishing) authentication may be disabled", error.Message);
    }

    public void Dispose() => _temp.Dispose();
}
