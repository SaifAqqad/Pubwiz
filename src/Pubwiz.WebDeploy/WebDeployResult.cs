namespace Pubwiz.WebDeploy;

/// <summary>The outcome of a successful sync: the changes the server reported.</summary>
/// <param name="Actions">Every progress message from both passes, in order.</param>
public sealed record WebDeployResult(IReadOnlyList<WebDeployProgress> Actions)
{
    /// <summary>
    /// <c>true</c> when this was a preview (<see cref="WebDeploySyncOptions.WhatIf"/>): the actions are what a sync would
    /// change, and nothing was changed.
    /// </summary>
    public bool WhatIf { get; init; }

    /// <summary>The number of changes the server applied, or would apply in a preview (0 means nothing changed).</summary>
    public int ChangeCount => Actions.Count;
}
