using Pubwiz.Core.Targets.Azure;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Profiles;

public sealed class PubxmlSerializerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public void Read_HandlesTheOldMsBuildNamespace()
    {
        var path = _temp.CreateFile("Old.pubxml", """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="4.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <WebPublishMethod>FileSystem</WebPublishMethod>
                <PublishUrl>bin\Release\publish\</PublishUrl>
              </PropertyGroup>
            </Project>
            """);

        var profile = PubxmlSerializer.Read(path);

        Assert.Equal("Old", profile.Name);
        Assert.Equal(PublishMethod.FileSystem, profile.Method);
        Assert.Equal(@"bin\Release\publish\", profile[ProfileProperties.PublishUrl]);
    }

    [Fact]
    public void EmptyElements_ReadAsUnset()
    {
        // A Visual Studio Zip Deploy profile that signs in with the Azure account: the user name is left empty.
        var path = _temp.CreateFile("Zip.pubxml", """
            <Project>
              <PropertyGroup>
                <WebPublishMethod>ZipDeploy</WebPublishMethod>
                <PublishUrl>https://qa-app.scm.azurewebsites.net/</PublishUrl>
                <UserName />
                <_SavePWD>false</_SavePWD>
                <SkipExtraFilesOnServer>   </SkipExtraFilesOnServer>
              </PropertyGroup>
            </Project>
            """);

        var profile = PubxmlSerializer.Read(path);

        Assert.Null(profile[ProfileProperties.UserName]);
        Assert.Null(profile[ProfileProperties.SkipExtraFilesOnServer]);
        Assert.True(AzureProfile.UsesAzCli(profile));
        Assert.Contains(profile.Properties, p => p.Key == "UserName"); // kept, so saving doesn't change the file
    }

    [Fact]
    public void Method_FallsBackToPublishProtocolForNonWebProfiles()
    {
        // What Visual Studio writes for a console app folder profile.
        var path = _temp.CreateFile("Console.pubxml", """
            <Project>
              <PropertyGroup>
                <PublishDir>bin\Release\net10.0\publish\</PublishDir>
                <PublishProtocol>FileSystem</PublishProtocol>
              </PropertyGroup>
            </Project>
            """);

        Assert.Equal(PublishMethod.FileSystem, PubxmlSerializer.Read(path).Method);
    }

    [Fact]
    public void WriteThenRead_KeepsPropertiesAndOrder()
    {
        var profile = new PublishProfile("Folder");
        profile[ProfileProperties.WebPublishMethod] = "FileSystem";
        profile[ProfileProperties.PublishUrl] = "out";
        profile[ProfileProperties.DeleteExistingFiles] = "true";
        var path = Path.Combine(_temp.Path, "Folder.pubxml");

        PubxmlSerializer.Write(profile, path);
        var read = PubxmlSerializer.Read(path);

        Assert.Equal(profile.Properties, read.Properties);
    }

    [Fact]
    public void Indexer_IsCaseInsensitiveAndNullRemoves()
    {
        var profile = new PublishProfile("Test");
        profile["PublishUrl"] = "a";

        profile["publishurl"] = "b";
        Assert.Equal("b", profile["PUBLISHURL"]);
        Assert.Single(profile.Properties);

        profile["PublishUrl"] = null;
        Assert.Empty(profile.Properties);
    }

    [Fact]
    public void ItemsAreReadAndWrittenBack()
    {
        var path = _temp.CreateFile("Iis.pubxml", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <WebPublishMethod>MSDeploy</WebPublishMethod>
              </PropertyGroup>
              <ItemGroup>
                <MsDeploySkipRules Include="KeepUploads">
                  <ObjectName>dirPath</ObjectName>
                  <AbsolutePath>wwwroot\\uploads$</AbsolutePath>
                </MsDeploySkipRules>
              </ItemGroup>
            </Project>
            """);

        var profile = PubxmlSerializer.Read(path);
        var copyPath = Path.Combine(_temp.Path, "Copy.pubxml");
        PubxmlSerializer.Write(profile, copyPath);
        var copy = PubxmlSerializer.Read(copyPath);

        var item = Assert.Single(copy.Items);
        Assert.Equal(ProfileProperties.MsDeploySkipRules, item.Type);
        Assert.Equal("KeepUploads", item.Include);
        Assert.Equal("dirPath", item["objectname"]);
        Assert.Equal(@"wwwroot\\uploads$", item["AbsolutePath"]);
    }

    [Fact]
    public void SetProperty_ChangesOnlyThatProperty()
    {
        var path = _temp.CreateFile("Iis.pubxml", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <!-- made by Visual Studio -->
              <PropertyGroup>
                <WebPublishMethod>MSDeploy</WebPublishMethod>
                <AllowUntrustedCertificate>False</AllowUntrustedCertificate>
              </PropertyGroup>
              <ItemGroup>
                <Content Remove="secrets.json" />
              </ItemGroup>
            </Project>
            """);

        PubxmlSerializer.SetProperty(path, ProfileProperties.AllowUntrustedCertificate, "true");

        var text = File.ReadAllText(path);
        Assert.Contains("<AllowUntrustedCertificate>true</AllowUntrustedCertificate>", text);
        Assert.Contains("<!-- made by Visual Studio -->", text);
        Assert.Contains("<Content Remove=\"secrets.json\" />", text);
        Assert.Equal("MSDeploy", PubxmlSerializer.Read(path)[ProfileProperties.WebPublishMethod]);
    }

    [Fact]
    public void SetProperty_AddsAMissingPropertyInTheFilesNamespace()
    {
        var path = _temp.CreateFile("Iis.pubxml", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <WebPublishMethod>MSDeploy</WebPublishMethod>
              </PropertyGroup>
            </Project>
            """);

        PubxmlSerializer.SetProperty(path, ProfileProperties.AllowUntrustedCertificate, "true");

        Assert.DoesNotContain("xmlns=\"\"", File.ReadAllText(path));
        Assert.Equal("true", PubxmlSerializer.Read(path)[ProfileProperties.AllowUntrustedCertificate]);
    }

    public void Dispose() => _temp.Dispose();
}
