namespace Pubwiz.Tool.Rendering;

/// <summary>
/// Shows the steps of a batch: which projects go first, and which are published at the same time.
/// </summary>
internal static class PublishPlan
{
    /// <summary>
    /// The bracket that joins the rows of a step: <c>┌ ├ └</c> for a step of several projects, <c>─</c> for one alone.
    /// </summary>
    /// <param name="index">The row's place in its step (0 first).</param>
    /// <param name="stepSize">How many projects the step has.</param>
    /// <param name="unicode">Whether the terminal can draw box characters.</param>
    public static string Connector(int index, int stepSize, bool unicode) => (stepSize, index) switch
    {
        (1, _) => unicode ? "─" : "-",
        (_, 0) => unicode ? "┌" : "+",
        _ when index == stepSize - 1 => unicode ? "└" : "+",
        _ => unicode ? "├" : "|"
    };

    /// <summary>
    /// The plan in words (plain text), e.g. "Api and Worker at the same time, then Web".
    /// </summary>
    /// <param name="names">The projects' names, each with its step, in any order.</param>
    /// <param name="parallel">The most projects published at once, if limited.</param>
    public static string Describe(IReadOnlyList<(int Step, string Name)> names, int? parallel = null)
    {
        var steps = names.GroupBy(n => n.Step).OrderBy(step => step.Key).Select(step => step.Select(n => n.Name).ToList()).ToList();
        if (names.Count == 1)
        {
            return names[0].Name;
        }

        var limit = parallel is { } max && steps.Any(step => step.Count > max) ? $" ({max} at a time at most)" : string.Empty;
        if (steps.Count == 1)
        {
            return $"All {names.Count} at the same time{limit}";
        }

        if (steps.All(step => step.Count == 1))
        {
            return $"One after another: {string.Join(", then ", steps.Select(step => step[0]))}";
        }

        return string.Join(", then ", steps.Select(step => step.Count == 1 ? step[0] : $"{JoinAnd(step)} at the same time")) + limit;
    }

    /// <summary>"A", "A and B", "A, B and C".</summary>
    private static string JoinAnd(IReadOnlyList<string> names) =>
        names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
}
