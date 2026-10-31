using Pubwiz.Tool.Prompts;

namespace Pubwiz.Tool.Tests.Prompts;

public class MultiSelectStateTests
{
    private static MultiSelectState Create(int count, int pageSize = 10, bool hasExitRow = false) =>
        new(Enumerable.Range(0, count).Select(i => $"Item{i}").ToList(), Enumerable.Repeat(true, count).ToList(), pageSize, hasExitRow);

    [Fact]
    public void Move_WrapsAroundBothEnds()
    {
        var state = Create(3);

        state.Move(-1);
        Assert.Equal(2, state.Cursor);

        state.Move(1);
        Assert.Equal(0, state.Cursor);
    }

    [Fact]
    public void Move_ScrollsToKeepTheCursorVisible()
    {
        var state = Create(10, pageSize: 3);

        state.MoveTo(5);
        Assert.Equal(3, state.Offset);

        state.MoveTo(1);
        Assert.Equal(1, state.Offset);
    }

    [Fact]
    public void ToggleCursor_IgnoresUnselectableItems()
    {
        var state = new MultiSelectState(["A", "B"], [false, true], pageSize: 10);

        state.ToggleCursor();
        Assert.Empty(state.Selected);

        state.Move(1);
        state.ToggleCursor();
        Assert.Equal([1], state.Selected);
    }

    [Fact]
    public void ToggleAll_SelectsEverythingThenClears()
    {
        var state = new MultiSelectState(["A", "B", "C"], [true, false, true], pageSize: 10);

        state.ToggleAll();
        Assert.Equal([0, 2], state.Selected);

        state.ToggleAll();
        Assert.Empty(state.Selected);
    }

    [Fact]
    public void SetSearch_ShowsOnlyMatchesIgnoringCase()
    {
        var state = new MultiSelectState(["Shop.Api", "Shop.Web", "Billing.Api"], [true, true, true], pageSize: 10);

        state.SetSearch("api");

        Assert.Equal([0, 2], state.VisibleItems);
        Assert.Equal(0, state.CursorItem);
    }

    [Fact]
    public void ToggleAll_WithSearch_OnlyAffectsMatchesAndKeepsHiddenSelections()
    {
        var state = new MultiSelectState(["Shop.Api", "Shop.Web", "Billing.Api"], [true, true, true], pageSize: 10);
        state.Move(1);
        state.ToggleCursor(); // Shop.Web

        state.SetSearch("api");
        state.ToggleAll();

        Assert.Equal([0, 1, 2], state.Selected);
    }

    [Fact]
    public void ExitRow_ComesAfterTheItemsAndStaysWhenNothingMatches()
    {
        var state = Create(2, hasExitRow: true);

        state.MoveTo(2);
        Assert.True(state.IsOnExitRow);
        Assert.Null(state.CursorItem);

        state.SetSearch("nothing like this");
        Assert.Equal(1, state.RowCount);
        Assert.True(state.IsOnExitRow);
    }
}
