using System.ComponentModel;
using Console = Log73.Console;

namespace RCaron.Shell;

public class Shell
{
    public Motor Motor { get; }
    public Function? PromptFunction { get; set; }
    public bool PrintShellExceptions { get; set; } = true;

    public Shell()
    {
        Motor = new(new(null!));
        Motor.InvokeRunExecutable = InvokeRunExecutable;
    }

    private object? InvokeRunExecutable(Motor motor, string name, ArraySegment<PosToken> args, FileScope fileScope,
        Pipeline? pipeline, bool isLeftOfPipeline)
    {
        try
        {
            return RunExecutable.Run(motor, name, args, fileScope.Raw, pipeline, isLeftOfPipeline);
        }
        catch (Win32Exception win32Exception) when (win32Exception.NativeErrorCode == 2)
        {
            throw new RCaronShellException($"Command not found: {name}", win32Exception);
        }
    }

    public void RunString(string code, bool import = true, string? fileName = null)
    {
        var ctx = RCaronRunner.Parse(code);
        if (import && Motor.MainFileScope != null!)
        {
            Motor.MainFileScope.Raw = code;
            if (ctx.FileScope.Functions is not null)
            {
                Motor.MainFileScope.Functions ??= new();
                foreach (var f in ctx.FileScope.Functions)
                {
                    Motor.MainFileScope.Functions[f.Key] = f.Value with { FileScope = Motor.MainFileScope };
                }
            }

            if (ctx.FileScope.ClassDefinitions is not null)
            {
                Motor.MainFileScope.ClassDefinitions ??= new();
                foreach (var c in ctx.FileScope.ClassDefinitions)
                {
                    var index = Motor.MainFileScope.ClassDefinitions.FindIndex(@class =>
                        @class.Name.Equals(c.Name, StringComparison.InvariantCultureIgnoreCase));
                    if (c.Functions != null)
                        foreach (var f in c.Functions)
                        {
                            c.Functions[f.Key] = f.Value with { FileScope = Motor.MainFileScope };
                        }

                    if (c.StaticFunctions != null)
                        foreach (var f in c.StaticFunctions)
                        {
                            c.StaticFunctions[f.Key] = f.Value with { FileScope = Motor.MainFileScope };
                        }

                    if (index == -1)
                        Motor.MainFileScope.ClassDefinitions.Add(c);
                    else
                        Motor.MainFileScope.ClassDefinitions[index] = c;
                }
            }
        }

        var isFirstRun = Motor.MainFileScope == null!;
        Motor.UseContext(ctx, isFirstRun);
        if (isFirstRun)
            Motor.MainFileScope!.Modules!.Add(new ShellStuffModule(this));
        if (import && Motor.MainFileScope != null!)
            Motor.MainFileScope.FileName = fileName;
        try
        {
            Motor.Run();
        }
        catch (RCaronShellException e)
        {
            if (PrintShellExceptions)
                Console.Error(e.Message);
            else throw;
        }
    }
}