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

    public void Dispose() => _temp.Dispose();
}
