using Pubwiz.Core.Profiles;
using System.Xml.Linq;

namespace Pubwiz.Core.Tests.Profiles;

public class PublishSettingsImporterTests
{
    // Shape of a file downloaded from the Azure portal (values made up).
    private static readonly XDocument AzureSettings = XDocument.Parse("""
        <publishData>
          <publishProfile profileName="contoso - Web Deploy" publishMethod="MSDeploy"
                          publishUrl="contoso.scm.azurewebsites.net:443" msdeploySite="contoso"
                          userName="$contoso" userPWD="secret-1" destinationAppUrl="https://contoso.azurewebsites.net"
                          SQLServerDBConnectionString="" webSystem="WebSites" />
          <publishProfile profileName="contoso - FTP" publishMethod="FTP"
                          publishUrl="ftps://waws-prod.ftp.azurewebsites.windows.net/site/wwwroot" ftpPassiveMode="True"
                          userName="contoso\$contoso" userPWD="secret-1" destinationAppUrl="https://contoso.azurewebsites.net" />
          <publishProfile profileName="contoso - Zip Deploy" publishMethod="ZipDeploy"
                          publishUrl="contoso.scm.azurewebsites.net:443"
                          userName="$contoso" userPWD="secret-1" destinationAppUrl="https://contoso.azurewebsites.net" />
          <publishProfile profileName="contoso - ReadOnly - SomethingElse" publishMethod="Unsupported" publishUrl="x" />
        </publishData>
        """);

    [Fact]
    public void Parse_ReadsSupportedEntries()
    {
        var entries = PublishSettingsImporter.Parse(AzureSettings);

        Assert.Equal([PublishMethod.MSDeploy, PublishMethod.FTP, PublishMethod.ZipDeploy], entries.Select(e => e.Method));
        Assert.All(entries, e => Assert.True(e.IsAzure));
        Assert.Equal("contoso", entries[0].SiteName);
        Assert.True(entries[1].FtpPassiveMode);
    }

    [Fact]
    public void ToProfile_MapsWebDeployLikeVisualStudio()
    {
        var entry = PublishSettingsImporter.Parse(AzureSettings)[0];

        var (profile, password) = PublishSettingsImporter.ToProfile(entry);

        Assert.Equal("contoso - Web Deploy", profile.Name);
        Assert.Equal(PublishMethod.MSDeploy, profile.Method);
        Assert.Equal("AzureWebSite", profile[ProfileProperties.PublishProvider]);
        Assert.Equal("contoso.scm.azurewebsites.net:443", profile[ProfileProperties.MSDeployServiceURL]);
        Assert.Equal("contoso", profile[ProfileProperties.DeployIisAppPath]);
        Assert.Equal("$contoso", profile[ProfileProperties.UserName]);
        Assert.Equal("secret-1", password);
        Assert.DoesNotContain(profile.Properties, p => p.Value == "secret-1");
    }

    [Fact]
    public void ToProfile_TurnsZipDeployHostIntoHttpsUrl()
    {
        var entry = PublishSettingsImporter.Parse(AzureSettings)[2];

        var (profile, _) = PublishSettingsImporter.ToProfile(entry);

        Assert.Equal("https://contoso.scm.azurewebsites.net/", profile[ProfileProperties.PublishUrl]);
    }

    [Fact]
    public void ToProfile_DoesNotMarkSelfHostedIisAsAzure()
    {
        var settings = XDocument.Parse("""
            <publishData>
              <publishProfile profileName="iis" publishMethod="MSDeploy" publishUrl="https://iis.local:8172/msdeploy.axd" msdeploySite="Default Web Site" userName="deploy" />
            </publishData>
            """);

        var (profile, _) = PublishSettingsImporter.ToProfile(PublishSettingsImporter.Parse(settings)[0]);

        Assert.Null(profile[ProfileProperties.PublishProvider]);
        Assert.Null(profile[ProfileProperties.EnableMSDeployBackup]);
    }
}
