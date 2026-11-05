using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;
using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Core.Targets.Containers;

namespace Pubwiz.Core.Tests.Targets.Containers;

public class ContainerProfileTests
{
    private static PublishProfile Profile(params (string Name, string Value)[] properties)
    {
        var profile = new PublishProfile("Container");
        profile[ProfileProperties.WebPublishMethod] = "Container";
        foreach (var (name, value) in properties)
        {
            profile[name] = value;
        }

        return profile;
    }

    [Fact]
    public void Read_UsesSdkDefaults()
    {
        var container = ContainerProfile.Read(Profile(), "MyApi");

        Assert.Equal(ContainerBuilder.Sdk, container.Builder);
        Assert.Equal("myapi", container.Repository);
        Assert.Equal(["myapi:latest"], container.ImageNames);
        Assert.Equal(PublishTargetType.LocalContainer, container.Destination);
    }

    [Theory]
    [InlineData(ProfileProperties.ContainerArchiveOutputPath, "bin/image.tar.gz", PublishTargetType.ContainerTarball)]
    [InlineData(ProfileProperties.ContainerRegistry, "ghcr.io", PublishTargetType.ContainerRegistry)]
    public void Destination_FollowsTheProfile(string property, string value, PublishTargetType expected)
    {
        Assert.Equal(expected, ContainerProfile.Read(Profile((property, value)), "app").Destination);
    }

    [Theory]
    [InlineData("AppService", PublishTargetType.AzureAppServiceContainer)]
    [InlineData("ContainerApp", PublishTargetType.AzureContainerApps)]
    public void Destination_RecognizesAzureHosts(string host, PublishTargetType expected)
    {
        var profile = Profile((ProfileProperties.ContainerRegistry, "contoso.azurecr.io"), (ProfileProperties.PubwizAzureContainerHost, host));

        Assert.Equal(expected, ContainerProfile.Read(profile, "app").Destination);
    }

    [Theory]
    [InlineData("Srta.Api", "srta-api")]
    [InlineData("Contoso.Shop_Api", "contoso-shop-api")]
    [InlineData("My  App", "my-app")]
    [InlineData("...", "app")]
    public void ToRepositoryName_UsesDashesForEverythingButLettersAndDigits(string projectName, string expected)
    {
        Assert.Equal(expected, ContainerProfile.ToRepositoryName(projectName));
        Assert.True(ContainerProfile.IsValidRepository(expected));
        Assert.Equal(expected, ContainerProfile.Read(Profile(), projectName).Repository);
    }

    [Theory]
    [InlineData("api", true)]
    [InlineData("contoso/shop-api", true)]
    [InlineData("shop.api__v2", true)]
    [InlineData("Shop", false)]
    [InlineData("-api", false)]
    [InlineData("api-", false)]
    [InlineData("shop//api", false)]
    [InlineData("shop api", false)]
    public void IsValidRepository_FollowsTheRegistryRules(string repository, bool expected)
    {
        Assert.Equal(expected, ContainerProfile.IsValidRepository(repository));
    }

    [Theory]
    [InlineData("latest", true)]
    [InlineData("1.2.0-preview_3", true)]
    [InlineData(".hidden", false)]
    [InlineData("-x", false)]
    [InlineData("a/b", false)]
    public void IsValidTag_FollowsTheRegistryRules(string tag, bool expected)
    {
        Assert.Equal(expected, ContainerProfile.IsValidTag(tag));
    }

    [Fact]
    public void Read_UnderstandsVisualStudioProperties()
    {
        // As Visual Studio writes an Azure Container Registry profile.
        var container = ContainerProfile.Read(Profile(
            (ProfileProperties.ContainerPublishMethod, "NetSdk"),
            (ProfileProperties.RegistryUrl, "contoso.azurecr.io"),
            (ProfileProperties.PublishImageTag, "v1")), "Shop.Api");

        Assert.Equal("contoso.azurecr.io", container.Registry);
        Assert.Equal(["contoso.azurecr.io/shop-api:v1"], container.ImageNames);
        Assert.Equal(PublishTargetType.ContainerRegistry, container.Destination);
    }

