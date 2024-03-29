﻿using System.Collections;
using System.Reflection;
using RCaron.Classes;

namespace RCaron;

public interface IAssigner
{
    public void Assign(object? value);
}

public class PropertyAssigner : IAssigner
{
    private readonly PropertyInfo _propertyInfo;
    private readonly object? _instance;

    public PropertyAssigner(PropertyInfo propertyInfo, object? instance)
    {
        _propertyInfo = propertyInfo;
        _instance = instance;
    }

    public void Assign(object? value)
    {
        _propertyInfo.SetValue(_instance, value);
    }
}

public class FieldAssigner : IAssigner
{
    private readonly FieldInfo _fieldInfo;
    private readonly object? _instance;

    public FieldAssigner(FieldInfo fieldInfo, object? instance)
    {
        _fieldInfo = fieldInfo;
        _instance = instance;
    }

    public void Assign(object? value)
    {
        _fieldInfo.SetValue(_instance, value);
    }
}

public class InterfaceListAssigner : IAssigner
{
    private readonly IList _list;
    private readonly IndexerToken _indexerToken;
    private readonly Motor _motor;

    public InterfaceListAssigner(IList list, IndexerToken indexerToken, Motor motor)
    {
        _list = list;
        _indexerToken = indexerToken;
        _motor = motor;
    }

    public void Assign(object? value)
    {
        var g = _motor.EvaluateExpressionHigh(_indexerToken.Tokens.ToArray());
        var asInt = (int)Convert.ChangeType(g, typeof(int))!;
        _list[asInt] = value;
    }
}

public class ClassAssigner : IAssigner
{
    private readonly ClassInstance _classInstance;
    private readonly int _propertyIndex;

    public ClassAssigner(ClassInstance classInstance, int propertyIndex)
    {
        (_classInstance, _propertyIndex) = (classInstance, propertyIndex);
    }

    public void Assign(object? value)
    {
        _classInstance.PropertyValues![_propertyIndex] = value;
    }
}

public class ClassStaticAssigner : IAssigner
{
    private readonly ClassDefinition _classDefinition;
    private readonly int _propertyIndex;

    public ClassStaticAssigner(ClassDefinition classDefinition, int propertyIndex)
    {
        (_classDefinition, _propertyIndex) = (classDefinition, propertyIndex);
    }

    public void Assign(object? value)
    {
        _classDefinition.StaticPropertyValues![_propertyIndex] = value;
    }
}
