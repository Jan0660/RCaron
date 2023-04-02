using System.Collections;

namespace RCaron;

public interface IPipeline
{
    public IEnumerator GetEnumerator();
}

public class SingleObjectPipeline : IPipeline
{
    public object? Object { get; }

    public SingleObjectPipeline(object? obj)
    {
        Object = obj;
    }

    public IEnumerator GetEnumerator()
    {
        yield return Object;
    }
}

public class EnumeratorPipeline : IPipeline
{
    public IEnumerator Enumerator { get; init; }

    public EnumeratorPipeline(IEnumerator enumerator)
    {
        Enumerator = enumerator;
    }

    public IEnumerator GetEnumerator()
        => Enumerator;
}

public class StreamPipeline : IPipeline
{
    public StreamReader StreamReader { get; init; }

    public StreamPipeline(StreamReader streamReader)
    {
        StreamReader = streamReader;
    }

    public IEnumerator GetEnumerator()
    {
        while (!StreamReader.EndOfStream)
        {
            yield return StreamReader.ReadLine();
        }
    }
}
