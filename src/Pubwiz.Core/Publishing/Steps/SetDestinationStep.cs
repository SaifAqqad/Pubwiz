namespace Pubwiz.Core.Publishing.Steps;

/// <summary>
/// Records where the app was deployed (site URL or server path) for the results. Runs last, so it's only set on success.
/// </summary>
public sealed class SetDestinationStep(string destination) : PublishStep("Done")
{
    public override Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        context.Destination = destination;
        context.Log.Info($"Deployed to {destination}");
        return Task.CompletedTask;
    }
}
