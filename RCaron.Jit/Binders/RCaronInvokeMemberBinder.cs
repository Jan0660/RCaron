using System.Diagnostics;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using DotNext.Reflection;
using Dynamitey;

namespace RCaron.Jit.Binders;

public class RCaronInvokeMemberBinder : InvokeMemberBinder
{
    public FileScope FileScope { get; }

    public RCaronInvokeMemberBinder(string name, bool ignoreCase, CallInfo callInfo, FileScope fileScope) : base(name,
        ignoreCase, callInfo)
    {
        FileScope = fileScope;
    }

    public override DynamicMetaObject FallbackInvoke(DynamicMetaObject target, DynamicMetaObject[] args,
        DynamicMetaObject? errorSuggestion)
    {
        return _do(target, args, errorSuggestion);
    }

    public override DynamicMetaObject FallbackInvokeMember(DynamicMetaObject target, DynamicMetaObject[] args,
        DynamicMetaObject? errorSuggestion)
    {
        return _do(target, args, errorSuggestion);
    }

    private DynamicMetaObject _do(DynamicMetaObject target, DynamicMetaObject[] args,
        DynamicMetaObject? errorSuggestion)
    {
        if (target.Expression.Type == typeof(IDynamicMetaObjectProvider))
        {
            // todo: could somehow do extension methods with this?
            return new DynamicMetaObject(
                Expression.Call(target.Expression, "GetMetaObject", Array.Empty<Type>(), Expression.Constant(null)),
                BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType));
        }

        {
            var (method, needsNumericConversion, isExtensionMethod) = MethodResolver.Resolve(Name, target.LimitType,
                FileScope, target.Value, args.Select(x => x.Value).ToArray());
            var argsExpressionEnumerable = args.Select(x => x.Expression);
            var argsExpressionArray = isExtensionMethod
                ? argsExpressionEnumerable.Prepend(target.Expression).ToArray()
                : argsExpressionEnumerable.ToArray();
            if (needsNumericConversion)
                throw new NotImplementedException("Numeric conversion not implemented yet");
            if (method.IsGenericMethod && !method.IsConstructedGenericMethod)
            {
                Expression GetExpression()
                {
                    // todo(perf): probably not the fastest
                    var t = method.DeclaringType!;

                    var endTarget = method.IsStatic ? Expression.Constant(t)/*Expression.Constant(InvokeContext.CreateStatic(t))*/ : target.Expression;

                    var mi = (MethodInfo)method;
                    var c = CacheableInvocation.CreateCall(
                        mi.ReturnType == typeof(void) ? InvocationKind.InvokeMemberAction : InvocationKind.InvokeMember,
                        mi.Name, new CallInfo(argsExpressionArray.Length/* todo: named args */), method.IsStatic ? InvokeContext.CreateStatic(t) : null);
                    
                    return Expression.Call(Expression.Constant(c), "Invoke", Array.Empty<Type>(), endTarget,
                        Expression.NewArrayInit(typeof(object), argsExpressionArray));
                    //
                    // // static class
                    // if (t.IsSealed && t.IsAbstract)
                    // {
                    //     var staticContext = InvokeContext.CreateStatic;
                    //     if (method is MethodInfo mi && mi.ReturnType == typeof(void))
                    //     {
                    //         Dynamic.InvokeMemberAction(target, method.Name, args);
                    //         return RCaronInsideEnum.NoReturnValue;
                    //     }
                    //
                    //     return Dynamic.InvokeMember(staticContext(t), method.Name, args);
                    // }
                    // else
                    // {
                    //     if (method is MethodInfo mi && mi.ReturnType == typeof(void))
                    //     {
                    //         Dynamic.InvokeMemberAction(target, method.Name, args);
                    //         return RCaronInsideEnum.NoReturnValue;
                    //     }
                    //
                    //     return Dynamic.InvokeMember(target, method.Name, args);
                    // }
                }

                return new DynamicMetaObject(GetExpression(),
                    BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType));
                // return new DynamicMetaObject(Expression.Call(null, methodInfo2,
                //     argsExpressionArray),
                // BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType));
            }

            return method is ConstructorInfo ? throw new() :
                method is MethodInfo methodInfo ? new DynamicMetaObject(
                    Expression.Call(target.Expression.EnsureIsType(target.RuntimeType), methodInfo,
                        args.Select(x => x.Expression).ToArray()).EnsureIsType(ReturnType),
                    BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType)) : throw new();
        }
    }
}