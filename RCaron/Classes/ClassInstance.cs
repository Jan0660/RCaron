using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Linq.Expressions;

namespace RCaron.Classes;

public sealed class ClassInstance : IDynamicMetaObjectProvider
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
        => Definition.GetPropertyIndex(name);

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


    public DynamicMetaObject GetMetaObject(Expression parameter)
    {
        return new ClassInstanceDynamicMetaObject(parameter, this);
    }

    private class ClassInstanceDynamicMetaObject : DynamicMetaObject
    {
        internal ClassInstanceDynamicMetaObject(
            System.Linq.Expressions.Expression parameter,
            ClassInstance value)
            : base(parameter, BindingRestrictions.Empty, value)
        {
        }

        public override DynamicMetaObject BindGetMember(GetMemberBinder binder)
        {
            var instance = (ClassInstance)Value!;
            var index = instance.Definition.GetPropertyIndex(binder.Name);
            if (index != -1)
            {
                return new DynamicMetaObject(
                    Expression.ArrayAccess(
                        Expression.Property(Expression.Convert(Expression, LimitType), nameof(PropertyValues)),
                        Expression.Constant(index)
                    ),
                    GetDefinitionRestriction());
            }
            // {
            //     var restrictions = BindingRestrictions.GetTypeRestriction(Expression, LimitType);
            //     // var propertyValues = Expression.Property(Expression.Constant(instance), nameof(PropertyValues));
            //     // var propertyValue = Expression.ArrayAccess(propertyValues, Expression.Constant(index));
            //     // return new DynamicMetaObject(propertyValue, restrictions);
            //     return new DynamicMetaObject(Expression.Constant(value), restrictions);
            // }

            return base.BindGetMember(binder);
        }

        public override DynamicMetaObject BindSetMember(SetMemberBinder binder, DynamicMetaObject value)
        {
            var instance = (ClassInstance)Value!;
            var index = instance.Definition.GetPropertyIndex(binder.Name);
            if (index != -1)
            {
                // var restrictions = BindingRestrictions.GetExpressionRestriction(
                //     Expression.Equal(
                //         Expression.Property(Expression.Convert(Expression, LimitType), nameof(Definition)), Expression.Constant(instance.Definition)));
                var propertyValues =
                    Expression.Property(Expression.Convert(Expression, LimitType), nameof(PropertyValues));
                var propertyValue = Expression.ArrayAccess(propertyValues, Expression.Constant(index));
                var assign = Expression.Assign(propertyValue, Expression.Convert(value.Expression, typeof(object)));
                var block = Expression.Block(assign);
                return new DynamicMetaObject(block, GetDefinitionRestriction());
            }

            return base.BindSetMember(binder, value);
        }
        
        private BindingRestrictions GetDefinitionRestriction()
        {
            var instance = (ClassInstance)Value!;
            return BindingRestrictions.GetExpressionRestriction(
                Expression.Equal(
                    Expression.Property(Expression.Convert(Expression, LimitType), nameof(Definition)), Expression.Constant(instance.Definition)));
        }
    }
}