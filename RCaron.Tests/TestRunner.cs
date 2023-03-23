using RCaron.Parsing;

namespace RCaron.Tests;

public static class TestRunner
{
    public static (RCaronParserContext context, Motor motor) Prepare(string code,
        Dictionary<string, object?>? variables = null,
        MotorOptions? motorOptions = null, List<IIndexerImplementation>? indexers = null,
        List<IPropertyAccessor>? propertyAccessors = null)
    {
        var ctx = RCaronParser.Parse(code);
        var motor = new Motor(ctx, motorOptions);
        if (variables != null)
            foreach (var (key, value) in variables)
                motor.SetVar(key, value);
        if (indexers != null)
            motor.MainFileScope.IndexerImplementations = indexers;
        if (propertyAccessors != null)
            motor.MainFileScope.PropertyAccessors = propertyAccessors;
        return (ctx, motor);
    }

    public static void RunPrepared(RCaronParserContext ctx, Motor motor)
    {
#if !RCARONJIT
        motor.Run();
#elif RCARONJIT
        // ReSharper disable twice RedundantNameQualifier
        // ReSharper disable once InvokeAsExtensionMethod
        System.Linq.Enumerable
            .First(System.AppDomain.CurrentDomain.GetAssemblies(), ass => ass.GetName().Name == "RCaron.Jit")
            .GetType("RCaron.Jit.Hook")!.GetMethod("Run")!.Invoke(null, new object?[] { ctx, null, motor });
#endif
    }

    public static void RunPrepared((RCaronParserContext context, Motor motor) prepared)
    {
        RunPrepared(prepared.context, prepared.motor);
    }

    public static Motor Run(string code, Dictionary<string, object?>? variables = null,
        MotorOptions? motorOptions = null, List<IIndexerImplementation>? indexers = null,
        List<IPropertyAccessor>? propertyAccessors = null)
    {
        var (ctx, motor) = Prepare(code, variables, motorOptions, indexers, propertyAccessors);
        RunPrepared(ctx, motor);
        return motor;
    }
}