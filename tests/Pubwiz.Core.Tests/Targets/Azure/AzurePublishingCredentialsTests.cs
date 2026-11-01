using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.Azure;

namespace Pubwiz.Core.Tests.Targets.Azure;

public class AzurePublishingCredentialsTests
{
    private const string PublishSettings = """
        <publishData>
          <publishProfile profileName="contoso - Web Deploy" publishMethod="MSDeploy" publishUrl="contoso.scm.azurewebsites.net:443"
                          msdeploySite="contoso" userName="$contoso" userPWD="web-deploy-secret" destinationAppUrl="https://contoso.azurewebsites.net" />
          <publishProfile profileName="contoso - FTP" publishMethod="FTP" publishUrl="ftps://waws.ftp.azurewebsites.windows.net/site/wwwroot"
                          userName="contoso\$contoso" userPWD="ftp-secret" />
        </publishData>
        """;

    [Fact]
    public void PickPassword_UsesTheEntryWithTheProfilesUserName()
    {
        var entries = PublishSettingsImporter.ReadXml(PublishSettings);

        Assert.Equal("web-deploy-secret", AzurePublishingCredentials.PickPassword(entries, "$contoso"));
        Assert.Null(AzurePublishingCredentials.PickPassword(entries, "someone-else"));
    }

    [Fact]
    public void TryParseResourceId_ReadsAppsAndSlots()
    {
        var app = AzureAppReference.TryParseResourceId("/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Web/sites/contoso");
        var slot = AzureAppReference.TryParseResourceId("/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Web/sites/contoso/slots/staging");

        Assert.Equal(new AzureAppReference("sub-1", "rg", "contoso"), app);
        Assert.Equal(new AzureAppReference("sub-1", "rg", "contoso", "staging"), slot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/data")]
    public void TryParseResourceId_RejectsOtherResources(string? resourceId)
    {
        Assert.Null(AzureAppReference.TryParseResourceId(resourceId));
    }

    [Fact]
    public void Applies_OnlyToAppServiceProfilesWithAUserName()
    {
        var zipDeploy = new PublishProfile("contoso - Zip Deploy");
        zipDeploy[ProfileProperties.WebPublishMethod] = "ZipDeploy";
        zipDeploy[ProfileProperties.PublishUrl] = "contoso.scm.azurewebsites.net:443";
        Assert.False(AzurePublishingCredentials.Applies(zipDeploy)); // no user name: signs in with az

        zipDeploy[ProfileProperties.UserName] = "$contoso";
        Assert.True(AzurePublishingCredentials.Applies(zipDeploy));

        var folder = new PublishProfile("Folder");
        folder[ProfileProperties.WebPublishMethod] = "FileSystem";
        folder[ProfileProperties.UserName] = "someone";
        Assert.False(AzurePublishingCredentials.Applies(folder));
    }
}
