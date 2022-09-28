using System.Diagnostics.CodeAnalysis;

namespace RCaron.Classes;

public sealed class ClassInstance
{
    public ClassDefinition Definition { get; }
    public object?[]? PropertyValues { get; }

    public ClassInstance(ClassDefinition definition)
    {
        Definition = definition;
        if (definition.PropertyNames != null)
            PropertyValues = new object[definition.PropertyNames.Length];
    }

    public int GetPropertyIndex(ReadOnlySpan<char> name)
    {
        for (var i = 0; i < Definition.PropertyNames.Length; i++)
        {
            if (name.Equals(Definition.PropertyNames[i], StringComparison.InvariantCultureIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public bool TryGetPropertyValue(ReadOnlySpan<char> name, out object? value)
    {
        var index = GetPropertyIndex(name);
        if (index == -1)
        {
            value = null;
            return false;
        }

        value = PropertyValues![index];
        return true;
    }
}