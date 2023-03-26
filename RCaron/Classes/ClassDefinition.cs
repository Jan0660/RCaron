using System.Diagnostics.CodeAnalysis;

namespace RCaron.Classes;

public sealed class ClassDefinition
{
    public string Name { get; }
    public string[]? PropertyNames { get; }
    public PosToken[]?[]? PropertyInitializers { get; }

    public Dictionary<string, Function>? Functions { get; init; }
    public Dictionary<string, Function>? StaticFunctions { get; init; }
    public string[]? StaticPropertyNames { get; init; }
    public object?[]? StaticPropertyValues { get; init; }

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

    public int GetStaticPropertyIndex(ReadOnlySpan<char> name)
    {
        if (StaticPropertyNames == null) return -1;
        for (var i = 0; i < StaticPropertyNames.Length; i++)
        {
            if (name.Equals(StaticPropertyNames[i], StringComparison.InvariantCultureIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public bool TryGetStaticPropertyValue(ReadOnlySpan<char> name, out object? value)
    {
        var index = GetStaticPropertyIndex(name);
        if (index == -1)
        {
            value = null;
            return false;
        }

        value = StaticPropertyValues![index];
        return true;
    }
}