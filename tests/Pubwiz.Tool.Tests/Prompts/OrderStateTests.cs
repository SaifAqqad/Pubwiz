using Pubwiz.Tool.Prompts;

namespace Pubwiz.Tool.Tests.Prompts;

public class OrderStateTests
{
    [Fact]
    public void Type_SetsTheNumberAndKeepsTheRowsSortedWithTheCursorOnItsItem()
    {
        var state = new OrderState([1, 1, 1], pageSize: 10);

        state.Type(2); // item 0 goes after the others

        Assert.Equal([2, 1, 1], state.Orders);
        Assert.Equal([1, 2, 0], state.Rows);
        Assert.Equal(0, state.CursorItem);
        Assert.Equal(2, state.CursorRow);
        Assert.Equal([2, 1, 1], state.Steps);
        Assert.Equal(2, state.StepCount);
    }

    [Fact]
    public void Type_IgnoresNumbersAboveTheItemCount()
    {
        var state = new OrderState([1, 1], pageSize: 10);

        state.Type(5);
        state.Type(0);

        Assert.Equal([1, 1], state.Orders);
    }

    [Fact]
    public void Type_CombinesDigitsWhileTheNumberFits()
    {
        var state = new OrderState(Enumerable.Repeat(1, 12).ToList(), pageSize: 20);

        state.Type(1);
        state.Type(2);
        Assert.Equal(12, state.Orders[0]);

        // 12 then 3 doesn't fit, so 3 starts over.
        state.Type(3);
        Assert.Equal(3, state.Orders[0]);
    }

    [Fact]
    public void Change_StaysWithinOneToTheItemCount()
    {
        var state = new OrderState([1, 1], pageSize: 10);

        state.Change(-1);
        Assert.Equal(1, state.Orders[0]);

        state.Change(5);
        Assert.Equal(2, state.Orders[0]);
    }

    [Fact]
    public void OneByOne_NumbersTheRowsInTheOrderTheyreShown()
    {
        var state = new OrderState([2, 1, 2], pageSize: 10);

        state.OneByOne();

        Assert.Equal([2, 1, 3], state.Orders);
        Assert.Equal(3, state.StepCount);
    }

    [Fact]
    public void AllAtOnce_MakesOneStep()
    {
        var state = new OrderState([1, 2, 3], pageSize: 10);

        state.AllAtOnce();

        Assert.Equal(1, state.StepCount);
        Assert.Equal(3, state.StepSize(0));
    }

    [Fact]
    public void Steps_RemoveGaps()
    {
        var state = new OrderState([3, 1, 3], pageSize: 10);

        Assert.Equal([2, 1, 2], state.Steps);
    }

    [Fact]
    public void Move_FollowsTheSortedRows()
    {
        var state = new OrderState([2, 1], pageSize: 10);

        // The cursor starts on item 0, shown second; down wraps to the first row.
        state.Move(1);

        Assert.Equal(1, state.CursorItem);
    }
}
