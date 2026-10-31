namespace Pubwiz.Core.Execution.Steps;

/// <summary>
/// Checks that the publish folder exists and isn't empty, and logs its size.
/// </summary>
public sealed class VerifyOutputStep() : PublishStep("Verify output")
{
    public override Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var folder = context.OutputDirectory;
        if (folder is null || !Directory.Exists(folder))
        {
            throw new PublishStepException($"The publish folder '{folder}' was not created.");
        }

        var files = new DirectoryInfo(folder).GetFiles("*", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            throw new PublishStepException($"The publish folder '{folder}' is empty.");
        }

        var sizeInMegabytes = files.Sum(f => f.Length) / 1024d / 1024d;
        context.Log.Info($"{files.Length} files, {sizeInMegabytes:0.0} MB in {folder}");
        return Task.CompletedTask;
    }
}
