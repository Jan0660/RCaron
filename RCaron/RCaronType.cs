using System.Dynamic;
using System.Linq.Expressions;

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
            System.Linq.Expressions.Expression parameter,
            RCaronType value)
            : base(parameter, BindingRestrictions.Empty, value)
        {
        }

        public override DynamicMetaObject BindGetMember(GetMemberBinder binder)
        {
            var instance = (RCaronType)Value!;
            return new DynamicMetaObject(
                Expression.PropertyOrField(Expression.Convert(Expression, instance.Type), binder.Name),
                GetRestrictions());
            // {
            //     var restrictions = BindingRestrictions.GetTypeRestriction(Expression, LimitType);
            //     // var propertyValues = Expression.Property(Expression.Constant(instance), nameof(PropertyValues));
            //     // var propertyValue = Expression.ArrayAccess(propertyValues, Expression.Constant(index));
            //     // return new DynamicMetaObject(propertyValue, restrictions);
            //     return new DynamicMetaObject(Expression.Constant(value), restrictions);
            // }

            return base.BindGetMember(binder);
        }

        // public override DynamicMetaObject BindSetMember(SetMemberBinder binder, DynamicMetaObject value)
        // {
        //     var instance = (ClassInstance)Value!;
        //     var index = instance.Definition.GetPropertyIndex(binder.Name);
        //     if (index != -1)
        //     {
        //         // var restrictions = BindingRestrictions.GetExpressionRestriction(
        //         //     Expression.Equal(
        //         //         Expression.Property(Expression.Convert(Expression, LimitType), nameof(Definition)), Expression.Constant(instance.Definition)));
        //         var propertyValues =
        //             Expression.Property(Expression.Convert(Expression, LimitType), nameof(PropertyValues));
        //         var propertyValue = Expression.ArrayAccess(propertyValues, Expression.Constant(index));
        //         var assign = Expression.Assign(propertyValue, Expression.Convert(value.Expression, typeof(object)));
        //         var block = Expression.Block(assign);
        //         return new DynamicMetaObject(block, GetDefinitionRestriction());
        //     }
        //
        //     return base.BindSetMember(binder, value);
        // }
        
        public override DynamicMetaObject BindInvokeMember(InvokeMemberBinder binder, DynamicMetaObject[] args)
        {
            var instance = (RCaronType)Value!;
            // todo: doesn't do overloads
            var method = instance.Type.GetMethod(binder.Name);
            if (method != null)
            {
                var restrictions = GetRestrictions();
                var parameters = method.GetParameters();
                var arguments = new Expression[parameters.Length];
                // todo: doesn't work with optional parameters and named parameters
                for (var i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    var argument = args[i].Expression;
                    if (parameter.ParameterType != argument.Type)
                    {
                        argument = Expression.Convert(argument, parameter.ParameterType);
                    }

                    arguments[i] = argument;
                }

                Expression final = Expression.Call(/*Expression.Convert(Expression, instance.Type)*/null, method, arguments);
                if (final.Type != binder.ReturnType)
                {
                    final = Expression.Convert(final, binder.ReturnType);
                }
                return new DynamicMetaObject(final, restrictions);
            }

            if (binder.Name.Equals("new", StringComparison.InvariantCultureIgnoreCase))
            {
                
            }

            throw new();
            // return base.BindInvokeMember(binder, args);
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