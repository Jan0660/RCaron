using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
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

    public class CodeBlockWrap
    {
        public Motor Motor { get; }
        public CodeBlockToken CodeBlockToken { get; }
        public CodeBlockWrap(Motor motor, CodeBlockToken codeBlockToken)
        {
            Motor = motor;
            CodeBlockToken = codeBlockToken;
        }
        internal object InvokeAsDelegateHelper(object[] args)
        {
            Console.WriteLine("wooo");
            return "h";
            // // Retrieve context and current runspace to ensure that we throw exception, if this is non-default runspace.
            // ExecutionContext context = GetContextFromTLS();
            // RunspaceBase runspace = (RunspaceBase)context.CurrentRunspace;
            //
            // List<object> rawResult = new List<object>();
            // Pipe outputPipe = new Pipe(rawResult);
            // InvokeWithPipe(
            //     useLocalScope: true,
            //     errorHandlingBehavior: ErrorHandlingBehavior.WriteToCurrentErrorPipe,
            //     dollarUnder: dollarUnder,
            //     input: null,
            //     scriptThis: dollarThis,
            //     outputPipe: outputPipe,
            //     invocationInfo: null,
            //     args: args);
            // return GetRawResult(rawResult, wrapToPSObject: false);
        }
    }
    
    
        #region Delegates
        // taken over from PowerShell

        private static readonly ConcurrentDictionary<CodeBlockToken, ConcurrentDictionary<Type, Delegate>> s_delegateTable = new();

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
                call = DynamicExpression.Dynamic(
                    // PSConvertBinder.Get(invokeMethod.ReturnType),
                    Binder.Invoke(CSharpBinderFlags.None, invokeMethod.ReturnType, new CSharpArgumentInfo[]{CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null), }
                    ),
                    invokeMethod.ReturnType,
                    call);
            }

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
        
        private MethodInfo CodeBlockWrap_InvokeAsDelegateHelper= typeof(CodeBlockWrap).GetMethod("InvokeAsDelegateHelper", BindingFlags.NonPublic | BindingFlags.Instance);

        #endregion
}