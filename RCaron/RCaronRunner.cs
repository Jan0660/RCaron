using RCaron.Parsing;

namespace RCaron;
public static class RCaronRunner
{
    public static Motor Run(string text, MotorOptions? motorOptions = null)
    {
        var ctx = Parse(text);
        if (!ctx.AllowsExecution)
            throw RCaronException.ExecutionNotAllowed();
        var motor = new Motor(ctx, motorOptions);
        motor.Run();
        return motor;
    }

    public static RCaronParserContext Parse(string text, bool returnDescriptive = false)
        => RCaronParser.Parse(text);
}
