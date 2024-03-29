﻿using System.Diagnostics;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using Dynamitey;
using RCaron.Binders;
using RCaron.Classes;

namespace RCaron.Jit.Binders;

public class RCaronInvokeMemberBinder : InvokeMemberBinder
{
    public CompiledContext Context { get; }

    public RCaronInvokeMemberBinder(string name, bool ignoreCase, CallInfo callInfo, CompiledContext context) : base(
        name,
        ignoreCase, callInfo)
    {
        Context = context;
    }

    public override DynamicMetaObject FallbackInvoke(DynamicMetaObject target, DynamicMetaObject[] args,
        DynamicMetaObject? errorSuggestion)
    {
        return _do(target, args);
    }

    public override DynamicMetaObject FallbackInvokeMember(DynamicMetaObject target, DynamicMetaObject[] args,
        DynamicMetaObject? errorSuggestion)
    {
        return _do(target, args);
    }

    private DynamicMetaObject _do(DynamicMetaObject target, DynamicMetaObject[] args)
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
                Debug.Assert(compiledClass != null);
                Debug.Assert(compiledClass.PropertyInitializers != null);
                for (var j = 0; j < compiledClass.PropertyInitializers.Length; j++)
                {
                    if (compiledClass.PropertyInitializers[j] != null)
                        expressions.Add(Expression.Assign(
                            Expression.ArrayAccess(Expression.Property(classVar, nameof(ClassInstance.PropertyValues)),
                                Expression.Constant(j)),
                            compiledClass.PropertyInitializers[j]!.EnsureIsType(typeof(object))));
                }
            }

            expressions.Add(classVar);

            var block = Expression.Block(typeof(ClassInstance), new[] { classVar }, expressions);
            return new DynamicMetaObject(block,
                BindingRestrictions.GetExpressionRestriction(Expression.Equal(target.Expression,
                    Expression.Constant(classDefinition))));
        }

        if (target.RuntimeType == typeof(ClassDefinition))
        {
            var classDefinition = (ClassDefinition)target.Value!;
            var compiledClass = Context.GetClass(classDefinition);
            var staticFunction = compiledClass?.StaticFunctions?[Name];
            if (staticFunction == null)
                throw RCaronException.ClassStaticFunctionNotFound(Name);
            return Shared.DoFunction(staticFunction, target, args, Name, CallInfo, typeof(object),
                BindingRestrictions.GetExpressionRestriction(Expression.Equal(target.Expression,
                    Expression.Constant(classDefinition))));
        }

        if (target.RuntimeType == typeof(ClassInstance))
        {
            if (Name.Equals("gettype", StringComparison.InvariantCultureIgnoreCase))
                return new DynamicMetaObject(Expression.Constant(typeof(ClassInstance)),
                    BindingRestrictions.GetTypeRestriction(target.Expression, typeof(ClassInstance)));
            var classInstance = (ClassInstance)target.Value!;
            if (classInstance.Definition.ToStringOverride == null &&
                Name.Equals("toString", StringComparison.InvariantCultureIgnoreCase))
                return new DynamicMetaObject(Expression.Call(target.Expression, "ToString", Array.Empty<Type>()),
                    GetClassInstanceDefinitionRestrictions(target, classInstance));
            var compiledClass = Context.GetClass(classInstance.Definition);
            var compiledFunction = compiledClass?.Functions?[Name];
            if (compiledFunction == null)
                throw RCaronException.ClassFunctionNotFound(Name);
            return Shared.DoFunction(compiledFunction, target, args, Name, CallInfo, typeof(object),
                GetClassInstanceDefinitionRestrictions(target, classInstance));
        }

        if (target.Expression.Type == typeof(IDynamicMetaObjectProvider) && target.LimitType != typeof(RCaronType))
        {
            return new DynamicMetaObject(
                Expression.Call(target.Expression, "GetMetaObject", Array.Empty<Type>(), Expression.Constant(null)),
                BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType));
        }

        {
            if (target.Value is RCaronType rCaronType)
            {
                if (Name.Equals("gettype", StringComparison.InvariantCultureIgnoreCase))
                    return new DynamicMetaObject(Expression.Constant(typeof(RCaronType)),
                        BinderUtil.SameTypeRCaronTypeRestrictions(target, rCaronType));
                if (Name.Equals("toString", StringComparison.InvariantCultureIgnoreCase))
                    return new DynamicMetaObject(Expression.Constant(rCaronType.ToString()),
                        BinderUtil.SameTypeRCaronTypeRestrictions(target, rCaronType));
            }
            var type = target.LimitType == typeof(RCaronType) ? ((RCaronType)target.Value!).Type : target.LimitType;
            var (method, needsNumericConversion, isExtensionMethod) = MethodResolver.Resolve(Name, type,
                Context.FileScope, target.Value, args.Select(x => x.Value).ToArray());
            var argsExpressionEnumerable = args.Select(x => x.Expression);
            var argsExpressionArray = isExtensionMethod
                ? argsExpressionEnumerable.Prepend(target.Expression).ToArray()
                : argsExpressionEnumerable.ToArray();
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
                }

                return new DynamicMetaObject(GetExpression().EnsureIsType(ReturnType),
                    BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType));
            }

            var finalArgs = args.Select(x => x.Expression).ToArray();
            if (needsNumericConversion)
            {
                var startIndex = isExtensionMethod ? 1 : 0;
                var methodParameters = method.GetParameters();
                for (var i = startIndex; i < finalArgs.Length; i++)
                {
                    var arg = finalArgs[i];
                    if (!arg.Type.IsAssignableTo(methodParameters[i - startIndex].ParameterType))
                        finalArgs[i] = Expression.Convert(arg, methodParameters[i - startIndex].ParameterType);
                }
            }

            return method is ConstructorInfo constructorInfo ? new DynamicMetaObject(
                    Expression.New(constructorInfo, finalArgs).EnsureIsType(ReturnType),
                    BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType)) :
                method is MethodInfo methodInfo ? new DynamicMetaObject(
                    Expression.Call(methodInfo.IsStatic ? null : target.Expression.EnsureIsType(target.LimitType),
                        methodInfo,
                        finalArgs).EnsureIsType(ReturnType),
                    BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType)) : throw new();
        }
    }

    public static BindingRestrictions GetClassInstanceDefinitionRestrictions(DynamicMetaObject target,
        ClassInstance classInstance)
        => BindingRestrictions.GetExpressionRestriction(Expression.Equal(
            Expression.Property(target.Expression.EnsureIsType(typeof(ClassInstance)), "Definition"),
            Expression.Constant(classInstance.Definition)));
}