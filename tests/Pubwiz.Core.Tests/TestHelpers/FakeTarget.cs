using Pubwiz.Core.Discovery;
using Pubwiz.Core.Execution;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets;

namespace Pubwiz.Core.Tests.TestHelpers;

/// <summary>A step that runs a delegate, for testing the runner.</summary>
public sealed class DelegateStep(string name, Func<PublishContext, CancellationToken, Task> action) : PublishStep(name)
{
    public bool Ran { get; private set; }

    public override Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        Ran = true;
        return action(context, cancellationToken);
    }

    public static DelegateStep Succeeds(string name) => new(name, (_, _) => Task.CompletedTask);

    public static DelegateStep Fails(string name, string message) => new(name, (_, _) => throw new PublishStepException(message));
}

/// <summary>A target that publishes FileSystem profiles with the given steps.</summary>
public sealed class FakeTarget(params PublishStep[] steps) : IPublishTarget
{
    public PublishTargetType Type => PublishTargetType.Folder;

    public bool CanPublish(PublishProfile profile) => profile.Method == PublishMethod.FileSystem;

    public bool RequiresPassword(PublishProfile profile) => false;

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request) => steps;
}

public static class TestRequests
{
    public static PublishRequest Create(string method = "FileSystem", string? password = null)
    {
        var project = new ProjectInfo { Name = "App", FilePath = Path.Combine(Path.GetTempPath(), "App", "App.csproj"), Kind = ProjectKind.Web };
        var profile = new PublishProfile("Test");
        profile[ProfileProperties.WebPublishMethod] = method;
        return new PublishRequest(project, profile) { Password = password };
    }
}
