using System.Dynamic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.CSharp.RuntimeBinder;

namespace RCaron.Binders;

public static class BinderUtil
{
    public static Expression EnsureIsType(this Expression exp, Type type)
    {
        if (exp.Type != type)
            return Expression.Convert(exp, type);
        return exp;
    }

    public static BindingRestrictions GetValidOnceRestriction()
    {
        var thing = new ValidOnceRestrictionThing();
        var v = Expression.Variable(typeof(ValidOnceRestrictionThing), "v");
        return BindingRestrictions.GetExpressionRestriction(
            Expression.Block(typeof(bool), new[] { v },
                Expression.Assign(v, Expression.Constant(thing)),
                Expression.Or(
                    Expression.Property(v, nameof(ValidOnceRestrictionThing.Valid)),
                    Expression.Assign(Expression.Property(v, nameof(ValidOnceRestrictionThing.Valid)),
                        Expression.Constant(false)))));
    }

    private class ValidOnceRestrictionThing
    {
        public bool Valid { get; set; } = true;
    }

    public static BindingRestrictions SameTypeRCaronTypeRestrictions(DynamicMetaObject target, RCaronType rCaronType)
        => BindingRestrictions.GetExpressionRestriction(Expression.Equal(
            Expression.Property(target.Expression.EnsureIsType(typeof(RCaronType)), nameof(RCaronType.Type)),
            Expression.Constant(rCaronType.Type)));

    public static CallSiteBinder GetBinaryOperationBinder(OperationEnum operation)
    {
        return Binder.BinaryOperation(CSharpBinderFlags.None, operation switch
        {
            OperationEnum.Sum => ExpressionType.Add,
            OperationEnum.Subtract => ExpressionType.Subtract,
            OperationEnum.Multiply => ExpressionType.Multiply,
            OperationEnum.Divide => ExpressionType.Divide,
            OperationEnum.Modulo => ExpressionType.Modulo,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Invalid operation.")
        }, null, new[]
        {
            CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
            CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
        });
    }

    public static CallSite<Func<CallSite, object, object, object>> GetBinaryOperationCallSite(OperationEnum operation)
    {
        var b = GetBinaryOperationBinder(operation);
        var callsite = CallSite<Func<CallSite, object, object, object>>.Create(b);
        return callsite;
    }

    public static CallSiteBinder GetComparisonOperationBinder(OperationEnum operation)
    {
        return Binder.BinaryOperation(CSharpBinderFlags.None, operation switch
        {
            OperationEnum.IsEqual => ExpressionType.Equal,
            OperationEnum.IsNotEqual => ExpressionType.NotEqual,
            OperationEnum.IsGreater => ExpressionType.GreaterThan,
            OperationEnum.IsGreaterOrEqual => ExpressionType.GreaterThanOrEqual,
            OperationEnum.IsLess => ExpressionType.LessThan,
            OperationEnum.IsLessOrEqual => ExpressionType.LessThanOrEqual,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Invalid comparison operation."),
        }, null, new[]
        {
            CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
            CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
        });
    }

    public static CallSite<Func<CallSite, object?, object?, object>> GetComparisonOperationCallSite(
        OperationEnum operation)
    {
        var b = GetComparisonOperationBinder(operation);
        var callsite = CallSite<Func<CallSite, object?, object?, object>>.Create(b);
        return callsite;
    }
}