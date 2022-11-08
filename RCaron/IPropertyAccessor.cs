namespace RCaron;

public interface IPropertyAccessor
{
    public bool Do(Motor motor, string propertyName, ref object? value, ref Type? type);
}