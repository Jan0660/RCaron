using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.CSharp.RuntimeBinder;
using RCaron.LibrarySourceGenerator;
using Binder = Microsoft.CSharp.RuntimeBinder.Binder;

namespace RCaron.FunLibrary;

[Module("Fun")]
public partial class FunModule : IRCaronModule
{
    [Method("DelegateTest")]
    public void TestStuff(Motor motor, CodeBlockToken codeBlockToken)
    {
        Console.WriteLine(codeBlockToken.Lines.Count);
        var del = GetDelegate(codeBlockToken, motor, typeof(Action<string>));
        var real = (Action<string>)del.DynamicInvoke();
        // var type = Type.GetType("Balls");
        // var e = type.GetEvent("event");
        // var g = e.EventHandlerType;
        // var z = new Func<int>(() => 1);

        real("balls");
    }

    [Method("Register_ObjectEvent")]
    public void RegisterObjectEvent(Motor motor, object obj, string eventName, CodeBlockToken codeBlockToken)
    {
        var type = obj.GetType();
        var ev = type.GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        var del = GetDelegate(codeBlockToken, motor, ev.EventHandlerType);
        var finDel = (Delegate)del.DynamicInvoke();
        ev.AddEventHandler(obj, finDel);
    }

    [Method("Start-MotorPoolTask")]
    public void StartMotorPoolTask(Motor motor, Motor parent, CodeBlockToken codeBlockToken, object[]? args = null)
    {
        using var m = MotorPool.GetAndPrepare(parent);
        var scope = new LocalScope();
        scope.Variables ??= new();
        scope.SetVariable("args", args);
        m.Motor.BlockStack.Push(new(false, true, scope));
        Task.Run((() => m.Motor.RunCodeBlock(codeBlockToken)));
    }

    public class CodeBlockWrap
    {
        public Motor Motor { get; }
        public CodeBlockToken CodeBlockToken { get; }

        public CodeBlockWrap(Motor motor, CodeBlockToken codeBlockToken)
        {
            Motor = motor;
            CodeBlockToken = codeBlockToken;
        }

        [UsedImplicitly]
        internal object? InvokeAsDelegateHelper(object[] args)
        {
            using var m = MotorPool.GetAndPrepare(Motor);
            var scope = new LocalScope();
            scope.SetVariable("eventArgs", args);
            m.Motor.BlockStack.Push(new(false, true, scope));
            return m.Motor.RunCodeBlock(CodeBlockToken);
        }
    }


    #region Delegates

    // taken over from PowerShell

    private static readonly ConcurrentDictionary<CodeBlockToken, ConcurrentDictionary<Type, Delegate>> s_delegateTable =
        new();

    internal Delegate GetDelegate(CodeBlockToken codeBlock, Motor motor, Type delegateType)
    {
        ConcurrentDictionary<Type, Delegate> d;
        if (!s_delegateTable.TryGetValue(codeBlock, out d))
        {
            d = new();
            s_delegateTable[codeBlock] = d;
        }

        Delegate ret;
        ret = d.GetOrAdd(delegateType, () => CreateDelegate(codeBlock, motor, delegateType));
        return ret;
    }
    // => s_delegateTable.GetOrCreateValue(this).GetOrAdd(delegateType, CreateDelegate);

    /// <summary>
    /// Get the delegate method as a call back.
    /// </summary>
    internal Delegate CreateDelegate(CodeBlockToken codeBlock, Motor motor, Type delegateType)
    {
        MethodInfo invokeMethod = delegateType.GetMethod("Invoke");
        ParameterInfo[] parameters = invokeMethod.GetParameters();
        if (invokeMethod.ContainsGenericParameters)
        {
            throw new("no code block delegate generic type");
            // throw new ScriptBlockToPowerShellNotSupportedException(
            //     "CantConvertScriptBlockToOpenGenericType",
            //     null,
            //     "AutomationExceptions",
            //     "CantConvertScriptBlockToOpenGenericType",
            //     delegateType);
        }

        // var codeBlockArg = Expression.Parameter(codeBlock.GetType());
        // var motorArg = Expression.Parameter(motor.GetType());
        var parameterExprs = new List<ParameterExpression>();
        foreach (var parameter in parameters)
        {
            parameterExprs.Add(Expression.Parameter(parameter.ParameterType));
        }

        bool returnsSomething = !invokeMethod.ReturnType.Equals(typeof(void));

        // Expression dollarUnderExpr;
        // Expression dollarThisExpr;
        // if (parameters.Length == 2 && !returnsSomething)
        // {
        //     // V1 was designed for System.EventHandler and not much else.
        //     // The first arg (sender) was bound to $this, the second (e or EventArgs) was bound to $_.
        //     // We do this for backwards compatibility, but we also bind the parameters (or $args) for
        //     // consistency w/ delegates that take more or fewer parameters.
        //     dollarUnderExpr = parameterExprs[1].Cast(typeof(object));
        //     dollarThisExpr = parameterExprs[0].Cast(typeof(object));
        // }
        // else
        // {
        //     dollarUnderExpr = ExpressionCache.AutomationNullConstant;
        //     dollarThisExpr = ExpressionCache.AutomationNullConstant;
        // }

        var h = parameterExprs.Select(static p => Cast(p, typeof(object)));

        Expression call = Expression.Call(
            Expression.Constant(new CodeBlockWrap(motor, codeBlock)),
            CodeBlockWrap_InvokeAsDelegateHelper,
            // (object)
            Expression.NewArrayInit(typeof(object), h));
        if (returnsSomething)
        {
            var variable = Expression.Variable(typeof(object), "returnValue");
            var block = Expression.Block(
                new[] { variable },
                Expression.Assign(variable, call),
                Expression.Convert(variable, invokeMethod.ReturnType));
            call = block;
            // call = DynamicExpression.Dynamic(
            //     // PSConvertBinder.Get(invokeMethod.ReturnType),
            //     Binder.Invoke(CSharpBinderFlags.None, null,
            //         new CSharpArgumentInfo[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null), }
            //     ),
            //     invokeMethod.ReturnType,
            //     call);
        }

        Console.WriteLine(delegateType.ToString());
        return Expression.Lambda(delegateType, call, parameterExprs).Compile();
    }


    private readonly static MethodInfo Convert_ChangeType = typeof(Convert).GetMethod("Convert");

    internal static Expression Cast(Expression expr, Type type)
    {
        if (expr.Type == type)
        {
            return expr;
        }

        if ((
                // todo(current)
                // expr.Type.IsFloating() || 
                expr.Type == typeof(decimal)) && type.IsPrimitive)
        {
            // Convert correctly handles most "primitive" conversions for PowerShell,
            // but it does not correctly handle floating point.
            expr = Expression.Call(
                Convert_ChangeType,
                Expression.Convert(expr, typeof(object)),
                Expression.Constant(type, typeof(Type)));
        }

        return Expression.Convert(expr, type);
    }

    private MethodInfo CodeBlockWrap_InvokeAsDelegateHelper =
        typeof(CodeBlockWrap).GetMethod("InvokeAsDelegateHelper", BindingFlags.NonPublic | BindingFlags.Instance);

    #endregion
}