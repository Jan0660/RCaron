using System.Runtime.InteropServices;
using RCaron.Classes;

namespace RCaron;

public class LocalScope
{
    public Dictionary<string, object?>? Variables;

    public virtual object GetVariable(string name)
    {
        if (Variables == null || !Variables.TryGetValue(name, out var value))
            return RCaronInsideEnum.VariableNotFound;
        return value;
    }

    public virtual bool TryGetVariable(string name, out object? value)
    {
        value = GetVariable(name);
        return !value.Equals(RCaronInsideEnum.VariableNotFound);
    }

    public virtual void SetVariable(string name, in object? value)
    {
        Variables ??= new();
        Variables[name] = value;
    }

    public virtual ref object? GetVariableRef(string name)
    {
        Variables ??= new();
        return ref CollectionsMarshal.GetValueRefOrNullRef(Variables, name);
    }

    public virtual bool VariableExists(string name)
        => Variables?.ContainsKey(name) ?? false;
}

public class ClassFunctionScope : LocalScope
{
    public ClassInstance ClassInstance { get; }
    public ClassFunctionScope(ClassInstance classInstance) => ClassInstance = classInstance;

    public override object GetVariable(string name)
    {
        if (ClassInstance.TryGetPropertyValue(name, out var propVal))
            return propVal;
        return base.GetVariable(name);
    }

    public override bool TryGetVariable(string name, out object? value)
    {
        if (ClassInstance.TryGetPropertyValue(name, out value))
            return true;
        return base.TryGetVariable(name, out value);
    }

    public override void SetVariable(string name, in object? value)
    {
        var index = ClassInstance.GetPropertyIndex(name);
        if (index == -1)
            base.SetVariable(name, in value);
        else
            ClassInstance.PropertyValues![index] = value;
    }

    public override bool VariableExists(string name)
    {
        var index = ClassInstance.GetPropertyIndex(name);
        return index != -1 || base.VariableExists(name);
    }

    public override ref object? GetVariableRef(string name)
    {
        var index = ClassInstance.GetPropertyIndex(name);
        if (index == -1)
            return ref base.GetVariableRef(name);
        else
        {
            return ref ClassInstance.PropertyValues![index];
        }
    }
}