namespace RCaron;

public class LetVariableValue
{
    public Type Type { get; }
    public object? Value { get; set; }
    public LetVariableValue(Type type, object? value)
    {
        Type = type;
        Value = value;
    }
}