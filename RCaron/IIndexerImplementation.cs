namespace RCaron;

public interface IIndexerImplementation
{
    public bool Do(Motor motor, object? indexerValue, ref object? value, ref Type? type);
}