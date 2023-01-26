using System.Collections;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using Dynamitey.DynamicObjects;

namespace RCaron.Jit.Binders;

public class RCaronGetMemberBinder : GetMemberBinder
{
    public FileScope FileScope { get; }
    public RCaronGetMemberBinder(string name, bool ignoreCase, FileScope fileScope) : base(name, ignoreCase)
    {
        FileScope = fileScope;
    }

    public override DynamicMetaObject FallbackGetMember(DynamicMetaObject target, DynamicMetaObject? errorSuggestion)
    {
        if (target.Expression.Type == typeof(IDynamicMetaObjectProvider))
        {
            return new DynamicMetaObject(
                Expression.Call(target.Expression, "GetMetaObject", Array.Empty<Type>(), Expression.Constant(null)),
                BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType));
        }

        var property = target.RuntimeType.GetProperty(Name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property != null)
        {
            return new DynamicMetaObject(Expression.Property(target.Expression.EnsureIsType(target.RuntimeType), property).EnsureIsType(ReturnType),
                GetRestrictions(target));
        }

        var field = target.RuntimeType.GetField(Name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (field != null)
        {
            return new DynamicMetaObject(Expression.Field(target.Expression.EnsureIsType(target.RuntimeType), field).EnsureIsType(ReturnType),
                GetRestrictions(target));
        }

        if (target.RuntimeType.IsAssignableTo(typeof(IDictionary)))
        {
            return new DynamicMetaObject(
                Expression.Call(
                    Expression.Convert(target.Expression, typeof(IDictionary)),
                    typeof(IDictionary).GetMethod("get_Item")!,
                    Expression.Constant(Name)),
                GetRestrictions(target));
        }

        throw new RCaronException($"Unable to find property or field {Name} on type {target.RuntimeType.Name}", RCaronExceptionCode.CannotResolveInDotThing);
    }

    private BindingRestrictions GetRestrictions(DynamicMetaObject target)
        => BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType);
}