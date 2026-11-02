using Pubwiz.WebDeploy.Protocol;

namespace Pubwiz.WebDeploy.Tests;

/// <summary>
/// The fixtures are the decompressed <c>MSDeploy.SyncOptions</c> / <c>MSDeploy.BaseOptions</c> headers of real msdeploy.exe
/// syncs to the test VM, one per option (e.g. hdr-SyncOptions-appoffline.bin is <c>-enableRule:AppOffline</c>).
/// msdeploy deletes extra files by default, so the captures without <c>-enableRule:DoNotDeleteRule</c> match
/// <see cref="WebDeploySyncOptions.DeleteExtraFiles"/> = true.
/// </summary>
public class NrbfOptionsWriterTests
{
    // Two skip rules, in the order they were passed to msdeploy:
    // -skip:skipAction=Delete,objectName=dirPath,absolutePath=\\logs$ -skip:objectName=filePath,absolutePath=extra\.txt
    private static readonly WebDeploySkipRule KeepLogs = new(WebDeploySkipObject.Directory, @"\\logs$") { Action = WebDeploySkipAction.Delete };
    private static readonly WebDeploySkipRule SkipExtra = new(WebDeploySkipObject.File, @"extra\.txt");

    public static TheoryData<string, WebDeploySyncOptions> SyncOptionsCaptures => new()
    {
        { "baseline", new() { DeleteExtraFiles = true } },
        { "appoffline", new() { DeleteExtraFiles = true, TakeAppOffline = true } },
        { "donotdelete", new() },
        { "whatif", new() { DeleteExtraFiles = true, WhatIf = true } },
        { "checksum", new() { DeleteExtraFiles = true, UseChecksum = true } },
        { "skip2", WithSkipRules(new() { DeleteExtraFiles = true }, KeepLogs, SkipExtra) }
    };

    public static TheoryData<string, WebDeploySyncOptions> BaseOptionsCaptures => new()
    {
        { "baseline", new() },
        { "skip", WithSkipRules(new(), SkipExtra) },
        { "retry", new() { RetryAttempts = 7, RetryInterval = TimeSpan.FromSeconds(3) } },
        { "skip2", WithSkipRules(new(), KeepLogs, SkipExtra) }
    };

    [Theory]
    [MemberData(nameof(SyncOptionsCaptures))]
    public void WriteSyncOptions_MatchesMsDeployByteForByte(string capture, WebDeploySyncOptions options)
    {
        Assert.Equal(Fixtures.Read($"hdr-SyncOptions-{capture}.bin"), NrbfOptionsWriter.WriteSyncOptions(options));
    }

    [Theory]
    [MemberData(nameof(BaseOptionsCaptures))]
    public void WriteBaseOptions_MatchesMsDeployByteForByte(string capture, WebDeploySyncOptions options)
    {
        Assert.Equal(Fixtures.Read($"hdr-BaseOptions-{capture}.bin"), NrbfOptionsWriter.WriteBaseOptions(options));
    }

    [Theory]
    [InlineData("a\"b", "double quote")]
    [InlineData(@"\uploads$", "regular expression")] // a single backslash: \u needs four hex digits
    public void Validate_RejectsAPathTheServerCantUse(string path, string expectedInError)
    {
        var options = WithSkipRules(new(), new WebDeploySkipRule(WebDeploySkipObject.File, path));

        var error = Assert.Throws<ArgumentException>(() => NrbfOptionsWriter.Validate(options));
        Assert.Contains(expectedInError, error.Message);
    }

    private static WebDeploySyncOptions WithSkipRules(WebDeploySyncOptions options, params WebDeploySkipRule[] rules)
    {
        foreach (var rule in rules)
        {
            options.SkipRules.Add(rule);
        }

        return options;
    }
}
