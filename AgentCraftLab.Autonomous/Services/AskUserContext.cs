namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// Per-execution context for the ask_user meta-tool.
/// Tool sets the flag; ReactExecutor checks it after each iteration.
/// </summary>
public sealed class AskUserContext
{
    public bool IsWaiting { get; private set; }
    public string Question { get; private set; } = "";
    public string InputType { get; private set; } = "text";
    public string? Choices { get; private set; }

    public void RequestInput(string question, string inputType = "text", string? choices = null)
    {
        IsWaiting = true;
        Question = question;
        InputType = inputType;
        Choices = choices;
    }

    public void Reset()
    {
        IsWaiting = false;
        Question = "";
        InputType = "text";
        Choices = null;
    }
}
