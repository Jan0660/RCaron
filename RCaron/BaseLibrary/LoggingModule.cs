using RCaron.LibrarySourceGenerator;

namespace RCaron.BaseLibrary;

[Module("LoggingModule")]
public partial class LoggingModule : IRCaronModule
{
    [Method("SayHello")]
    public static object SayHello(Motor motor, in ReadOnlySpan<PosToken> arguments)
    {
        Console.WriteLine("Hello, ř!");
        return RCaronInsideEnum.NoReturnValue;
    }
    [Method("Error")]
    public static void Error(Motor motor, object value)
        => Log73.Console.Error(value.ToString());

    [Method("Warn")]
    public static void Warn(Motor motor, object value)
        => Log73.Console.Warn(value.ToString());

    [Method("Info")]
    public static void Info(Motor motor, object value)
        => Log73.Console.Info(value.ToString());

    [Method("Debug")]
    public static void Debug(Motor motor, object value)
        => Log73.Console.Debug(value.ToString());

    [Method("ForUnitTests")]
    public static long ForUnitTests(Motor motor, long a, long b = 1)
    {
        var v = a + b;
        motor.GlobalScope.SetVariable("global", v);
        return v;
    }
}