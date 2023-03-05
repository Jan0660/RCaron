namespace RCaron.Shell.Prompt;

public ref struct StackStack<T> where T : unmanaged
{
    private readonly Span<T> _span;
    private int _index;

    public StackStack(ref Span<T> span)
    {
        _span = span;
        _index = 0;
    }

    public StackStack(Span<T> span)
    {
        _span = span;
        _index = 0;
    }

    public void Push(T value)
    {
        _span[_index++] = value;
    }

    public T Pop()
    {
        return _span[--_index];
    }

    public T Peek()
    {
        return _span[_index - 1];
    }

    public void Clear()
    {
        _index = 0;
    }

    public int Count => _index;

    public bool IsEmpty => _index == 0;

    public bool IsFull => _index == _span.Length;
}