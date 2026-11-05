using Pubwiz.Core.Targets.Azure;

namespace Pubwiz.Core.Tests.Targets.Azure;

public class AzureResourceIdsTests
{
    [Fact]
    public void WebApp_CanBeParsedBackIntoTheApp()
    {
        var resourceId = AzureResourceIds.WebApp("sub-1", "shop-rg", "shop-api");

        Assert.Equal("/subscriptions/sub-1/resourceGroups/shop-rg/providers/Microsoft.Web/sites/shop-api", resourceId);
        Assert.Equal(new AzureAppReference("sub-1", "shop-rg", "shop-api"), AzureAppReference.TryParseResourceId(resourceId));
    }

    [Fact]
    public void GetResourceGroupAndName_ReadAnyResourceId()
    {
        var resourceId = AzureResourceIds.ContainerApp("sub-1", "shop-rg", "shop-api");

        Assert.Equal("shop-rg", AzureResourceIds.GetResourceGroup(resourceId));
        Assert.Equal("shop-api", AzureResourceIds.GetName(resourceId));
        Assert.Null(AzureResourceIds.GetName(null));
    }
}
