using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;

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
            if (needsNumericConversion)
                throw new NotImplementedException("Numeric conversion not implemented yet");
            if (isExtensionMethod)
                throw new NotImplementedException("Extension methods not implemented yet");

            return method is ConstructorInfo ? throw new() :
                method is MethodInfo methodInfo ? new DynamicMetaObject(
                    Expression.Call(target.Expression.EnsureIsType(target.RuntimeType), methodInfo,
                        args.Select(x => x.Expression).ToArray()),
                    BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType)) : throw new();
        }

        if (FileScope.UsedNamespacesForExtensionMethods == null)
            return null;

        foreach (var @namespace in FileScope.UsedNamespacesForExtensionMethods)
        {
        }

        return null;
    }
}