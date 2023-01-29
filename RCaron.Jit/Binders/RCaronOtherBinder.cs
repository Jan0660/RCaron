using System.Buffers;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using RCaron.LibrarySourceGenerator;

namespace RCaron.Jit.Binders;

public class RCaronOtherBinder : DynamicMetaObjectBinder
{
    public CompiledContext CompiledContext { get; }
    public string Name { get; }
    public FunnyArguments Arguments { get; }

    public RCaronOtherBinder(CompiledContext compiledContext, string name, FunnyArguments arguments)
    {
        CompiledContext = compiledContext;
        Name = name;
        Arguments = arguments;
    }

    public override DynamicMetaObject Bind(DynamicMetaObject target, DynamicMetaObject[] args)
    {
        // RCaron function
        foreach (var func in CompiledContext.Functions)
        {
            // todo: call precompile in the Hook
            if (func.Key.Equals(Name, StringComparison.InvariantCultureIgnoreCase))
            {
                Expression?[]? exps = null;
                if (func.Value.OriginalFunction.Arguments is not null)
                {
                    var arguments = func.Value.OriginalFunction.Arguments;
                    // var exps = new Expression?[arguments.Length];
                    if (Arguments.Positional.Length > arguments.Length)
                        throw RCaronException.LeftOverPositionalArgument();
                    exps = new Expression?[arguments.Length];
                    Array.Copy(Arguments.Positional, 0, exps, 0, Arguments.Positional.Length);
                    foreach (var namedArg in Arguments.Named)
                    {
                        var found = false;
                        for (var i = 0; i < arguments.Length; i++)
                            if (arguments[i].Name.Equals(namedArg.Key,
                                    StringComparison.InvariantCultureIgnoreCase))
                            {
                                exps[i] = namedArg.Value;
                                found = true;
                                break;
                            }

                        if (!found)
                            throw RCaronException.NamedArgumentNotFound(namedArg.Key);
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
                    Expression.Call(Expression.Constant(func.Value), func.Value.GetType().GetMethod("Invoke")!,
                        exps == null ? Expression.Constant(null, typeof(object[])) : Expression.NewArrayInit(
                    typeof(object), exps.Select(x => x!.EnsureIsType(typeof(object))!))).EnsureIsType(ReturnType),
                BindingRestrictions.GetExpressionRestriction(Expression.Equal(target.Expression,
                    Expression.Constant(Arguments))));
            }
        }

        // module
        if (CompiledContext.FileScope.Modules != null)
        {
            foreach (var module in CompiledContext.FileScope.Modules)
            {
                var methods = module.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var att = method.GetCustomAttribute<MethodAttribute>();
                    if (att == null)
                        continue;
                    if (att.Name.Equals(Name, StringComparison.InvariantCultureIgnoreCase))
                    {
                        var parameters = method.GetParameters();
                        var exps = new Expression?[parameters.Length];
                        exps[0] = CompiledContext.FakedMotorConstant;
                        if (Arguments.Positional.Length > parameters.Length - 1)
                            throw RCaronException.LeftOverPositionalArgument();
                        Array.Copy(Arguments.Positional, 0, exps, 1, Arguments.Positional.Length);
                        foreach (var namedArg in Arguments.Named)
                        {
                            var found = false;
                            for (var i = 1; i < parameters.Length; i++)
                                if (parameters[i].Name.Equals(namedArg.Key,
                                        StringComparison.InvariantCultureIgnoreCase))
                                {
                                    exps[i] = namedArg.Value;
                                    found = true;
                                    break;
                                }

                            if (!found)
                                throw RCaronException.NamedArgumentNotFound(namedArg.Key);
                        }

                        // assign default values
                        for (var i = 1; i < exps.Length; i++)
                            if (exps[i] == null)
                            {
                                if (parameters[i].HasDefaultValue)
                                    exps[i] = Expression.Constant(parameters[i].DefaultValue);
                                else
                                    throw RCaronException.ArgumentsLeftUnassigned(parameters[i].Name);
                            }

                        return new DynamicMetaObject(
                            Expression.Call(method.IsStatic ? null : Expression.Constant(module), method, exps!)
                                .EnsureIsType(ReturnType),
                            BindingRestrictions.GetExpressionRestriction(Expression.Equal(target.Expression,
                                Expression.Constant(Arguments))));
                    }
                }
            }
        }

        throw new RCaronException($"Method of name '{Name}' not found", RCaronExceptionCode.MethodNotFound);
    }

    // public record Arguments(object[] Positional, string[] NamedNames, object[] NamedValues);
    public record FunnyArguments(Expression[] Positional, Dictionary<string, Expression> Named);
}