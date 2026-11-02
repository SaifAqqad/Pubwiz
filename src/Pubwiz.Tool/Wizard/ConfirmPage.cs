namespace Pubwiz.Tool.Wizard;

/// <summary>
/// The last question before publishing. A batch picked in the wizard can also be saved as a preset here
/// (a single project's profile is saved from its profile list instead).
/// </summary>
public sealed class ConfirmPage(IAnsiConsole console, PresetPage presets)
{
    private enum Choice
    {
        Publish,
        SaveAsPreset,
        Cancel
    }

    /// <param name="batch">What would be published.</param>
    /// <param name="cancelLabel">The cancel option, e.g. "Cancel" or "Back to projects".</param>
    /// <returns><c>false</c> when the user cancels (with the cancel option or Esc).</returns>
    public bool Confirm(PublishBatch batch, string cancelLabel)
    {
        console.WriteLine();
        var canSavePreset = batch.PresetFilePath is not null && batch.Selections.Count > 1;
        while (true)
        {
            List<Choice> choices = canSavePreset ? [Choice.Publish, Choice.SaveAsPreset, Choice.Cancel] : [Choice.Publish, Choice.Cancel];

            Choice choice;
            try
            {
                choice = console.Choose(Question(batch), choices, c => c switch
                {
                    Choice.Publish => "Publish now",
                    Choice.SaveAsPreset => "Save this selection as a preset",
                    _ => cancelLabel
                });
            }
            catch (PromptCancelledException)
            {
                return false;
            }

            if (choice != Choice.SaveAsPreset)
            {
                return choice == Choice.Publish;
            }

            presets.Save(batch.Selections, batch.PresetFilePath!, parallel: batch.Parallel);
            canSavePreset = false;
            console.WriteLine();
        }
    }

    /// <summary>e.g. "Publish 3 project(s), 2 at a time?"</summary>
    private static string Question(PublishBatch batch)
    {
        var count = batch.Selections.Count;
        return batch.Parallel switch
        {
            > 1 when batch.Parallel >= count => $"Publish {count} project(s), all at once?",
            > 1 => $"Publish {count} project(s), {batch.Parallel} at a time?",
            _ => $"Publish {count} project(s)?"
        };
    }
}
