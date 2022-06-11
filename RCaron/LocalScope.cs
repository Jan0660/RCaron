using System.Runtime.InteropServices;

namespace RCaron;

public class LocalScope
{
    public Dictionary<string, object?>? Variables;

    public object GetVariable(string name)
    {
        if (Variables == null || !Variables.TryGetValue(name, out var value))
            return RCaronInsideEnum.VariableNotFound;
        return value;
    }

    public bool TryGetVariable(string name, out object? value)
    {
        value = GetVariable(name);
        return !value.Equals(RCaronInsideEnum.VariableNotFound);
    }

    public void SetVariable(string name, in object? value)
    {
        Variables ??= new();
        Variables[name] = value;
    }

    public ref object? GetVariableRef(string name)
        => ref CollectionsMarshal.GetValueRefOrNullRef(Variables, name);

    public bool VariableExists(string name)
        => Variables?.ContainsKey(name) ?? false;
}