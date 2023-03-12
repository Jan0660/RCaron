using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;

namespace RCaron;

public record RCaronType(Type Type) : IDynamicMetaObjectProvider
{
    public DynamicMetaObject GetMetaObject(Expression parameter)
    {
        return new RCaronTypeDynamicMetaObject(parameter, this);
    }

    private class RCaronTypeDynamicMetaObject : DynamicMetaObject
    {
        internal RCaronTypeDynamicMetaObject(
            Expression parameter,
            RCaronType value)
            : base(parameter, BindingRestrictions.Empty, value)
        {
        }

        public override DynamicMetaObject BindGetMember(GetMemberBinder binder)
        {
            var restrictions = GetRestrictions();
            var instance = (RCaronType)Value!;
            // try property
            var property = instance.Type.GetProperty(binder.Name,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (property != null)
            {
                var exp = Expression.Property(null, property);
                return new DynamicMetaObject(exp.Type == typeof(object) ? exp : Expression.Convert(exp, typeof(object)),
                    restrictions);
            }

            // try field
            var field = instance.Type.GetField(binder.Name,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (field != null)
            {
                var exp = Expression.Field(null, field);
                return new DynamicMetaObject(exp.Type == typeof(object) ? exp : Expression.Convert(exp, typeof(object)),
                    restrictions);
            }
            // return new DynamicMetaObject(
            //     Expression.PropertyOrField(Expression.Convert(Expression, instance.Type), binder.Name),
            //     GetRestrictions());
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
            var instance = (RCaronType)Value!;
            // var restrictions = BindingRestrictions.GetExpressionRestriction(
            //     Expression.Equal(
            //         Expression.Property(Expression.Convert(Expression, LimitType), nameof(Definition)), Expression.Constant(instance.Definition)));
            var property = instance.Type.GetProperty(binder.Name,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (property != null)
            {
                Expression exp = Expression.Assign(Expression.Property(null, property), Expression.Convert(value.Expression, property.PropertyType));
                if(exp.Type != binder.ReturnType)
                    exp = Expression.Convert(exp, binder.ReturnType);
                return new DynamicMetaObject(exp, GetRestrictions());
            }
            
            var field = instance.Type.GetField(binder.Name,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (field != null)
            {
                Expression exp = Expression.Assign(Expression.Field(null, field), Expression.Convert(value.Expression, field.FieldType));
                if(exp.Type != binder.ReturnType)
                    exp = Expression.Convert(exp, binder.ReturnType);
                return new DynamicMetaObject(exp, GetRestrictions());
            }
            // var h = (MemberInfo?) ??
            //         (MemberInfo?)instance.Type.GetField(binder.Name,
            //             BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
            //
            
            // var propertyValues =
            //     Expression.Property(Expression.Convert(Expression, LimitType), nameof(PropertyValues));
            // var propertyValue = Expression.ArrayAccess(propertyValues, Expression.Constant(index));
            // var assign = Expression.Assign(propertyValue, Expression.Convert(value.Expression, typeof(object)));
            // var block = Expression.Block(assign);
            // return new DynamicMetaObject(block, GetRestrictions());
            return base.BindSetMember(binder, value);
        }

        private BindingRestrictions GetRestrictions()
        {
            var val = (RCaronType)Value!;
            return BindingRestrictions.GetExpressionRestriction(
                Expression.Equal(
                    Expression.Property(Expression.Convert(Expression, LimitType), nameof(Type)),
                    Expression.Constant(val.Type)));
        }
    }
}