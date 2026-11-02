namespace Pubwiz.WebDeploy;

/// <summary>The outcome of a successful sync: the progress messages the server reported.</summary>
/// <param name="Actions">Every progress message from both passes, in order.</param>
public sealed record WebDeployResult(IReadOnlyList<WebDeployProgress> Actions)
{
    /// <summary>The number of changes the server applied (one per reported action; 0 means nothing changed).</summary>
    public int ChangeCount => Actions.Count;
}
