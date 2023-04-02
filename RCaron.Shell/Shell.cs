namespace RCaron.Shell;

public class Shell
{
    public Motor Motor { get; }
    public Function? PromptFunction { get; set; }

    public Shell()
    {
        Motor = new(new(null!));
        Motor.InvokeRunExecutable = InvokeRunExecutable;
    }

    private object? InvokeRunExecutable(Motor motor, string name, ArraySegment<PosToken> args, FileScope fileScope,
        IPipeline? pipeline, bool isLeftOfPipeline)
    {
        return RunExecutable.Run(motor, name, args, fileScope.Raw, pipeline, isLeftOfPipeline);
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
                    Motor.MainFileScope.Functions[f.Key] = f.Value;
            }

            if (ctx.FileScope.ClassDefinitions is not null)
            {
                // todo: doesn't overwrite existing classes with the same name
                Motor.MainFileScope.ClassDefinitions ??= new();
                foreach (var c in ctx.FileScope.ClassDefinitions)
                    Motor.MainFileScope.ClassDefinitions.Add(c);
            }
        }

        var isFirstRun = Motor.MainFileScope == null!;
        Motor.UseContext(ctx, Motor.MainFileScope == null!);
        if (isFirstRun)
            Motor.MainFileScope!.Modules!.Add(new ShellStuffModule(this));
        if (import && Motor.MainFileScope != null!)
            Motor.MainFileScope.FileName = fileName;
        Motor.Run();
    }
}