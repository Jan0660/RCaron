using System.Buffers;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using RCaron.Binders;
using RCaron.LibrarySourceGenerator;

namespace RCaron.Jit.Binders;

public class RCaronOtherBinder : DynamicMetaObjectBinder
{
    public CompiledContext CompiledContext { get; }
    public string Name { get; }
    public CallInfo CallInfo { get; }
    public object EnsureSameOrigin { get; }

    public RCaronOtherBinder(CompiledContext compiledContext, string name, CallInfo callInfo, object ensureSameOrigin)
    {
        CompiledContext = compiledContext;
        Name = name;
        CallInfo = callInfo;
        EnsureSameOrigin = ensureSameOrigin;
    }

    public override DynamicMetaObject Bind(DynamicMetaObject target, DynamicMetaObject[] args)
    {
        // RCaron function
        var func = CompiledContext.GetFunction(Name);
        if (func != null)
        {
            Expression?[]? exps = null;
            if (func.OriginalFunction.Arguments is not null)
            {
                var arguments = func.OriginalFunction.Arguments;
                if (CallInfo.ArgumentCount > arguments.Length)
                    throw RCaronException.LeftOverPositionalArgument();
                exps = new Expression?[arguments.Length];
                for (var i = 0; i < CallInfo.ArgumentCount - CallInfo.ArgumentNames.Count; i++)
                    exps[i] = args[i].Expression;
                for (var index = 0; index < CallInfo.ArgumentNames.Count; index++)
                {
                    var namedArg = CallInfo.ArgumentNames[index];
                    var found = false;
                    for (var i = 0; i < arguments.Length; i++)
                        if (arguments[i].Name.Equals(namedArg,
                                StringComparison.InvariantCultureIgnoreCase))
                        {
                            exps[i] = args[index + CallInfo.ArgumentCount - CallInfo.ArgumentNames.Count]
                                .Expression;
                            found = true;
                            break;
                        }

                    if (!found)
                        throw RCaronException.NamedArgumentNotFound(namedArg);
                }

                // assign default values
                for (var i = 0; i < exps.Length; i++)
                    if (exps[i] == null)
                    {
                        if (!arguments[i].DefaultValue?.Equals(RCaronInsideEnum.NoDefaultValue) ?? true)
                            exps[i] = Expression.Constant(arguments[i].DefaultValue);
                        else
                            throw RCaronException.ArgumentsLeftUnassigned(arguments[i].Name);
                    }
            }

            return new DynamicMetaObject(
                Expression.Call(Expression.Constant(func),
                        typeof(CompiledFunction).GetMethod(nameof(CompiledFunction.Invoke))!,
                        exps == null
                            ? Expression.Constant(null, typeof(object[]))
                            : Expression.NewArrayInit(
                                typeof(object), exps.Select(x => x!.EnsureIsType(typeof(object))!)))
                    .EnsureIsType(ReturnType),
                BindingRestrictions.GetExpressionRestriction(Expression.Equal(target.Expression,
                    Expression.Constant(EnsureSameOrigin))));
        }

        // module
        if (CompiledContext.FileScope.Modules != null)
        {
            foreach (var overrideModule in CompiledContext.OverrideModules)
            {
                var result = DoModule(overrideModule, target, args);
                if (result != null)
                    return result;
            }

            foreach (var module in CompiledContext.FileScope.Modules)
            {
                var result = DoModule(module, target, args);
                if (result != null)
                    return result;
            }
        }

        throw new RCaronException($"Method of name '{Name}' not found", RCaronExceptionCode.MethodNotFound);
    }

    DynamicMetaObject? DoModule(object module, DynamicMetaObject target,
        DynamicMetaObject[] args)
    {
        var moduleType = module.GetType();
        var methods = moduleType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        for (var index = 0; index < methods.Length; index++)
        {
            var method = methods[index];
            var att = method.GetCustomAttribute<MethodAttribute>();
            if (att == null)
                continue;
            if (!att.Name.Equals(Name, StringComparison.InvariantCultureIgnoreCase)) continue;
            if (method.ReturnType == typeof(void))
                method = moduleType.GetMethod(method.Name + "_ReturnsNoReturnValue")!;
            var parameters = method.GetParameters();
            var exps = new Expression?[parameters.Length];
            exps[0] = parameters[0].ParameterType == typeof(CompiledContext)
                ? CompiledContext.CompiledContextConstant
                : CompiledContext.FakedMotorConstant;
            if (CallInfo.ArgumentCount > parameters.Length - 1)
                throw RCaronException.LeftOverPositionalArgument();
            for (var i = 0; i < CallInfo.ArgumentCount - CallInfo.ArgumentNames.Count; i++)
                exps[i + 1] = args[i].Expression.EnsureIsType(parameters[i + 1].ParameterType);
            for (var namedArgIndex = 0; namedArgIndex < CallInfo.ArgumentNames.Count; namedArgIndex++)
            {
                var namedArg = CallInfo.ArgumentNames[namedArgIndex];
                var found = false;
                for (var i = 1; i < parameters.Length; i++)
                    if (parameters[i].Name.Equals(namedArg,
                            StringComparison.InvariantCultureIgnoreCase))
                    {
                        exps[i] = args[
                                namedArgIndex + CallInfo.ArgumentCount - CallInfo.ArgumentNames.Count]
                            .Expression;
                        found = true;
                        break;
                    }

                if (!found)
                    throw RCaronException.NamedArgumentNotFound(namedArg);
            }

            // assign default values
            for (var i = 1; i < exps.Length; i++)
                if (exps[i] == null)
                {
                    if (parameters[i].HasDefaultValue)
                        exps[i] = Expression.Constant(parameters[i].DefaultValue,
                            parameters[i].ParameterType);
                    else
                        throw RCaronException.ArgumentsLeftUnassigned(parameters[i].Name);
                }

            Expression call = Expression.Call(method.IsStatic ? null : Expression.Constant(module), method,
                exps!).EnsureIsType(ReturnType);

            return new DynamicMetaObject(
                call,
                BindingRestrictions.GetExpressionRestriction(Expression.Equal(target.Expression,
                    Expression.Constant(EnsureSameOrigin))));
        }

        return null;
    }
}