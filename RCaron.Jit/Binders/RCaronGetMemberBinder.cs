using System.Collections;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using Dynamitey.DynamicObjects;

namespace RCaron.Jit.Binders;

public class RCaronGetMemberBinder : GetMemberBinder
{
    public CompiledContext Context { get; }
    public FileScope FileScope => Context.FileScope;
    public RCaronGetMemberBinder(string name, bool ignoreCase, CompiledContext context) : base(name, ignoreCase)
    {
        Context = context;
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

        if (FileScope.PropertyAccessors != null && target.HasValue)
        {
            var motor = (Motor)Context.FakedMotorConstant.Value!;
            var value = target.Value;
            var type = target.RuntimeType;
            foreach (var propertyAccessor in FileScope.PropertyAccessors)
            {
                if (propertyAccessor.Do(motor, Name, ref value, ref type))
                {
                    return new DynamicMetaObject(Expression.Constant(value), Util.GetValidOnceRestriction());
                }
            }
        }

        throw new RCaronException($"Unable to find property or field {Name} on type {target.RuntimeType.Name}", RCaronExceptionCode.CannotResolveInDotThing);
    }

    private BindingRestrictions GetRestrictions(DynamicMetaObject target)
        => BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType);
}