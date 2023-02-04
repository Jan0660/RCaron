﻿using System.Diagnostics;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using DotNext.Reflection;
using Dynamitey;
using RCaron.Classes;

namespace RCaron.Jit.Binders;

public class RCaronInvokeMemberBinder : InvokeMemberBinder
{
    public CompiledContext Context { get; }
    public FileScope FileScope => Context.FileScope;

    public RCaronInvokeMemberBinder(string name, bool ignoreCase, CallInfo callInfo, CompiledContext context) : base(
        name,
        ignoreCase, callInfo)
    {
        Context = context;
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
        if (target.RuntimeType == typeof(ClassDefinition) &&
            Name.Equals("new", StringComparison.InvariantCultureIgnoreCase))
        {
            var classDefinition = (ClassDefinition)target.Value!;
            var expressions = new List<Expression>(classDefinition.PropertyNames?.Length + 2 ?? 2);
            var classVar = Expression.Variable(typeof(ClassInstance), "classInstance");
            expressions.Add(Expression.Assign(classVar,
                Expression.New(typeof(ClassInstance).GetConstructor(new[] { typeof(ClassDefinition) })!,
                    Expression.Constant(classDefinition))));
            if (classDefinition.PropertyInitializers != null)
            {
                var compiledClass = Context.GetClass(classDefinition);
                Debug.Assert(compiledClass.PropertyInitializers != null);
                for (var j = 0; j < compiledClass.PropertyInitializers.Length; j++)
                {
                    var bruh = nameof(ClassInstance.PropertyValues);
                    if (compiledClass.PropertyInitializers[j] != null)
                        expressions.Add(Expression.Assign(
                            Expression.ArrayAccess(Expression.Property(classVar, nameof(ClassInstance.PropertyValues)),
                                Expression.Constant(j)),
                            compiledClass.PropertyInitializers[j].EnsureIsType(typeof(object))));
                }
            }
            expressions.Add(classVar);

            var block = Expression.Block(typeof(ClassInstance), new[] { classVar }, expressions);
            return new DynamicMetaObject(block, BindingRestrictions.Empty);
        }

        if (target.Expression.Type == typeof(IDynamicMetaObjectProvider))
        {
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

                    var endTarget = method.IsStatic
                        ? Expression.Constant(t) /*Expression.Constant(InvokeContext.CreateStatic(t))*/
                        : target.Expression;

                    var mi = (MethodInfo)method;
                    var c = CacheableInvocation.CreateCall(
                        mi.ReturnType == typeof(void) ? InvocationKind.InvokeMemberAction : InvocationKind.InvokeMember,
                        mi.Name, new CallInfo(argsExpressionArray.Length /* todo: named args */),
                        method.IsStatic ? InvokeContext.CreateStatic(t) : null);

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

                return new DynamicMetaObject(GetExpression().EnsureIsType(ReturnType),
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