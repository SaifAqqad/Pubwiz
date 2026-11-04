namespace Pubwiz.Core.Publishing;

/// <summary>
/// The order the projects of a batch are published in. Each project has an order number: lower numbers go first,
/// and projects with the same number are published at the same time. Each number is a step of the batch.
/// </summary>
public static class PublishOrder
{
    /// <summary>
    /// Turns a batch's order numbers (as typed, or missing) into steps numbered 1, 2, ... without gaps.
    /// </summary>
    /// <param name="orders">One per project, in batch order; <c>null</c> when the project has none.</param>
    /// <param name="parallel">
    /// The batch's parallel setting. Only used when no project has an order (presets saved before orders existed):
    /// those were published in list order, <paramref name="parallel"/> at a time, so they become one step when it's
    /// more than 1, and one step per project otherwise.
    /// </param>
    /// <returns>The step of each project, in batch order.</returns>
    /// <remarks>Projects without an order, when others have one, go after them, one at a time in list order.</remarks>
    public static int[] ToSteps(IReadOnlyList<int?> orders, int? parallel = null)
    {
        if (orders.All(order => order is null))
        {
            return parallel > 1
                ? Enumerable.Repeat(1, orders.Count).ToArray()
                : Enumerable.Range(1, orders.Count).ToArray();
        }

        var last = orders.Max(order => order ?? 0);
        var filled = orders.Select(order => order ?? ++last).ToArray();
        var distinct = filled.Distinct().Order().ToList();
        return filled.Select(order => distinct.IndexOf(order) + 1).ToArray();
    }
}
