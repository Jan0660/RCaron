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
            path = Path.GetFullPath(PathResolver.Instance.Resolve(path));
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
        if (!(Shell.Motor.MainFileScope.Functions?.TryGetValue(functionName, out var function) ?? false))
            throw new RCaronShellException($"Function '{functionName}' not found.");
        Shell.PromptFunction = function;
    }

    [Method("Get-ExecAlias", Description = "Gets the executable under the given alias. Returns null if not found.")]
    public string? GetExecAlias(Motor _, string alias)
    {
        if (Shell.ExecutableAliases.TryGetValue(alias, out var exec))
            return exec;
        return null;
    }

    [Method("Set-ExecAlias", Description = "Sets the alias to the given executable. Errors if executable is null.")]
    public void SetExecAlias(Motor _, string alias, string executable)
    {
        if (executable is null)
            throw new RCaronShellException("Executable cannot be null.");
        Shell.ExecutableAliases[alias] = executable;
    }

    [Method("Exit", Description = "Exits the process with the given exit code.")]
    public void Exit(Motor _, int code = 0)
    {
        Environment.Exit(code);
    }
}