namespace Pubwiz.Core.Publishing.Steps;

/// <summary>
/// Records where the app was deployed (site URL or server path) for the results. Runs last, so it's only set on success.
/// The destination can be given up front, or read when the step runs (e.g. a URL an earlier step found out).
/// </summary>
public sealed class SetDestinationStep(Func<string> getDestination) : PublishStep("Done")
{
    public SetDestinationStep(string destination)
        : this(() => destination)
    {
    }

    public override Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var destination = getDestination();
        context.Destination = destination;
        context.Log.Info($"Deployed to {destination}");
        return Task.CompletedTask;
    }
}
