using System.Dynamic;
using System.Linq.Expressions;

namespace RCaron.Jit;

public static class Util
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
            Expression.Block(typeof(bool), new[]{v},
                Expression.Assign(v, Expression.Constant(thing)),
                Expression.Or(
                    Expression.Property(v, nameof(ValidOnceRestrictionThing.Valid)),
                    Expression.Assign(Expression.Property(v, nameof(ValidOnceRestrictionThing.Valid)), Expression.Constant(false)))));
    }

    private class ValidOnceRestrictionThing
    {
        public bool Valid { get; set; } = true;
    }
}