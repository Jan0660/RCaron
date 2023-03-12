using System.Collections;

namespace RCaron;

public class RCaronRange : IEnumerable
{
    public long Start { get; }
    public long End { get; }

    public RCaronRange(long start, long end)
    {
        Start = start;
        End = end;
    }

// Implementation for the GetEnumerator method.
    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    public RCaronRangeEnumerator GetEnumerator()
    {
        return new RCaronRangeEnumerator(Start, End);
    }
}

public class RCaronRangeEnumerator : IEnumerator
{
    public long Start { get; }
    public long End { get; }

    public bool MoveNext()
    {
        Current = ((long)Current) + 1;
        return (long)Current != End;
    }

    public void Reset()
    {
        Current = Start - 1;
    }

    public object Current { get; private set; }

    public RCaronRangeEnumerator(long start, long end)
    {
        Start = start;
        End = end;
        Current = Start - 1;
    }
}