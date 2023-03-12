using System.CommandLine;
using System.Drawing;
using Log73;
using Log73.LogPres;
using RCaron;
using RCaron.FunLibrary;
using Console = System.Console;

var logger = new Log73Logger();
logger.LogTypes.Info = new LogType("RCaron.Cli", LogLevel.Info, true)
{
    LogPres = new()
    {
        new LogTypeLogPre()
        {
            SpaceOutTo = 12
        },
    },
    LogPreStyle = new()
    {
        ForegroundColor = Color.FromArgb(0x69, 0x69, 0xff),
        AnsiStyle = AnsiStyle.Bold,
    },
};
logger.Configure.EnableVirtualTerminalProcessing();

// Create some options:
var fileArgument = new Argument<FileInfo>(
    "file",
    "An option whose argument is parsed as a FileInfo");
fileArgument.SetDefaultValue(null);
var interactiveOption = new Option<bool>("--interactive",
    "Run interactive.");
interactiveOption.AddAlias("-i");
var funOption = new Option<bool>("--fun",
    "Add experimental stuff module.");
var argsArgument = new Argument<string[]>("arguments", "Arguments to pass to the file");
argsArgument.SetDefaultValue(Array.Empty<string>());

// Add the options to a root command:
var rootCommand = new RootCommand();
rootCommand.AddArgument(fileArgument);
rootCommand.AddOption(interactiveOption);
rootCommand.AddOption(funOption);
rootCommand.AddArgument(argsArgument);

rootCommand.Description = "RCaron.Cli";

rootCommand.SetHandler((FileInfo? f, bool interactive, bool fun, string[] arguments) =>
{
    Motor motor = new(new(null!));
    if (f is not null)
    {
        logger.Info($"Executing file {f.FullName}");
        motor.SetVar("args", arguments);
        motor.UseContext(RCaronRunner.Parse(File.ReadAllText(f.FullName)));
        motor.MainFileScope.FileName = f.FullName;
        if (fun)
            AddFun(motor);
        try
        {
            motor.Run();
        }
        catch (Exception exc)
        {
            try
            {
                logger.Error("Exception thrown while running the file:");
                FunModule.PrintException(exc, motor);
            }
            finally
            {
                logger.Error(exc);
            }
        }
    }

    if (interactive)
    {
        var input = Console.ReadLine();
        while (input != null)
        {
            var ctx = RCaronRunner.Parse(input);
            // todo: doesn't do functions and classes
            motor.UseContext(ctx, false);
            try
            {
                motor.Run();
            }
            catch (Exception exc)
            {
                Console.WriteLine(exc);
            }

            input = Console.ReadLine();
        }
    }
}, fileArgument, interactiveOption, funOption, argsArgument);

// Parse the incoming args and invoke the handler
return rootCommand.Invoke(args);

void AddFun(Motor motor)
{
    motor.MainFileScope.Modules!.Add(new FunModule());
    motor.MainFileScope.Modules.Add(new JsonModule());
    motor.MainFileScope.IndexerImplementations ??= new();
    motor.MainFileScope.IndexerImplementations.Add(new JsonNodeIndexer());
    motor.MainFileScope.PropertyAccessors ??= new();
    motor.MainFileScope.PropertyAccessors.Add(new JsonObjectPropertyAccessor());
}