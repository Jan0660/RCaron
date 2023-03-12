using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using JetBrains.Annotations;
using RCaron.LibrarySourceGenerator;

namespace RCaron.FunLibrary;

[Module("Fun")]
public partial class FunModule : IRCaronModule
{
    [Method("DelegateTest")]
    public void TestStuff(Motor motor, CodeBlockToken codeBlockToken)
    {
        Console.WriteLine(codeBlockToken.Lines.Count);
        var del = GetDelegate(codeBlockToken, motor, typeof(Action<string>));
        var real = (Action<string>)del.DynamicInvoke()!;
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
        if (ev is null)
            throw new($"Event '{eventName}' not found on type '{type}'");
        if(ev.EventHandlerType is null)
            throw new($"Event '{eventName}' on type '{type}' has null event handler type");
        var del = GetDelegate(codeBlockToken, motor, ev.EventHandlerType);
        var finDel = (Delegate)del.DynamicInvoke()!;
        ev.AddEventHandler(obj, finDel);
    }

    [Method("Start-MotorPoolTask")]
    public void StartMotorPoolTask(Motor motor, Motor parent, CodeBlockToken codeBlockToken, object[]? args = null)
    {
        var m = MotorPool.GetAndPrepare(parent);
        var scope = new LocalScope();
        scope.Variables ??= new();
        scope.SetVariable("args", args);
        m.Motor.BlockStack.Push(new(false, true, scope, motor.MainFileScope));
        Task.Run((() =>
        {
            m.Motor.RunCodeBlock(codeBlockToken);
            m.Dispose();
        }));
    }

    [Method("Convert-Array")]
    public object ConvertArray(Motor _, object[] array, RCaronType type)
    {
        if (array is not { } arr)
            throw new();
        var r = Array.CreateInstance(type.Type, arr.Length);
        for (var i = 0; i < arr.Length; i++)
        {
            r.SetValue(arr[i], i);
        }

        return r;
    }

    [Method("Get-StackTraceString")]
    public static string GetStackTraceString(Motor motor)
    {
        // this is going to be terrible
        var sb = new StringBuilder();

        FileScope GetFileScopeBefore(int i)
            => i == 0 ? motor.MainFileScope : motor.BlockStack.At(i - 1).FileScope;

        int GetPos(Line line)
            => line switch
            {
                TokenLine tl => tl.Tokens[0].Position.Start,
                SingleTokenLine stl => stl.Token.Position.Start,
                CodeBlockLine cbl => cbl.Token.Position.Start,
                _ => throw new ArgumentOutOfRangeException()
            };

        string GetName(FileScope fileScope, int pos)
        {
            if (fileScope.Functions != null)
                foreach (var function in fileScope.Functions)
                {
                    var l = function.Value.CodeBlock.Lines;
                    if (GetPos(l[0]) <= pos && GetPos(l[^1]) >= pos)
                        return function.Key;
                }

            if (fileScope.ClassDefinitions != null)
                foreach (var classDefinition in fileScope.ClassDefinitions)
                {
                    if (classDefinition.Functions == null)
                        continue;
                    foreach (var function in classDefinition.Functions)
                    {
                        var l = function.Value.CodeBlock.Lines;
                        if (GetPos(l[0]) <= pos && GetPos(l[^1]) >= pos)
                            return $"{classDefinition.Name}.{function.Key}";
                    }
                }

            return "???";
        }

        void Do(Line line, FileScope fileScopeBefore)
        {
            var pos = GetPos(line);
            var lineNumber = motor.GetLineNumber(fileScopeBefore, pos);
            var name = GetName(fileScopeBefore, pos);
            sb.AppendLine(
                $"at {name} in {fileScopeBefore.FileName ?? "null FileName"}:{lineNumber}");
        }

        Do(motor.Lines[motor.CurrentLineIndex], GetFileScopeBefore(motor.BlockStack.Count - 1));

        for (int i = motor.BlockStack.Count - 1; i >= 0; i--)
        {
            var s = motor.BlockStack.At(i);
            if (s.LineForTrace == null)
                continue;
            var line = s.LineForTrace;
            var fileScope = GetFileScopeBefore(i);
            Do(line, fileScope);
        }

        return sb.ToString();
    }

    [Method("Try-StackTrace")]
    public void TryStackTrace(Motor motor, CodeBlockToken codeBlockToken)
    {
        // var h = new StackTrace().GetFrames();
        var previousCount = motor.BlockStack.Count;
        try
        {
            motor.BlockStack.Push(new(false, true, null, motor.MainFileScope));
            motor.RunCodeBlock(codeBlockToken);
        }
        catch (Exception e)
        {
            PrintException(e, motor);
            while (motor.BlockStack.Count != previousCount)
                motor.BlockStack.Pop();
        }
    }

    public static void PrintException(Exception e, Motor motor)
    {
        Console.WriteLine("RCaron Stack Trace:");
        Console.Write(GetStackTraceString(motor));
        Console.WriteLine(e.ToString());
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
            m.Motor.BlockStack.Push(new(false, true, scope, Motor.MainFileScope));
            return m.Motor.RunCodeBlock(CodeBlockToken);
        }
    }


    #region Delegates

    // taken over from PowerShell

    private static readonly ConcurrentDictionary<CodeBlockToken, ConcurrentDictionary<Type, Delegate>> SDelegateTable =
        new();

    internal Delegate GetDelegate(CodeBlockToken codeBlock, Motor motor, Type delegateType)
    {
        ConcurrentDictionary<Type, Delegate>? d;
        if (!SDelegateTable.TryGetValue(codeBlock, out d))
        {
            d = new();
            SDelegateTable[codeBlock] = d;
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
        MethodInfo invokeMethod = delegateType.GetMethod("Invoke")!;
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
            _codeBlockWrapInvokeAsDelegateHelper,
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


    private static readonly MethodInfo ConvertChangeType = typeof(Convert).GetMethod("Convert")!;

    internal static Expression Cast(Expression expr, Type type)
    {
        if (expr.Type == type)
        {
            return expr;
        }

        if (expr.Type == typeof(decimal) && type.IsPrimitive)
        {
            // Convert correctly handles most "primitive" conversions for PowerShell,
            // but it does not correctly handle floating point.
            expr = Expression.Call(
                ConvertChangeType,
                Expression.Convert(expr, typeof(object)),
                Expression.Constant(type, typeof(Type)));
        }

        return Expression.Convert(expr, type);
    }

    private readonly MethodInfo _codeBlockWrapInvokeAsDelegateHelper =
        typeof(CodeBlockWrap).GetMethod(nameof(CodeBlockWrap.InvokeAsDelegateHelper), BindingFlags.NonPublic | BindingFlags.Instance)!;

    #endregion
}