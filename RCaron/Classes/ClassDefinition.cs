namespace RCaron.Classes;

public sealed class ClassDefinition
{
    public string Name { get; }
    public string[]? PropertyNames { get; }
    public PosToken[]?[]? PropertyInitializers { get; }

    public Dictionary<string, Function>? Functions { get; init; }
    // todo(feat): constructor

    public ClassDefinition(string name, string[]? propertyNames, PosToken[]?[]? propertyInitializers)
    {
        (Name, PropertyNames, PropertyInitializers) = (name, propertyNames, propertyInitializers);
    }

    public int GetPropertyIndex(ReadOnlySpan<char> name)
    {
        if (PropertyNames == null) return -1;
        for (var i = 0; i < PropertyNames.Length; i++)
        {
            if (name.Equals(PropertyNames[i], StringComparison.InvariantCultureIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}