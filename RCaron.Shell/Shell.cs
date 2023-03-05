namespace RCaron.Shell;

public class Shell
{
    public Motor Motor { get; }
    public Function? PromptFunction { get; set; }

    public Shell()
    {
        Motor = new(new(null!));
    }

    public void RunString(string code, bool import = true, string? fileName = null)
    {
        var ctx = RCaronRunner.Parse(code);
        if (import && Motor.MainFileScope != null!)
        {
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

        Motor.UseContext(ctx, Motor.MainFileScope == null!);
        if (import)
            Motor.MainFileScope.FileName = fileName;
        Motor.Run();
    }
}