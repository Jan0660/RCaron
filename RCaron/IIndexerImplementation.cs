namespace RCaron;

public interface IIndexerImplementation
{
    public bool Do(object? indexerValue, ref object? value, ref Type? type);
}