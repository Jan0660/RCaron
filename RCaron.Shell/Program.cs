using System.CommandLine;
using System.Drawing;
using Log73;
using Log73.LogPres;
using PrettyPrompt;
using PrettyPrompt.Highlighting;
using RCaron.FunLibrary;
using RCaron.Shell;
using RCaron.Shell.Prompt;
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

// options and arguments
var fileArgument = new Argument<FileInfo?>(
    "file",
    "A file to execute.");
fileArgument.SetDefaultValue(null);
var interactiveOption = new Option<bool?>("--interactive", () => null,
    "Run interactive. If no file is specified, true is the default.");
interactiveOption.AddAlias("-i");
var argsArgument = new Argument<string[]>("arguments", "Arguments to pass to the file");
argsArgument.SetDefaultValue(Array.Empty<string>());

// Add the options to a root command:
var rootCommand = new RootCommand();
rootCommand.AddArgument(fileArgument);
rootCommand.AddOption(interactiveOption);
rootCommand.AddArgument(argsArgument);

rootCommand.Description = "RCaron Shell";
rootCommand.SetHandler(async context =>
{
    var file = context.ParseResult.GetValueForArgument(fileArgument);
    var interactive = context.ParseResult.GetValueForOption(interactiveOption);
    var arguments = context.ParseResult.GetValueForArgument(argsArgument);
    var shell = new Shell();
    var profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rcaron");
    var profileHistoryPath = Path.Combine(profilePath, "history");

    var promptConfig = new PromptConfiguration(proportionOfWindowHeightForCompletionPane: 0.1)
    {
        Prompt = new(),
    };
    var promptCallbacks = new RCaronPromptCallbacks();
    var prompt = new Prompt(configuration: promptConfig, callbacks: promptCallbacks,
        persistentHistoryFilepath: profileHistoryPath);
    shell.Motor.SetVar("args", arguments);
    shell.Motor.SetVar("current_shell", shell);
    shell.Motor.SetVar("prompt_callbacks", promptCallbacks);

    var profileFile = Path.Combine(profilePath, "profile.rcaron");
    if (File.Exists(profileFile))
    {
        logger.Debug($"Executing profile file {profileFile}");
        shell.RunString(File.ReadAllText(profileFile), true, profileFile);
    }

    if (file is not null)
    {
        logger.Info($"Executing file {file.FullName}");
        // motor.UseContext(RCaronRunner.Parse(File.ReadAllText(f.FullName)));
        // motor.MainFileScope.FileName = f.FullName;
        // if (fun)
        //     AddFun(motor);
        try
        {
            shell.RunString(File.ReadAllText(file.FullName), true, file.FullName);
        }
        catch (Exception exc)
        {
            try
            {
                logger.Error("Exception thrown while running the file:");
                FunModule.PrintException(exc, shell.Motor);
            }
            finally
            {
                logger.Error(exc);
            }
        }

        interactive ??= false;
    }

    if (interactive ?? true)
    {
        while (true)
        {
            if (shell.PromptFunction != null)
            {
                var promptResult = shell.Motor.FunctionCall(shell.PromptFunction);
                promptConfig.Prompt = new FormattedString(promptResult?.ToString());
            }

            var input = await prompt.ReadLineAsync();
            if (!input.IsSuccess)
                break;
            var inputText = input.Text;
            // if (inputText == null)
            //     break;
            // Console.WriteLine(input.Text);
            // var ctx = RCaronRunner.Parse(input);
            // // todo: doesn't do functions and classes
            // motor.UseContext(ctx, false);
            try
            {
                shell.RunString(inputText);
            }
            catch (Exception exc)
            {
                Console.WriteLine(exc);
            }
        }
    }
});

// Parse the incoming args and invoke the handler
return await rootCommand.InvokeAsync(args);