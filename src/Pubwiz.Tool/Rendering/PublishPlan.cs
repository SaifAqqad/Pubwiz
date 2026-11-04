namespace Pubwiz.Tool.Rendering;

/// <summary>
/// Shows the steps of a batch: which projects go first, and which are published at the same time.
/// </summary>
internal static class PublishPlan
{
    /// <summary>
    /// The most projects we recommend publishing at the same time. Each one is a full build, so more than this tends
    /// to make all of them slower on a typical machine, not faster.
    /// </summary>
    public const int RecommendedAtOnce = 4;

    /// <summary>The tip shown when more than <see cref="RecommendedAtOnce"/> would be published at the same time (markup).</summary>
    public static string TooManyAtOnceTip =>
        $"[yellow]Tip:[/] [dim]each project is a full build, so more than {RecommendedAtOnce} at the same time tends to slow them all down. {RecommendedAtOnce} at a time is recommended.[/]";

    /// <summary>
    /// Whether a step would publish more than <see cref="RecommendedAtOnce"/> projects at the same time.
    /// </summary>
    /// <param name="steps">The step of each project.</param>
    /// <param name="parallel">The limit on projects at once, if any.</param>
    public static bool TooManyAtOnce(IEnumerable<int> steps, int? parallel)
    {
        var largestStep = steps.GroupBy(step => step).Select(step => step.Count()).DefaultIfEmpty(0).Max();
        return Math.Min(largestStep, parallel ?? int.MaxValue) > RecommendedAtOnce;
    }

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
