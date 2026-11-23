using Pubwiz.Tool.Prompts;
using Spectre.Console;

namespace Pubwiz.Tool.Tests.Prompts;

public class KeyHintsTests
{
    private static readonly (string, string)[] Keys = [("↑/↓", "move"), ("ctrl+a", "select all"), ("enter", "confirm"), ("esc", "back")];

    [Fact]
    public void AllKeysAreShownWhenTheyFit()
    {
        var hints = Markup.Remove(KeyHints.ToMarkup(Keys, width: 80, "ctrl+a", "↑/↓"));

        Assert.Equal("↑/↓ move · ctrl+a select all · enter confirm · esc back", hints);
    }

    [Fact]
    public void OptionalKeysAreLeftOutInOrderUntilTheyFit()
    {
        Assert.Equal("↑/↓ move · enter confirm · esc back", Markup.Remove(KeyHints.ToMarkup(Keys, width: 40, "ctrl+a", "↑/↓")));
        Assert.Equal("enter confirm · esc back", Markup.Remove(KeyHints.ToMarkup(Keys, width: 30, "ctrl+a", "↑/↓")));
    }
}
