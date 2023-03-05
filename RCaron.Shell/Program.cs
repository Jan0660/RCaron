using System.CommandLine;
using System.CommandLine.Invocation;
using System.Drawing;
using Log73;
using Log73.LogPres;
using PrettyPrompt;
using PrettyPrompt.Highlighting;
using RCaron;
using RCaron.FunLibrary;
using RCaron.Shell;
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
var interactiveOption = new Option<bool>("--interactive", () => true,
    "Run interactive.");
interactiveOption.AddAlias("-i");
var argsArgument = new Argument<string[]>("arguments", "Arguments to pass to the file");
argsArgument.SetDefaultValue(Array.Empty<string>());

// Add the options to a root command:
var rootCommand = new RootCommand();
rootCommand.AddArgument(fileArgument);
rootCommand.AddOption(interactiveOption);
rootCommand.AddArgument(argsArgument);

rootCommand.Description = "RCaron Shell";
rootCommand.SetHandler((Func<InvocationContext, Task>)(async (context) =>
{
    var file = context.ParseResult.GetValueForArgument(fileArgument);
    var interactive = context.ParseResult.GetValueForOption(interactiveOption);
    var arguments = context.ParseResult.GetValueForArgument(argsArgument);
    await Task.CompletedTask;
    var promptConfig = new PromptConfiguration(proportionOfWindowHeightForCompletionPane: 0.1)
    {
        Prompt = new(),
    };
    var prompt = new Prompt(configuration: promptConfig, callbacks: new RCaronPromptCallbacks());
    var shell = new Shell();
    shell.Motor.SetVar("args", arguments);
    shell.Motor.SetVar("current_shell", shell);
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
    }

    var profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rcaron");
    var profileFile = Path.Combine(profilePath, "profile.rcaron");
    if (File.Exists(profileFile))
    {
        logger.Debug($"Executing profile file {profileFile}");
        shell.RunString(File.ReadAllText(profileFile), true, profileFile);
    }

    if (interactive)
    {
        while (true)
        {
            
            if (shell.PromptFunction != null)
            {
                var promptResult = shell.Motor.FunctionCall(shell.PromptFunction);
                promptConfig.Prompt = new FormattedString(promptResult.ToString());
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
}));

// Parse the incoming args and invoke the handler
return await rootCommand.InvokeAsync(args);


void AddFun(Motor motor)
{
    motor.MainFileScope.Modules.Add(new FunModule());
    motor.MainFileScope.Modules.Add(new JsonModule());
    motor.MainFileScope.IndexerImplementations ??= new();
    motor.MainFileScope.IndexerImplementations.Add(new JsonNodeIndexer());
    motor.MainFileScope.PropertyAccessors ??= new();
    motor.MainFileScope.PropertyAccessors.Add(new JsonObjectPropertyAccessor());
}
