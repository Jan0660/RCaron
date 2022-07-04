using System.CommandLine;
using System.Drawing;
using System.IO;
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
var lintOption = new Option<bool>("--lint",
    "Print code funny colored.");
lintOption.AddAlias("-l");
var funOption = new Option<bool>("--fun",
    "Add experimental stuff module.");

// Add the options to a root command:
var rootCommand = new RootCommand();
rootCommand.AddArgument(fileArgument);
rootCommand.AddOption(interactiveOption);
rootCommand.AddOption(lintOption);
rootCommand.AddOption(funOption);

rootCommand.Description = "RCaron.Cli";

rootCommand.SetHandler((FileInfo? f, bool interactive, bool lint, bool fun) =>
{
    RCaronRunner.GlobalLog = lint ? RCaronRunnerLog.FunnyColors: 0;
    Motor motor = new(new(null!, null!));
    if(fun)
        motor.Modules.Add(new FunModule());
    if (f is not null)
    {
        logger.Info($"Executing file {f.FullName}");
        motor.UseContext(RCaronRunner.Parse(File.ReadAllText(f.FullName)));
        motor.Run();
    }

    RCaronRunner.GlobalLog = 0;

    if (interactive)
    {
        var input = Console.ReadLine();
        while (input != null)
        {
            var ctx = RCaronRunner.Parse(input);
            motor.UseContext(ctx);
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
}, fileArgument, interactiveOption, lintOption, funOption);

// Parse the incoming args and invoke the handler
return rootCommand.Invoke(args);