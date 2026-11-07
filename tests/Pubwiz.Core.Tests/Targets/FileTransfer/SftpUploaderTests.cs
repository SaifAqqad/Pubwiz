using Pubwiz.Core.Targets.FileTransfer;

namespace Pubwiz.Core.Tests.Targets.FileTransfer;

public class SftpUploaderTests
{
    /// <summary>A remote file tree: folder path → its entries.</summary>
    private static Func<string, IEnumerable<(string Name, bool IsDirectory)>> Server(Dictionary<string, (string, bool)[]> folders, List<string>? listed = null) =>
        folder =>
        {
            listed?.Add(folder);
            return [(".", true), ("..", true), .. folders[folder]];
        };

    [Fact]
    public void FindExtraFiles_ReturnsRemoteFilesMissingLocally()
    {
        var server = Server(new()
        {
            ["/var/www/app"] = [("app.dll", false), ("old.dll", false), ("app_offline.htm", false), ("wwwroot", true)],
            ["/var/www/app/wwwroot"] = [("site.css", false), ("old.css", false)]
        });

        var extra = SftpUploader.FindExtraFiles("/var/www/app", ["app.dll", "wwwroot/site.css"], keep: "app_offline.htm", server);

        Assert.Equal(["/var/www/app/old.dll", "/var/www/app/wwwroot/old.css"], extra.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FindExtraFiles_AtTheServerRoot_ListsTheRootNotTheHomeFolder()
    {
        var listed = new List<string>();
        var server = Server(new()
        {
            ["/"] = [("app.dll", false), ("old.dll", false), ("bin", true)],
            ["/bin"] = [("lib.dll", false)]
        }, listed);

        var extra = SftpUploader.FindExtraFiles("/", ["app.dll", "bin/lib.dll"], keep: null, server);

        Assert.Equal(["/old.dll"], extra);
        Assert.Equal(["/", "/bin"], listed);
    }
}
