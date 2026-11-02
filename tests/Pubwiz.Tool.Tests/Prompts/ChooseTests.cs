using Pubwiz.Tool.Prompts;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Pubwiz.Tool.Tests.Prompts;

public class ChooseTests
{
    private enum Color
    {
        Red,
        Green
    }

    [Fact]
    public void EnterPicksTheFirstItemEvenWhenItIsntTheTypesDefault()
    {
        // Spectre on its own starts on the item equal to default(T): false, and Red here.
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);

        Assert.True(console.Choose("Sure?", [true, false]));
        Assert.Equal(Color.Green, console.Choose("Color", [Color.Green, Color.Red]));
    }
}
