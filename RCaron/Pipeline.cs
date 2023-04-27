using System.Collections;
using System.Threading.Channels;

namespace RCaron;

public abstract class Pipeline
{
    public bool EnumeratorGotten { get; private set; }
    protected abstract IEnumerator _getEnumerator();

    public IEnumerator GetEnumerator()
    {
        if (EnumeratorGotten)
        {
            throw new InvalidOperationException("Cannot get enumerator twice");
        }

        EnumeratorGotten = true;
        return _getEnumerator();
    }
}

public class SingleObjectPipeline : Pipeline
{
    public object? Object { get; }

    public SingleObjectPipeline(object? obj)
    {
        Object = obj;
    }

    protected override IEnumerator _getEnumerator()
    {
        yield return Object;
    }
}

public class EnumeratorPipeline : Pipeline
{
    public IEnumerator Enumerator { get; }

    public EnumeratorPipeline(IEnumerator enumerator)
    {
        Enumerator = enumerator;
    }

    protected override IEnumerator _getEnumerator()
        => Enumerator;
}

public class ChannelPipeline : Pipeline
{
    public Channel<object?> Channel { get; }

    public ChannelPipeline()
    {
        Channel = System.Threading.Channels.Channel.CreateBounded<object?>(new BoundedChannelOptions(68)
        {
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    protected override IEnumerator _getEnumerator()
    {
        while (Channel.Reader.TryRead(out var obj))
        {
            yield return obj;
        }
    }

    public void Write(object? obj)
    {
        if (obj is IEnumerator enumerator)
        {
            while (enumerator.MoveNext())
            {
                Channel.Writer.TryWrite(enumerator.Current);
            }
        }

        if (obj is IEnumerable enumerable)
        {
            foreach (var val in enumerable)
            {
                Channel.Writer.TryWrite(val);
            }
        }
        else
        {
            Channel.Writer.TryWrite(obj);
        }
    }
}

public class StreamPipeline : Pipeline
{
    public StreamReader StreamReader { get; init; }

    public StreamPipeline(StreamReader streamReader)
    {
        StreamReader = streamReader;
    }

    protected override IEnumerator _getEnumerator()
    {
        while (!StreamReader.EndOfStream)
        {
            yield return StreamReader.ReadLine();
        }
    }
}

public class FuncEnumerator : IEnumerator
{
    private readonly Func<object?, object?> _func;
    private readonly IEnumerator _enumerator;

    public FuncEnumerator(Func<object?, object?> func, IEnumerator enumerator)
    {
        _func = func;
        _enumerator = enumerator;
    }

    public bool MoveNext()
    {
        if (!_enumerator.MoveNext())
            return false;
        Current = _func(_enumerator.Current);
        return true;
    }

    public void Reset()
    {
        throw new Exception($"{nameof(FuncEnumerator)} does not support {nameof(Reset)}.");
    }

    public object? Current { get; private set; }
}