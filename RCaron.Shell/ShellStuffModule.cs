using RCaron.LibrarySourceGenerator;

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
        Environment.CurrentDirectory = Path.GetFullPath(path);
    }
    
    [Method("Set-Prompt")]
    public void SetPrompt(Motor _, string functionName)
    {
        Shell.PromptFunction = Shell.Motor.MainFileScope!.Functions![functionName];
    }
}