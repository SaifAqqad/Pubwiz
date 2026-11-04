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

    [Theory]
    [InlineData("contoso-api", "con", 0)]
    [InlineData("my-contoso", "con", 1)]
    [InlineData("acontoso", "con", 2)]
    [InlineData("api", "con", -1)]
    public void Rank_PrefersPrefixesThenWordStarts(string text, string search, int rank)
    {
        Assert.Equal(rank, MultiSelectState.Rank(text, search));
    }

    [Fact]
    public void SetSearch_ListsPrefixMatchesFirst()
    {
        var state = new MultiSelectState(["acontoso", "my-contoso", "contoso-api", "contoso-web"], [true, true, true, true], pageSize: 10);

        state.SetSearch("con");

        Assert.Equal([2, 3, 1, 0], state.VisibleItems);
        Assert.Equal(2, state.CursorItem);
    }

    [Fact]
    public void ToggleCursor_MovesTheItemToTheSelectedSectionAndKeepsTheCursorOnTheNextItem()
    {
        var state = Create(4);
        state.MoveTo(1);

        state.ToggleCursor();

        Assert.Equal([1], state.Selected);
        Assert.Equal([0, 2, 3], state.VisibleItems);
        Assert.Equal(MultiSelectSection.List, state.Focus);
        Assert.Equal(2, state.CursorItem);
    }

    [Fact]
    public void ToggleCursor_OnTheLastItem_StaysOnTheLastItemInsteadOfTheExitRow()
    {
        var state = Create(3, hasExitRow: true);
        state.MoveTo(2);

        state.ToggleCursor();

        Assert.False(state.IsOnExitRow);
        Assert.Equal(1, state.CursorItem);
    }

    [Fact]
    public void SwitchSection_MovesBetweenTheListAndTheSelection()
    {
        var state = Create(4);
        state.ToggleCursor(); // 0
        state.ToggleCursor(); // 1, the cursor is now on 2

        state.SwitchSection();
        Assert.Equal(MultiSelectSection.Selected, state.Focus);
        Assert.Equal(0, state.CursorItem);

        state.Move(1);
        Assert.Equal(1, state.CursorItem);

        state.SwitchSection();
        Assert.Equal(MultiSelectSection.List, state.Focus);
        Assert.Equal(2, state.CursorItem);
    }

    [Fact]
    public void SwitchSection_DoesNothingWithoutASelection()
    {
        var state = Create(2);

        state.SwitchSection();

        Assert.Equal(MultiSelectSection.List, state.Focus);
    }

    [Fact]
    public void ToggleCursor_InTheSelection_PutsTheItemBackAndKeepsBothCursors()
    {
        var state = Create(5);
        state.ToggleCursor(); // 0
        state.ToggleCursor(); // 1
        state.ToggleCursor(); // 2, the list cursor is now on 3
        state.SwitchSection();
        state.Move(1); // 1

        state.ToggleCursor();

        Assert.Equal([0, 2], state.Selected);
        Assert.Equal([1, 3, 4], state.VisibleItems);
        Assert.Equal(2, state.CursorItem); // the next selected item

        state.SwitchSection();
        Assert.Equal(3, state.CursorItem);
    }

    [Fact]
    public void UnselectingTheLastSelectedItem_ReturnsToTheList()
    {
        var state = Create(3);
        state.ToggleCursor();
        state.SwitchSection();

        state.ToggleCursor();

        Assert.Empty(state.Selected);
        Assert.Equal(MultiSelectSection.List, state.Focus);
    }

    [Fact]
    public void Search_ReturnsToTheListAndKeepsTheSelectionVisible()
    {
        var state = new MultiSelectState(["Shop.Api", "Shop.Web", "Billing.Api"], [true, true, true], pageSize: 10);
        state.ToggleCursor(); // Shop.Api
        state.SwitchSection();

        state.SetSearch("api");

        Assert.Equal(MultiSelectSection.List, state.Focus);
        Assert.Equal([0], state.Selected);
        Assert.Equal([2], state.VisibleItems);
    }

    [Fact]
    public void SelectedSection_TakesAtMostHalfThePage()
    {
        var state = Create(20, pageSize: 10);
        state.ToggleAll();

        Assert.Equal(5, state.SelectedPageSize);
        Assert.Equal(10 - 5 - MultiSelectState.SelectedChromeHeight, state.ListPageSize);

        Assert.Equal(MultiSelectSection.Selected, state.Focus); // everything is selected, so the list is empty
        state.MoveToEnd();
        Assert.Equal(19, state.SelectedCursor);
        Assert.Equal(15, state.SelectedOffset);
    }

    [Fact]
    public void AlwaysShownSelectedSection_KeepsItsRowsWhileNothingIsSelected()
    {
        var state = new MultiSelectState(["A", "B"], [true, true], pageSize: 10, alwaysShowSelected: true);

        Assert.True(state.ShowsSelected);
        Assert.Equal(10 - 1 - MultiSelectState.SelectedChromeHeight, state.ListPageSize);

        state.ToggleCursor();
        Assert.Equal(10 - 1 - MultiSelectState.SelectedChromeHeight, state.ListPageSize);
    }

    [Fact]
    public void SelectingEverything_MovesTheCursorOffTheExitRow()
    {
        var state = Create(3, hasExitRow: true);

        state.ToggleAll();

        Assert.False(state.IsOnExitRow);
        Assert.Equal(MultiSelectSection.Selected, state.Focus);
    }

    [Fact]
    public void SelectingTheLastListedItem_MovesTheCursorToTheSelection()
    {
        var state = Create(2, hasExitRow: true);
        state.ToggleCursor();

        state.ToggleCursor();

        Assert.Equal(MultiSelectSection.Selected, state.Focus);
        Assert.Equal([0, 1], state.Selected);
    }
}
