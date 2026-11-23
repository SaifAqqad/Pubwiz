using Pubwiz.Core.Targets.Azure;
using Pubwiz.Tool.Wizard;

namespace Pubwiz.Tool.Tests.Wizard;

public class DeploymentSlotPromptTests
{
    private static readonly AzureWebApp App = new("contoso", "rg", "app", "contoso.azurewebsites.net", "Running");

    private static readonly AzureDeploymentSlot Staging = new("staging", "contoso-staging.azurewebsites.net", "Running");

    [Fact]
    public void ProfileName_NamesTheSlot()
    {
        Assert.Equal("contoso", DeploymentSlotPrompt.ProfileName(App, AzureDeploymentSlot.Production(App)));
        Assert.Equal("contoso (staging)", DeploymentSlotPrompt.ProfileName(App, Staging));
    }

    [Fact]
    public void ResourceId_PointsAtTheSlot()
    {
        Assert.EndsWith("/sites/contoso", DeploymentSlotPrompt.ResourceId("sub-1", App, AzureDeploymentSlot.Production(App)));
        Assert.EndsWith("/sites/contoso/slots/staging", DeploymentSlotPrompt.ResourceId("sub-1", App, Staging));
    }
}
