namespace RCaron;

public interface IPropertyAccessor
{
    public bool Do(string propertyName, ref object? value, ref Type? type);
}