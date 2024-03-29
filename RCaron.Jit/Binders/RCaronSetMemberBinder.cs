﻿using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using RCaron.Binders;
using RCaron.Classes;

namespace RCaron.Jit.Binders;

public class RCaronSetMemberBinder : SetMemberBinder
{
    public FileScope FileScope { get; }

    public RCaronSetMemberBinder(string name, bool ignoreCase, FileScope fileScope) : base(name, ignoreCase)
    {
        FileScope = fileScope;
    }

    public override DynamicMetaObject FallbackSetMember(DynamicMetaObject target, DynamicMetaObject value,
        DynamicMetaObject? errorSuggestion)
    {
        if (target.LimitType.IsAssignableTo(typeof(IDynamicMetaObjectProvider)))
        {
            return new DynamicMetaObject(
                Expression.Call(target.Expression.EnsureIsType(target.LimitType), "GetMetaObject", Array.Empty<Type>(),
                    Expression.Constant(target.Expression)),
                BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType));
        }

        // static RCaron class property
        if (target.Value is ClassDefinition classDefinition)
        {
            var staticPropertyIndex = classDefinition.GetStaticPropertyIndex(Name);
            if (staticPropertyIndex == -1)
                throw RCaronException.ClassStaticPropertyNotFound(Name);
            return new DynamicMetaObject(
                Expression.Assign(Expression.ArrayAccess(Expression.Constant(classDefinition.StaticPropertyValues),
                    Expression.Constant(staticPropertyIndex)), value.Expression.EnsureIsType(typeof(object))),
                Shared.GetSameClassDefinitionRestrictions(target, classDefinition));
        }

        var property = target.RuntimeType?.GetProperty(Name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property != null)
        {
            var exp = Expression
                .Assign(Expression.Property(target.Expression, property),
                    Expression.Convert(value.Expression, property.PropertyType)).EnsureIsType(ReturnType);
            return new DynamicMetaObject(exp, GetRestrictions(target));
        }

        var field = target.RuntimeType?.GetField(Name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (field != null)
        {
            var exp = Expression.Assign(Expression.Field(target.Expression, field),
                Expression.Convert(value.Expression, field.FieldType)).EnsureIsType(ReturnType);
            return new DynamicMetaObject(exp, GetRestrictions(target));
        }

        throw new Exception();
    }

    private BindingRestrictions GetRestrictions(DynamicMetaObject target)
        => target.Restrictions.Merge(BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType));
}