    [Fact]
    public void Read_DropsTheSchemeOfOldRegistryUrls()
    {
        var container = ContainerProfile.Read(Profile((ProfileProperties.RegistryUrl, "https://registry.hub.docker.com/contoso/")), "api");

        Assert.Equal(["registry.hub.docker.com/contoso/api:latest"], container.ImageNames);
    }

    [Fact]
    public void ImageNames_IncludeTheRegistryAndEveryTag()
    {
        var container = ContainerProfile.Read(Profile(
            (ProfileProperties.ContainerRegistry, "contoso.azurecr.io"),
            (ProfileProperties.ContainerRepository, "shop/api"),
            (ProfileProperties.ContainerImageTags, "1.2.0; latest")), "app");

        Assert.Equal(["contoso.azurecr.io/shop/api:1.2.0", "contoso.azurecr.io/shop/api:latest"], container.ImageNames);
        Assert.True(container.IsAzureRegistry);
        Assert.Equal("contoso", container.AzureRegistryName);
    }

    [Fact]
    public void Target_PicksStepsForTheBuilderAndDestination()
    {
        var target = new ContainerTarget(new DotnetCli(new ProcessRunner(), "dotnet"), new ContainerCli(new ProcessRunner()), new AzCli(new ProcessRunner()), PublishTargetType.ContainerRegistry);
        var project = new ProjectInfo { Name = "Api", FilePath = "/src/Api/Api.csproj", Kind = ProjectKind.Web };

        var sdk = target.CreateSteps(new PublishRequest(project, Profile((ProfileProperties.ContainerRegistry, "ghcr.io"))));
        var dockerfile = target.CreateSteps(new PublishRequest(project, Profile(
            (ProfileProperties.ContainerRegistry, "ghcr.io"), (ProfileProperties.PubwizContainerBuilder, "Dockerfile"), (ProfileProperties.LocalRegistry, "Podman"))));

        Assert.Equal(["Sign in to registry", "Build and publish", "Done"], sdk.Select(s => s.Name));
        var publish = Assert.IsType<DotnetPublishStep>(sdk[1]).Options;
        Assert.Equal("ghcr.io", publish.Properties[ProfileProperties.ContainerRegistry]);
        Assert.Equal("api", publish.Properties[ProfileProperties.ContainerRepository]);
        Assert.Equal(["Sign in to registry", "Build image (Dockerfile)", "Push image", "Done"], dockerfile.Select(s => s.Name));
    }

    [Fact]
    public void RequiresPassword_OnlyForRegistriesWithAUserName()
    {
        var registry = new ContainerTarget(new DotnetCli(new ProcessRunner(), "dotnet"), new ContainerCli(new ProcessRunner()), new AzCli(new ProcessRunner()), PublishTargetType.ContainerRegistry);

        Assert.False(registry.RequiresPassword(Profile((ProfileProperties.ContainerRegistry, "contoso.azurecr.io"))));
        Assert.True(registry.RequiresPassword(Profile((ProfileProperties.ContainerRegistry, "ghcr.io"), (ProfileProperties.UserName, "saif"))));
    }

    [Theory]
    [InlineData("bin/container/", "/src/Api/bin/container/shop-api.tar.gz")]
    [InlineData("out/api.tar.gz", "/src/Api/out/api.tar.gz")]
    public void ResolveArchivePath_NamesFilesInFoldersAfterTheRepository(string archivePath, string expected)
    {
        var project = new ProjectInfo { Name = "Api", FilePath = "/src/Api/Api.csproj", Kind = ProjectKind.Web };
        var profile = Profile((ProfileProperties.ContainerArchiveOutputPath, archivePath), (ProfileProperties.ContainerRepository, "shop/api"));

        var path = ContainerTarget.ResolveArchivePath(new PublishRequest(project, profile), ContainerProfile.Read(profile, "api"));

        // GetFullPath adds the current drive on Windows ("/src" becomes "D:\src").
        Assert.Equal(Path.GetFullPath(expected), path);
    }
}
