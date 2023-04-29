using System.Collections;
using Dynamitey;
using RCaron.LibrarySourceGenerator;

namespace RCaron.BaseLibrary;

[Module("LoggingModule")]
public partial class LoggingModule : IRCaronModule
{
    [Method("SayHello", Description = "Says hello to ř.")]
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

    [Method("MeasurePipelineCount")]
    public static long MeasurePipelineCount(Motor motor, [FromPipeline] Pipeline pipeline)
    {
        var count = 0;
        var enumerator = pipeline.GetEnumerator();
        while (enumerator.MoveNext())
            count++;

        return count;
    }

    [Method("SetVarFromPipeline")]
    public static void SetVarFromPipeline(Motor motor, string name, [FromPipeline] Pipeline pipeline)
    {
        object? obj;
        if (pipeline is SingleObjectPipeline sop)
            obj = sop.Object;
        else
        {
            var e = pipeline.GetEnumerator();
            if (!e.MoveNext())
                throw new("Pipeline is empty.");
            obj = e.Current;
        }

        motor.GlobalScope.SetVariable(name, obj);
    }

    [Method("SetVarFromPipelineObject")]
    public static void SetVarFromPipeline(Motor motor, string name, [FromPipeline] object obj)
    {
        motor.GlobalScope.SetVariable(name, obj);
    }

    [Method("EnumeratorRange")]
    public static IEnumerator EnumeratorRange(Motor motor, int start, int end)
    {
        for (var i = start; i < end; i++)
            yield return i;
    }

    [Method("EnumerableRange")]
    public static IEnumerable EnumerableRange(Motor motor, int start, int end)
    {
        return new RCaronRange(start, end);
    }

    [Method("MeasureEnumeratorCount")]
    public static long MeasureEnumeratorCount(Motor motor, [FromPipeline] IEnumerator enumerator)
    {
        var count = 0;
        while (enumerator.MoveNext())
            count++;

        return count;
    }

    [Method("Increment")]
    public static int Increment(Motor motor, [FromPipeline] int value)
    {
        return value + 1;
    }

    [Method("Enumerate")]
    public static IEnumerator Enumerate(Motor _, [FromPipeline] object obj)
        => obj switch
        {
            IEnumerable enumerable => enumerable.GetEnumerator(),
            IEnumerator enumerator => enumerator,
            Pipeline pipeline => pipeline.GetEnumerator(),
            _ => new SingleObjectEnumerator(obj),
        };

    private class SingleObjectEnumerator : IEnumerator
    {
        private readonly object _obj;
        private bool _moved;
        public SingleObjectEnumerator(object obj) => _obj = obj;

        public bool MoveNext()
        {
            if (_moved)
                return false;
            Current = _obj;
            return _moved = true;
        }

        public void Reset()
        {
            throw new();
        }

        public object Current { get; private set; }
    }

    [Method("ConvertTo")]
    public object ConvertTo(Motor _, [FromPipeline] object obj, Type type)
    {
        if (type.IsInstanceOfType(obj))
            return obj;
        return Dynamic.InvokeConvert(obj, type, true);
    }
}