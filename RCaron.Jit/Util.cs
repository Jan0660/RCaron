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
}