using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Tool.Rendering;

namespace Pubwiz.Tool.Tests.Rendering;

public class ProfileTextTests
{
    private static PublishProfile ZipDeploy(string name, string? slot)
    {
        var profile = new PublishProfile(name);
        profile[ProfileProperties.WebPublishMethod] = "ZipDeploy";
        profile[ProfileProperties.PublishUrl] = "https://contoso.scm.azurewebsites.net/";
        profile[ProfileProperties.ResourceId] = AzureResourceIds.WebApp("sub-1", "rg", "contoso", slot);
        return profile;
    }

    [Fact]
    public void Details_IsTheMethodForTheProductionSlot()
    {
        Assert.Equal("ZipDeploy", ProfileText.Details(ZipDeploy("contoso - Zip Deploy", slot: null)));
    }

    [Fact]
    public void Details_NamesTheSlotWhenTheProfileNameDoesnt()
    {
        Assert.Equal("ZipDeploy · staging slot", ProfileText.Details(ZipDeploy("contoso - Zip Deploy", "staging")));
        Assert.Equal("ZipDeploy", ProfileText.Details(ZipDeploy("contoso (staging) - Zip Deploy", "staging")));
    }

    [Fact]
    public void ToMarkup_StylesTheNameAndDimsTheDetails()
    {
        Assert.Equal("[blue]contoso - Zip Deploy[/] [dim](ZipDeploy · qa slot)[/]", ProfileText.ToMarkup(ZipDeploy("contoso - Zip Deploy", "qa"), "blue"));
    }
}
