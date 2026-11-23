using Pubwiz.Tool.Prompts;

namespace Pubwiz.Tool.Tests.Prompts;

public class ListColumnsTests
{
    [Fact]
    public void ColumnsThatFit_AreUnchanged()
    {
        Assert.Equal([10, 6, 5], ListColumns.Fit([10, 6, 5], available: 100));
    }

    [Fact]
    public void TheLastColumn_IsCutFirst()
    {
        // 10 + 3 + 6 + 3 + 10 = 32, so the last column loses 4.
        Assert.Equal([10, 6, 6], ListColumns.Fit([10, 6, 10], available: 28));
    }

    [Fact]
    public void AColumnTooNarrowToRead_IsHidden()
    {
        // 45 wide: cutting "Windows" (7) by 6 would leave 1, so it's hidden, with its gap.
        Assert.Equal([20, 12, 0], ListColumns.Fit([20, 12, 7], available: 39));
    }

    [Fact]
    public void TheFirstColumn_IsCutOnlyWhenItDoesntFitAlone()
    {
        Assert.Equal([15, 0, 0], ListColumns.Fit([30, 20, 7], available: 15));
    }

    [Fact]
    public void TotalWidth_CountsGapsBetweenVisibleColumnsOnly()
    {
        Assert.Equal(10 + 3 + 6, ListColumns.TotalWidth([10, 0, 6]));
    }
}
