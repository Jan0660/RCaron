using RCaron.LibrarySourceGenerator;
using Console = Log73.Console;

namespace RCaron.Shell;

[Module("shell")]
public partial class ShellStuffModule : IRCaronModule
{
    public Shell Shell { get; }

    public ShellStuffModule(Shell shell)
    {
        Shell = shell;
    }

    [Method("cd", Description = $"Changes current directory to {nameof(path)}.")]
    public void Cd(Motor _, string path)
    {
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception e)
        {
            throw new RCaronShellException($"Could not get full path: {e.Message}", e);
        }

        try
        {
            Environment.CurrentDirectory = path;
        }
        catch (DirectoryNotFoundException e)
        {
            throw new RCaronShellException($"Directory not found: {path}", e);
        }
    }

    [Method("Set-Prompt", Description = "Sets the prompt function to the function with the given name.")]
    public void SetPrompt(Motor _, string functionName)
    {
        if(!(Shell.Motor.MainFileScope.Functions?.TryGetValue(functionName, out var function) ?? false))
            throw new RCaronShellException($"Function '{functionName}' not found.");
        Shell.PromptFunction = function;
    }

    [Method("Exit", Description = "Exits the process with the given exit code.")]
    public void Exit(Motor _, int code = 0)
    {
        Environment.Exit(code);
    }
}