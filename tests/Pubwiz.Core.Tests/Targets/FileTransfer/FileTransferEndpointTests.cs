using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.FileTransfer;

namespace Pubwiz.Core.Tests.Targets.FileTransfer;

public class FileTransferEndpointTests
{
    private static PublishProfile Profile(string url, string? sitePath = null, string? security = null)
    {
        var profile = new PublishProfile("Test");
        profile[ProfileProperties.PublishUrl] = url;
        profile[ProfileProperties.FtpSitePath] = sitePath;
        profile[ProfileProperties.PubwizFtpSecurity] = security;
        return profile;
    }

    [Theory]
    [InlineData("ftp://ftp.contoso.com", false, 21, "/", FtpSecurity.Auto)]
    [InlineData("ftps://waws-prod.ftp.azurewebsites.windows.net/site/wwwroot", false, 21, "/site/wwwroot", FtpSecurity.Explicit)]
    [InlineData("ftp://ftp.contoso.com:2121/www/", false, 2121, "/www", FtpSecurity.Auto)]
    [InlineData("sftp://server/var/www/app", true, 22, "/var/www/app", FtpSecurity.Auto)]
    [InlineData("sftp://server:2222/c:/inetpub/app", true, 2222, "/c:/inetpub/app", FtpSecurity.Auto)]
    [InlineData("ftp.contoso.com", false, 21, "/", FtpSecurity.Auto)]
    public void FromProfile_ReadsTheUrl(string url, bool isSftp, int port, string directory, FtpSecurity security)
    {
        var endpoint = FileTransferEndpoint.FromProfile(Profile(url));

        Assert.Equal(isSftp, endpoint.IsSftp);
        Assert.Equal(port, endpoint.Port);
        Assert.Equal(directory, endpoint.RemoteDirectory);
        Assert.Equal(security, endpoint.FtpSecurity);
    }

    [Fact]
    public void FromProfile_AddsTheVisualStudioSitePath()
    {
        Assert.Equal("/site/wwwroot", FileTransferEndpoint.FromProfile(Profile("ftp://ftp.contoso.com", sitePath: "site/wwwroot")).RemoteDirectory);
    }

    [Fact]
    public void FromProfile_ImplicitSecurityDefaultsToPort990()
    {
        var endpoint = FileTransferEndpoint.FromProfile(Profile("ftps://ftp.contoso.com", security: "Implicit"));

        Assert.Equal(990, endpoint.Port);
        Assert.Equal(FtpSecurity.Implicit, endpoint.FtpSecurity);
    }
}
