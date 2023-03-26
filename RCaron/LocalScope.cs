using System.Runtime.InteropServices;
using RCaron.Classes;

namespace RCaron;

public class LocalScope
{
    public static Dictionary<string, object?> GetNewVariablesDictionary()
        => new(StringComparer.InvariantCultureIgnoreCase);

    public Dictionary<string, object?>? Variables { get; private set; }

    public Dictionary<string, object?> GetVariables()
        => Variables ??= GetNewVariablesDictionary();

    public virtual object? GetVariable(string name)
    {
        if (Variables == null || !Variables.TryGetValue(name, out var value))
            return RCaronInsideEnum.VariableNotFound;
        if (value is LetVariableValue letVal)
            value = letVal.Value;
        return value;
    }

    public virtual bool TryGetVariable(string name, out object? value)
    {
        value = GetVariable(name);
        return !value?.Equals(RCaronInsideEnum.VariableNotFound) ?? true;
    }

    public virtual void SetVariable(string name, in object? value)
    {
        Variables ??= GetNewVariablesDictionary();
        ref var r = ref CollectionsMarshal.GetValueRefOrAddDefault(Variables, name, out var exists);
        if (exists && r is LetVariableValue letVal && !letVal.Type.IsInstanceOfType(value))
        {
            throw RCaronException.LetVariableTypeMismatch(name, letVal.Type, value?.GetType() ?? typeof(object));
        }

        r = value;
    }

    public virtual ref object? GetVariableRef(string name)
    {
        Variables ??= GetNewVariablesDictionary();
        return ref CollectionsMarshal.GetValueRefOrNullRef(Variables, name);
    }

    public virtual bool VariableExists(string name)
        => Variables?.ContainsKey(name) ?? false;
}

public class ClassFunctionScope : ClassStaticFunctionScope
{
    public ClassInstance ClassInstance { get; }

    public ClassFunctionScope(ClassInstance classInstance) : base(classInstance.Definition)
        => ClassInstance = classInstance;

    public override object? GetVariable(string name)
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

public class ClassStaticFunctionScope : LocalScope
{
    public ClassDefinition ClassDefinition { get; }
    public ClassStaticFunctionScope(ClassDefinition classDefinition) => ClassDefinition = classDefinition;

    public override object? GetVariable(string name)
    {
        if (ClassDefinition.TryGetStaticPropertyValue(name, out var propVal))
            return propVal;
        return base.GetVariable(name);
    }

    public override bool TryGetVariable(string name, out object? value)
    {
        if (ClassDefinition.TryGetStaticPropertyValue(name, out value))
            return true;
        return base.TryGetVariable(name, out value);
    }

    public override void SetVariable(string name, in object? value)
    {
        var index = ClassDefinition.GetStaticPropertyIndex(name);
        if (index == -1)
            base.SetVariable(name, in value);
        else
            ClassDefinition.StaticPropertyValues![index] = value;
    }

    public override bool VariableExists(string name)
    {
        var index = ClassDefinition.GetStaticPropertyIndex(name);
        return index != -1 || base.VariableExists(name);
    }

    public override ref object? GetVariableRef(string name)
    {
        var index = ClassDefinition.GetStaticPropertyIndex(name);
        if (index == -1)
            return ref base.GetVariableRef(name);
        else
        {
            return ref ClassDefinition.StaticPropertyValues![index];
        }
    }
}