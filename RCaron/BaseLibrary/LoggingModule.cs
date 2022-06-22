using RCaron.LibrarySourceGenerator;

namespace RCaron.BaseLibrary;

[Module("LoggingModule")]
public partial class LoggingModule : IRCaronModule
{
    [Method("SayHello")]
    public static object? SayHello(Motor motor, in ReadOnlySpan<PosToken> arguments)
    {
        Console.WriteLine("Hello, ř!");
        return RCaronInsideEnum.NoReturnValue;
    }

    [Method("Warn")]
    public static void Warn(Motor motor, string value)
    {
        Log73.Console.Warn(value);
    }

    [Method("ForUnitTests")]
    public static long ForUnitTests(Motor motor, long a, long b = 1)
    {
        var v = a + b;
        motor.GlobalScope.SetVariable("global", v);
        return v;
    }
}