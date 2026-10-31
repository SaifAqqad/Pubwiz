namespace Pubwiz.WebDeploy.Tests;

public class WebDeployEndpointTests
{
    [Theory]
    [InlineData("iis.contoso.com", "https://iis.contoso.com:8172/msdeploy.axd?site=Default%20Web%20Site")]
    [InlineData("iis.contoso.com:9000", "https://iis.contoso.com:9000/msdeploy.axd?site=Default%20Web%20Site")]
    [InlineData("https://iis.contoso.com", "https://iis.contoso.com:8172/msdeploy.axd?site=Default%20Web%20Site")]
    [InlineData("https://iis.contoso.com:8172/msdeploy.axd", "https://iis.contoso.com:8172/msdeploy.axd?site=Default%20Web%20Site")]
    [InlineData("https://iis.contoso.com:8172/msdeploy.axd?site=Other", "https://iis.contoso.com:8172/msdeploy.axd?site=Other")]
    [InlineData("contoso.scm.azurewebsites.net:443", "https://contoso.scm.azurewebsites.net/msdeploy.axd?site=Default%20Web%20Site")]
    public void Create_AddsTheSameDefaultsAsTheSdk(string serviceUrl, string expected)
    {
        var endpoint = WebDeployEndpoint.Create(serviceUrl, "Default Web Site");

        Assert.Equal(expected, endpoint.ServiceUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("Default Web Site", "Default Web Site", "")]
    [InlineData("Default Web Site/api", "Default Web Site", "api")]
    [InlineData(@"Default Web Site\api\v2", "Default Web Site", "api/v2")]
    [InlineData("/PubwizTest/", "PubwizTest", "")]
    public void Create_SplitsSiteAndApplication(string iisPath, string site, string application)
    {
        var endpoint = WebDeployEndpoint.Create("server", iisPath);

        Assert.Equal(site, endpoint.SiteName);
        Assert.Equal(application, endpoint.ApplicationPath);
    }

    [Fact]
    public void IsAzure_DetectsAppServiceScmSites()
    {
        Assert.True(WebDeployEndpoint.Create("contoso.scm.azurewebsites.net:443", "contoso").IsAzure);
        Assert.False(WebDeployEndpoint.Create("iis.local", "Default Web Site").IsAzure);
    }
}
