using RCaron.Parsing;

namespace RCaron;
public static class RCaronRunner
{
    public static Motor Run(string text, MotorOptions? motorOptions = null)
    {
        var ctx = Parse(text);
        if (!ctx.AllowsExecution)
            throw RCaronException.ExecutionNotAllowed();
#if RCARONJIT
        var fakedMotor =
            // ReSharper disable twice RedundantNameQualifier
            // ReSharper disable once InvokeAsExtensionMethod
            System.Linq.Enumerable.First(System.AppDomain.CurrentDomain.GetAssemblies(), ass => ass.GetName().Name == "RCaron.Jit").GetType("RCaron.Jit.Hook")!.GetMethod("Run")!.Invoke(null, new object[] { ctx, motorOptions, null });
        return (Motor)fakedMotor!;
#endif
        var motor = new Motor(ctx, motorOptions);
        motor.Run();
        return motor;
    }

    public static RCaronParserContext Parse(string text, bool returnDescriptive = false)
        => RCaronParser.Parse(text);
}
