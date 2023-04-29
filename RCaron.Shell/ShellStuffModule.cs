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

    [Method("cd")]
    public void Cd(Motor _, string path)
    {
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception e)
        {
            Console.Error("Could not get full path: " + e.Message);
            return;
        }

        try
        {
            Environment.CurrentDirectory = path;
        }
        catch (DirectoryNotFoundException)
        {
            Console.Error("Directory not found: " + path);
        }
    }

    [Method("Set-Prompt")]
    public void SetPrompt(Motor _, string functionName)
    {
        Shell.PromptFunction = Shell.Motor.MainFileScope.Functions![functionName];
    }

    [Method("Exit")]
    public void Exit(Motor _, int code = 0)
    {
        Environment.Exit(code);
    }
}