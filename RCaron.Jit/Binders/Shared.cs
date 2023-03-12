using System.Dynamic;
using System.Linq.Expressions;
using RCaron.Binders;
using RCaron.Classes;

namespace RCaron.Jit.Binders;

public static class Shared
{
    public static DynamicMetaObject DoFunction(CompiledFunction func, DynamicMetaObject target,
        DynamicMetaObject[] args,
        string name, CallInfo callInfo, Type returnType, BindingRestrictions restrictions)
    {
        Expression?[]? exps = null;
        if (func.OriginalFunction.Arguments is not null)
        {
            var arguments = func.OriginalFunction.Arguments;
            if (callInfo.ArgumentCount > arguments.Length)
                throw RCaronException.LeftOverPositionalArgument();
            var startIndex = target.LimitType == typeof(ClassInstance) ? 1 : 0;
            exps = new Expression?[arguments.Length + startIndex];
            if(startIndex == 1)
                exps[0] = target.Expression;
            for (var i = 0; i < callInfo.ArgumentCount - callInfo.ArgumentNames.Count; i++)
                exps[i+startIndex] = args[i].Expression;
            for (var index = 0; index < callInfo.ArgumentNames.Count; index++)
            {
                var namedArg = callInfo.ArgumentNames[index];
                var found = false;
                for (var i = 0; i < arguments.Length; i++)
                    if (arguments[i].Name.Equals(namedArg,
                            StringComparison.InvariantCultureIgnoreCase))
                    {
                        exps[i+startIndex] = args[index + callInfo.ArgumentCount - callInfo.ArgumentNames.Count]
                            .Expression;
                        found = true;
                        break;
                    }

                if (!found)
                    throw RCaronException.NamedArgumentNotFound(namedArg);
            }

            // assign default values
            for (var i = startIndex; i < exps.Length; i++)
                if (exps[i] == null)
                {
                    if (!arguments[i-startIndex].DefaultValue?.Equals(RCaronInsideEnum.NoDefaultValue) ?? true)
                        exps[i] = Expression.Constant(arguments[i-startIndex].DefaultValue);
                    else
                        throw RCaronException.ArgumentsLeftUnassigned(arguments[i-startIndex].Name);
                }
        }

        return new DynamicMetaObject(
            Expression.Call(Expression.Constant(func),
                    typeof(CompiledFunction).GetMethod(nameof(CompiledFunction.Invoke))!,
                    exps == null
                        ? Expression.Constant(null, typeof(object[]))
                        : Expression.NewArrayInit(
                            typeof(object), exps.Select(x => x!.EnsureIsType(typeof(object)))))
                .EnsureIsType(returnType),
            restrictions);
    }
}