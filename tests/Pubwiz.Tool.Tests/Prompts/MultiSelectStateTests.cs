using Pubwiz.Tool.Prompts;

namespace Pubwiz.Tool.Tests.Prompts;

public class MultiSelectStateTests
{
    [Fact]
    public void Move_WrapsAroundBothEnds()
    {
        var state = new MultiSelectState([true, true, true], pageSize: 10);

        state.Move(-1);
        Assert.Equal(2, state.Cursor);

        state.Move(1);
        Assert.Equal(0, state.Cursor);
    }

    [Fact]
    public void Move_ScrollsToKeepTheCursorVisible()
    {
        var state = new MultiSelectState(Enumerable.Repeat(true, 10).ToList(), pageSize: 3);

        state.MoveTo(5);
        Assert.Equal(3, state.Offset);

        state.MoveTo(1);
        Assert.Equal(1, state.Offset);
    }

    [Fact]
    public void ToggleCursor_IgnoresUnselectableItems()
    {
        var state = new MultiSelectState([false, true], pageSize: 10);

        state.ToggleCursor();
        Assert.Empty(state.Selected);

        state.Move(1);
        state.ToggleCursor();
        Assert.Equal([1], state.Selected);
    }

    [Fact]
    public void ToggleAll_SelectsEverythingThenClears()
    {
        var state = new MultiSelectState([true, false, true], pageSize: 10);

        state.ToggleAll();
        Assert.Equal([0, 2], state.Selected);

        state.ToggleAll();
        Assert.Empty(state.Selected);
    }
}
