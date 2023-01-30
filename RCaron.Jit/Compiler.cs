using System.Collections;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using Dynamitey;
using JetBrains.Annotations;
using Microsoft.CSharp.RuntimeBinder;
using RCaron.Jit.Binders;
using Binder = Microsoft.CSharp.RuntimeBinder.Binder;

namespace RCaron.Jit;

public class Compiler
{
    private static readonly object NoReturnValue = RCaronInsideEnum.NoReturnValue;
    private static readonly object ReturnWithoutValue = RCaronInsideEnum.ReturnWithoutValue;

    public static BlockExpression CompileToBlock(
        RCaronRunnerContext parsed, Motor? fakedMotor = null)
    {
        var contextStack = new NiceStack<Context>();
        var fakedMotorConstant = Expression.Constant(fakedMotor, typeof(Motor));
        // var fakedMotor = isMain ? Expression.Parameter(typeof(Motor), "\u0159motor") : null;
        System.Lazy<MethodInfo> consoleWriteMethod =
            new(() => typeof(Console).GetMethod(nameof(Console.Write), new[] { typeof(object) })!);
        System.Lazy<MethodInfo> consoleWriteLineMethod =
            new(() => typeof(Console).GetMethod(nameof(Console.WriteLine), Array.Empty<Type>())!);
        System.Lazy<MethodInfo> consoleWriteLineWithArgMethod =
            new(() => typeof(Console).GetMethod(nameof(Console.WriteLine), new[] { typeof(object) })!);
        var functions = new Dictionary<string, CompiledFunction>(StringComparer.InvariantCultureIgnoreCase);

        ParameterExpression? GetVariableNullable(string name, bool mustBeAtCurrent = false)
        {
            if (mustBeAtCurrent)
            {
                if (contextStack.Peek().Variables?.TryGetValue(name, out var variable) ?? false)
                {
                    return variable;
                }

                return null;
            }

            for (var i = contextStack.Count - 1; i >= 0; i--)
            {
                var context = contextStack.At(i);
                if (context.Variables?.TryGetValue(name, out var variable) ?? false)
                {
                    return variable;
                }
                
                if (context is ContextWithSpecialVariables spec && spec.SpecialVariables.TryGetValue(name, out var specialVariable))
                {
                    return specialVariable;
                }

                if (context.ReturnWorthy && contextStack.At(0) is FunctionContext
                    {
                        ArgumentsInner: { Length: > 0 }
                    } functionContext)
                {
                    foreach (var argExpression in functionContext.ArgumentsInner)
                    {
                        if (argExpression.Name!.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                        {
                            return argExpression;
                        }
                    }
                }

                if (context.ReturnWorthy)
                    break;
            }

            return null;
        }

        ParameterExpression GetVariable(string name, bool mustBeAtCurrent = false)
        {
            var v = GetVariableNullable(name, mustBeAtCurrent);
            if (v == null)
                throw RCaronException.VariableNotFound(name);
            return v;
        }

        Expression GetSingleExpression(PosToken token)
        {
            switch (token)
            {
                case ConstToken constToken:
                    return Expression.Constant(constToken.Value);
                case VariableToken variableToken:
                {
                    switch (variableToken.Name)
                    {
                        case "true":
                            return Expression.Constant(true);
                        case "false":
                            return Expression.Constant(false);
                        case "null":
                            return Expression.Constant(null);
                    }

                    return GetVariable(variableToken.Name);
                    // if (!variables.TryGetValue(variableToken.Name, out var variable))
                    //     throw new Exception("variable not declared");
                    // return variable;
                }
                case MathValueGroupPosToken mathValueGroupPosToken:
                    return GetMathExpression(mathValueGroupPosToken.ValueTokens);
                case LogicalOperationValuePosToken logicalToken:
                    return GetLogicalExpression(logicalToken);
                case ComparisonValuePosToken comparisonToken:
                    return GetComparisonExpression(comparisonToken);
                case CallLikePosToken { Name: "@" } callToken:
                {
                    var exps = new Expression[callToken.Arguments.Length];
                    for (var i = 0; i < callToken.Arguments.Length; i++)
                    {
                        var p = GetHighExpression(callToken.Arguments[i]);
                        if (p.Type != typeof(object))
                            p = Expression.Convert(p, typeof(object));
                        exps[i] = p;
                    }

                    return Expression.NewArrayInit(typeof(object), exps);
                }
                case CallLikePosToken { Name: "float" } callToken:
                {
                    return Expression.Convert(GetHighExpression(callToken.Arguments[0]), typeof(float));
                }
                case DotGroupPosToken dotGroup:
                    return GetDotGroupExpression(dotGroup);
                case ExternThingToken externThing:
                {
                    // todo(perf): cache directly here
                    return Expression.New(typeof(RCaronType).GetConstructor(new[] { typeof(Type) }),
                        Expression.Call(null, typeof(TypeResolver).GetMethod(nameof(TypeResolver.FindType))!,
                            Expression.Constant(externThing.String), Expression.Constant(parsed.FileScope)));
                    // return Expression.Dynamic(new RCaronTypeBinder(), typeof(RCaronType),
                    //     Expression.Constant(externThing.String), Expression.Constant(parsed.FileScope));
                }
                case CallLikePosToken callToken:
                {
                    return MethodCall(callToken.Name, callToken: callToken);
                }
            }

            throw new Exception($"Single expression {token.Type} not implemented");
        }

        Expression GetMathExpression(ReadOnlySpan<PosToken> tokens)
        {
            if (tokens.Length == 1 && tokens[0] is MathValueGroupPosToken mathToken)
                return GetMathExpression(mathToken.ValueTokens);
            Expression exp = GetSingleExpression(tokens[0]);
            for (var i = 1; i < tokens.Length; i++)
            {
                var opToken = (ValueOperationValuePosToken)tokens[i];

                void Do(ExpressionType expressionType, in ReadOnlySpan<PosToken> tokens)
                {
                    exp = Expression.Dynamic(Binder.BinaryOperation(CSharpBinderFlags.None, expressionType, null,
                            new[]
                            {
                                CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                                CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
                            }), typeof(object), exp,
                        GetSingleExpression(tokens[++i]));
                }

                switch (opToken.Operation)
                {
                    case OperationEnum.Sum:
                        Do(ExpressionType.Add, tokens);
                        // exp = Expression.Add(exp, GetSingleExpression(tokens[++i]));
                        break;
                    case OperationEnum.Subtract:
                        Do(ExpressionType.Subtract, tokens);
                        // exp = Expression.Subtract(exp, GetSingleExpression(tokens[++i]));
                        break;
                    case OperationEnum.Divide:
                        Do(ExpressionType.Divide, tokens);
                        // exp = Expression.Divide(exp, GetSingleExpression(tokens[++i]));
                        break;
                    case OperationEnum.Multiply:
                        Do(ExpressionType.Multiply, tokens);
                        // exp = Expression.Multiply(exp, GetSingleExpression(tokens[++i]));
                        break;
                    case OperationEnum.Modulo:
                        Do(ExpressionType.Modulo, tokens);
                        // exp = Expression.Modulo(exp, GetSingleExpression(tokens[++i]));
                        break;
                    case OperationEnum.Range:
                    {
                        if (exp is ConstantExpression constantExpression)
                        {
                            var right = GetSingleExpression(tokens[++i]);
                            if (right is ConstantExpression rightConstantExpression)
                            {
                                // todo: this won't do smart conversions to long I assume -- wait I don't even have those
                                var range = new RCaronRange((long)constantExpression.Value!,
                                    (long)rightConstantExpression.Value!);
                                exp = Expression.Constant(range);
                            }
                            else
                            {
                                exp = Expression.New(
                                    typeof(RCaronRange).GetConstructor(new[] { typeof(long), typeof(long) })!,
                                    exp, right);
                            }
                        }

                        break;
                    }
                }
            }

            return exp;
        }

        Expression GetComparisonExpression(ComparisonValuePosToken comparisonToken)
        {
            Expression DoDynamic(ExpressionType expressionType)
            {
                // todo: for some reason have to convert to bool instead of the returnType param in the Expression.Dynamic call being typeof(bool)
                return Expression.Convert(Expression.Dynamic(Binder.BinaryOperation(CSharpBinderFlags.None,
                        expressionType, null,
                        new[]
                        {
                            CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                            CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
                        }), typeof(object), GetSingleExpression(comparisonToken.Left),
                    GetSingleExpression(comparisonToken.Right)), typeof(bool));
            }

            switch (comparisonToken.ComparisonToken)
            {
                case { Operation: OperationEnum.IsEqual }:
                    return DoDynamic(ExpressionType.Equal);
                case { Operation: OperationEnum.IsNotEqual }:
                    return DoDynamic(ExpressionType.NotEqual);
                case { Operation: OperationEnum.IsGreater }:
                    return DoDynamic(ExpressionType.GreaterThan);
                case { Operation: OperationEnum.IsGreaterOrEqual }:
                    return DoDynamic(ExpressionType.GreaterThanOrEqual);
                case { Operation: OperationEnum.IsLess }:
                    return DoDynamic(ExpressionType.LessThan);
                case { Operation: OperationEnum.IsLessOrEqual }:
                    return DoDynamic(ExpressionType.LessThanOrEqual);
            }

            throw new($"GetComparisonExpression for {comparisonToken.ComparisonToken.Type} not implemented");
        }

        Expression GetLogicalExpression(LogicalOperationValuePosToken logicalToken)
        {
            var left = GetSingleExpression(logicalToken.Left);
            var right = GetSingleExpression(logicalToken.Right);
            switch (logicalToken.ComparisonToken.Operation)
            {
                case OperationEnum.And:
                    return Expression.And(left, right);
                case OperationEnum.Or:
                    return Expression.Or(left, right);
                default:
                    throw new("GetLogicalExpression for {logicalToken.ComparisonToken.Type} not implemented");
            }
        }

        Expression GetHighExpression(ArraySegment<PosToken> tokens)
            => tokens.Count switch
            {
                > 0 when tokens[0] is KeywordToken keywordToken => MethodCall(keywordToken.String,
                    argumentTokens: tokens.Segment(1..)),
                1 => GetSingleExpression(tokens[0]),
                > 2 => GetMathExpression(tokens),
                _ => throw new Exception("what he fuck")
            };

        Expression GetBoolExpression(ReadOnlySpan<PosToken> tokens)
            => tokens switch
            {
                { Length: 1 } when tokens[0] is ComparisonValuePosToken comparisonToken => GetComparisonExpression(
                    comparisonToken),
                { Length: 1 } when tokens[0] is LogicalOperationValuePosToken logicalToken => GetLogicalExpression(
                    logicalToken),
                [VariableToken { Name: "true" }] => Expression.Constant(true),
                [VariableToken { Name: "false" }] => Expression.Constant(false),
                // todo: variable and constant
                // { Length: 1 } when tokens[0] is ValuePosToken => (bool)SimpleEvaluateExpressionSingle(tokens[0])!,
                _ => throw new Exception("what he fuck")
            };

        Expression GetDotGroupExpression(DotGroupPosToken dotGroup, bool doLast = true)
        {
            var value = GetSingleExpression(dotGroup.Tokens[0]);
            var l = doLast ? dotGroup.Tokens.Length : dotGroup.Tokens.Length - 1;
            for (var i = 1; i < l; i++)
            {
                var t = dotGroup.Tokens[i];
                if (t.Type == TokenType.Dot || t.Type == TokenType.Colon)
                    continue;
                if (t is CallLikePosToken callToken)
                {
                    var exps = new Expression[callToken.Arguments.Length];
                    for (var j = 0; j < callToken.Arguments.Length; j++)
                    {
                        var p = GetHighExpression(callToken.Arguments[j]);
                        if (p.Type != typeof(object))
                            p = Expression.Convert(p, typeof(object));
                        exps[j] = p;
                    }

                    // var expsE = exps.Prepend(Expression.Constant(null));

                    // Expression? createContext = null;
                    // if (value.Type == typeof(RCaronType) || (value is DynamicExpression dexp && dexp.Type == typeof(RCaronType)))
                    // {
                    //     createContext = Expression.New(typeof(InvokeContext).GetConstructor(new[]
                    //         { typeof(Type), typeof(bool), typeof(object) })!, new Expression[]
                    //     {
                    //         Expression.Property(value, nameof(RCaronType.Type)),
                    //         Expression.Constant(true),
                    //         Expression.Constant(null)
                    //     });
                    // }
                    //
                    // var name = Expression.Convert(Expression.Constant(callToken.Name), typeof(String_OR_InvokeMemberName));
                    //
                    // var args = Expression.NewArrayInit(typeof(object), exps);
                    //
                    // value = Expression.Call(null, typeof(Dynamic).GetMethod(nameof(Dynamic.InvokeMember))!,
                    //     new []{ createContext, name}.Append(args)!
                    //     );

                    {
                        var expsNew = new Expression[exps.Length + 1];
                        expsNew[0] = value;
                        Array.Copy(exps, 0, expsNew, 1, exps.Length);
                        exps = expsNew;
                    }


                    // value = Expression.Dynamic(
                    //     Binder.InvokeMember(CSharpBinderFlags.None, callToken.Name, null, null,
                    //         exps.Select(exp => CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null))
                    //             .ToArray()), typeof(object), exps);
                    value = Expression.Dynamic(
                        new RCaronInvokeMemberBinder(callToken.Name, true, new CallInfo(exps.Length,
                            // todo: named args https://learn.microsoft.com/en-us/dotnet/api/system.dynamic.callinfo?view=net-6.0#examples
                            Array.Empty<string>()), parsed.FileScope),
                        typeof(object),
                        exps);


                    // value = Expression.Dynamic(
                    //     Binder.InvokeMember(CSharpBinderFlags.None, callToken.Name, null,
                    //         null,
                    //         // todo: doesn't do named arguments
                    //         expsE.Select(e => CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null))
                    //             .ToArray()
                    //         // new[]
                    //         // {
                    //         //     CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                    //         //     CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.UseCompileTimeType, null)
                    //         // }
                    //     ),
                    //     typeof(object),
                    //     expsE);
                    // // todo: unreplicate/make you know what
                    // var exps = new Expression[callToken.Arguments.Length];
                    // for (var j = 0; j < callToken.Arguments.Length; j++)
                    // {
                    //     var p = GetHighExpression(callToken.Arguments[j]);
                    //     if (p.Type != typeof(object))
                    //         p = Expression.Convert(p, typeof(object));
                    //     exps[j] = p;
                    // }
                    //
                    // value = Expression.Call(value, callToken.Name, Array.Empty<Type>(), exps);
                }
                else if (t is KeywordToken keywordToken)
                {
                    value = DynamicExpression.Dynamic(
                        new RCaronGetMemberBinder(keywordToken.String, true, parsed.FileScope),
                        typeof(object),
                        value);
                }
                else if (t is IndexerToken indexerToken)
                {
                    var indexExpression = GetHighExpression(indexerToken.Tokens);
                    value = DynamicExpression.Dynamic(
                        new RCaronGetIndexBinder(new CallInfo(1, Array.Empty<string>()), parsed.FileScope,
                            fakedMotor),
                        typeof(object),
                        value, indexExpression);
                    // value = DynamicExpression.Dynamic(
                    //     Binder.GetIndex(CSharpBinderFlags.None, null, new[]
                    //     {
                    //         CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                    //         CSharpArgumentInfo.Create(
                    //             indexExpression is ConstantExpression
                    //                 ? CSharpArgumentInfoFlags.UseCompileTimeType |
                    //                   CSharpArgumentInfoFlags.Constant
                    //                 : CSharpArgumentInfoFlags.None, null)
                    //     }),
                    //     typeof(object),
                    //     value,
                    //     indexExpression);
                }
                else
                {
                    throw new Exception($"invalid dot group token: {t.Type}");
                }
            }

            return value;
        }

        Expression MethodCall(string name, ArraySegment<PosToken> argumentTokens = default,
            CallLikePosToken? callToken = null)
        {
            if (callToken != null)
            {
                switch (callToken.Name.ToLowerInvariant())
                {
                    case "int32":
                        return Expression.Convert(GetHighExpression(callToken.Arguments[0]), typeof(Int32));
                    case "int64":
                        return Expression.Convert(GetHighExpression(callToken.Arguments[0]), typeof(Int64));
                    case "float":
                        return Expression.Convert(GetHighExpression(callToken.Arguments[0]), typeof(Single));
                    case "string":
                        return Expression.Dynamic(
                            new RCaronInvokeMemberBinder("ToString", false, new CallInfo(0, Array.Empty<string>()),
                                parsed.FileScope), /* todo: wtf can't do typeof(string)*/ typeof(object),
                            GetHighExpression(callToken.Arguments[0])).EnsureIsType(typeof(string));
                    case "globalset":
                        return Expression.Call(fakedMotorConstant, typeof(Motor).GetMethod(nameof(Motor.SetVar))!,
                            GetHighExpression(callToken.Arguments[0]).EnsureIsType(typeof(string)),
                            GetHighExpression(callToken.Arguments[1]).EnsureIsType(typeof(object)));
                    case "globalget":
                        return Expression.Call(fakedMotorConstant, typeof(Motor).GetMethod(nameof(Motor.GetVar))!,
                            GetHighExpression(callToken.Arguments[0]).EnsureIsType(typeof(string)));
                    case "sum":
                        return Expression.Dynamic(Binder.BinaryOperation(CSharpBinderFlags.None, ExpressionType.Add,
                                null,
                                new[]
                                {
                                    CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                                    CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
                                }), typeof(object),
                            GetHighExpression(callToken.Arguments[0]),
                            GetHighExpression(callToken.Arguments[1]));
                }
            }

            var enumerator = callToken == null
                ? new ArgumentEnumerator(argumentTokens)
                : new ArgumentEnumerator(callToken!);
            switch (name)
            {
                case "print":
                {
                    // todo: make it so that this has can be directly adding the expression onto the destination instead of having to create a new list and returning a block
                    var expressions = new List<Expression>();
                    while (enumerator.MoveNext())
                    {
                        expressions.Add(Expression.Call(consoleWriteMethod.Value,
                            // todo(perf): can get the correct write method instead of casting to object
                            GetHighExpression(enumerator.CurrentTokens).EnsureIsType(typeof(object))));
                        expressions.Add(Expression.Call(consoleWriteMethod.Value,
                            Expression.Constant(' ', typeof(object))));
                    }

                    expressions.Add(Expression.Call(consoleWriteLineMethod.Value));
                    return Expression.Block(expressions);
                }
                case "println":
                {
                    // todo: make it so that this has can be directly adding the expression onto the destination instead of having to create a new list and returning a block
                    var expressions = new List<Expression>();
                    while (enumerator.MoveNext())
                    {
                        expressions.Add(Expression.Call(consoleWriteLineWithArgMethod.Value,
                            // todo(perf): can get the correct write method instead of casting to object
                            GetHighExpression(enumerator.CurrentTokens).EnsureIsType(typeof(object))));
                    }

                    return Expression.Block(expressions);
                }
            }

            // todo: doesn't do named arguments won't what
            // todo(perf): cache CompiledContext
            // var site = new KeywordCallCallSite(name, new CompiledContext(functions, parsed.FileScope, fakedMotorConstant));
            Expression args;
            List<Expression> positionalArgs = new();
            Dictionary<string, Expression> namedArgs = new();
            while (enumerator.MoveNext())
            {
                if (enumerator.CurrentName != null)
                {
                    namedArgs.Add(enumerator.CurrentName, GetHighExpression(enumerator.CurrentTokens));
                }
                else if (!enumerator.HitNamedArgument)
                {
                    positionalArgs.Add(GetHighExpression(enumerator.CurrentTokens));
                }
                else
                {
                    throw RCaronException.PositionalArgumentAfterNamedArgument();
                }
            }

            var arguments = new RCaronOtherBinder.FunnyArguments(positionalArgs.ToArray(), namedArgs);

            // args = Expression.New(
            //     typeof(KeywordCallCallSite.Arguments).GetConstructors().First(),
            //     new Expression[]
            //     {
            //         Expression.NewArrayInit(typeof(object),
            //             positionalArgs.Select(arg => arg.EnsureIsType(typeof(object)))),
            //         Expression.Constant(namedArgs.Select(a => a.Key).ToArray()),
            //         Expression.NewArrayInit(typeof(object), namedArgs.Select(a => a.Value.EnsureIsType(typeof(object))))
            //     });
            return Expression.Dynamic(
                new RCaronOtherBinder(new CompiledContext(functions, parsed.FileScope, fakedMotorConstant), name,
                    arguments), typeof(object), Expression.Constant(arguments));

            // if (tokenLine != null)
            // {
            //     var args = new Expression[tokenLine.Tokens.Length - 1];
            //     for (var i = 1; i < tokenLine.Tokens.Length; i++)
            //     {
            //         args[i - 1] = GetSingleExpression(tokenLine.Tokens[i]);
            //     }
            //
            //     arr = Expression.NewArrayInit(typeof(object), args);
            // }
            // else if (callToken != null)
            // {
            //     var args = new Expression[callToken.Arguments];
            //     for (var i = 1; i < tokenLine.Tokens.Length; i++)
            //     {
            //         args[i - 1] = GetSingleExpression(tokenLine.Tokens[i]);
            //     }
            //
            //     arr = Expression.NewArrayInit(typeof(object), args);
            // }
            // else
            // {
            //     throw new();
            // }

            // return Expression.Call(Expression.Constant(site), nameof(site.Run), null, args);
        }

        Expression AssignGlobal(string name, Expression value)
            => Expression.Call(fakedMotorConstant, typeof(Motor).GetMethod(nameof(Motor.SetVar))!,
                Expression.Constant(name), value.EnsureIsType(typeof(object)));

        void IfFakedAssignVariable(string variableName, Expression varExp, List<Expression> expressions)
        {
            if (fakedMotor != null)
            {
                var stillGlobal = true;
                for (int i = contextStack.Count - 1; i > -1; i--)
                {
                    if (contextStack.At(i).ReturnWorthy)
                    {
                        stillGlobal = false;
                        break;
                    }
                }

                if (stillGlobal)
                    expressions.Add(AssignGlobal(variableName, varExp));
            }
        }

        void DoLine(Line line, List<Expression> expressions, IList<Line> lines, ref int index)
        {
            ParameterExpression GetOrNewVariable(string name, bool mustBeAtCurrent = false, Type specificType = null)
            {
                var varExp = GetVariableNullable(name, mustBeAtCurrent);
                if (varExp == null)
                {
                    var peak = contextStack.Peek();
                    varExp = Expression.Variable(specificType ?? typeof(object), name);
                    peak.Variables ??= _newVariableDict();
                    peak.Variables.Add(name, varExp);
                }

                return varExp;
            }

            switch (line)
            {
                // variable
                case TokenLine { Type: LineType.VariableAssignment } tokenLine:
                {
                    var vt = (VariableToken)tokenLine.Tokens[0];
                    var varExp = GetOrNewVariable(vt.Name);

                    var right = GetHighExpression(tokenLine.Tokens.Segment(2..));
                    // var right = GetMathExpression(tokenLine.Tokens.AsSpan()[2..]);
                    if (right.Type != typeof(object))
                        right = Expression.Convert(right, typeof(object));
                    expressions.Add(Expression.Assign(varExp, right));

                    IfFakedAssignVariable(vt.Name, varExp, expressions);

                    break;
                }
                case CodeBlockLine codeBlockLine:
                {
                    expressions.Add(DoLines(codeBlockLine.Token.Lines));
                    break;
                }
                case TokenLine { Type: LineType.KeywordPlainCall } tokenLine:
                {
                    var name = ((KeywordToken)tokenLine.Tokens[0]).String;
                    switch (name)
                    {
                        case "open":
                            // todo: just going to do this like this for now
                            parsed.FileScope.UsedNamespaces ??= new();
                            expressions.Add(Expression.Call(
                                Expression.Property(Expression.Constant(parsed.FileScope), "UsedNamespaces"), "Add",
                                null,
                                Expression.Convert(GetSingleExpression(tokenLine.Tokens[1]), typeof(string))));
                            break;
                        case "open_ext":
                            // todo: just going to do this like this for now
                            parsed.FileScope.UsedNamespacesForExtensionMethods ??= new();
                            expressions.Add(Expression.Call(
                                Expression.Property(Expression.Constant(parsed.FileScope),
                                    "UsedNamespacesForExtensionMethods"), "Add",
                                null,
                                Expression.Convert(GetSingleExpression(tokenLine.Tokens[1]), typeof(string))));
                            break;
                        case "return":
                        {
                            Context c;
                            for (var i = contextStack.Count - 1; (c = contextStack.At(i)).ReturnWorthy == false; i--)
                            {
                            }

                            // while ((c = contextStack.Pop()).ReturnWorthy == false)
                            // {
                            // }
                            if (tokenLine.Tokens.Length == 1)
                                expressions.Add(Expression.Return(c.ReturnLabel,
                                    Expression.Constant(ReturnWithoutValue, typeof(object))));
                            else
                                expressions.Add(Expression.Return(c.ReturnLabel,
                                    GetHighExpression(tokenLine.Tokens.Segment(1..)).EnsureIsType(c.ReturnLabel.Type)));
                            break;
                        }
                        case "throw":
                        {
                            expressions.Add(Expression.Throw(GetHighExpression(tokenLine.Tokens.Segment(1..))));
                            break;
                        }
                        case "break":
                        {
                            Context c;
                            for (var i = contextStack.Count - 1; (c = contextStack.At(i)) is not LoopContext; i--)
                            {
                            }

                            expressions.Add(Expression.Break(((LoopContext)(c)).BreakLabel));
                            break;
                        }
                        case "continue":
                        {
                            Context c;
                            for (var i = contextStack.Count - 1; (c = contextStack.At(i)) is not LoopContext; i--)
                            {
                            }

                            expressions.Add(Expression.Continue(((LoopContext)(c)).ContinueLabel));
                            break;
                        }
                        default:
                        {
                            expressions.Add(MethodCall(name, argumentTokens: tokenLine.Tokens.Segment(1..)));
                            break;
                        }
                    }

                    break;
                }
                case TokenLine { Type: LineType.KeywordCall } tokenLine
                    when tokenLine.Tokens[0] is CallLikePosToken callToken:
                {
                    var name = callToken.Name;
                    switch (name)
                    {
                        case "throw":
                        {
                            expressions.Add(Expression.Throw(GetHighExpression(callToken.Arguments[0])));
                            break;
                        }
                        default:
                            expressions.Add(MethodCall(name, callToken: callToken));
                            break;
                    }

                    break;
                }
                case TokenLine { Type: LineType.IfStatement } tokenLine
                    when tokenLine.Tokens[0] is CallLikePosToken callToken &&
                         tokenLine.Tokens[1] is CodeBlockToken codeBlockToken:
                {
                    // ElseState = false;
                    // if (SimpleEvaluateBool(callToken.Arguments[0]))
                    // {
                    //     ElseState = true;
                    //     BlockStack.Push(new(false, false, null, GetFileScope()));
                    //     var res = RunCodeBlock((CodeBlockToken)line.Tokens[1]);
                    //     if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                    //     {
                    //         return (true, res);
                    //     }
                    // }
                    // todo(perf): make an exceptional case for when it is only `if(){}else{}` to do without the elseState variable and use Expression.IfThenElse
                    var elseState = GetOrNewVariable("$$elseState", mustBeAtCurrent: true, specificType: typeof(bool));
                    expressions.Add(Expression.Assign(elseState, Expression.Constant(false)));
                    expressions.Add(Expression.IfThen(GetBoolExpression(callToken.Arguments[0]),
                        Expression.Block(
                            Expression.Assign(elseState, Expression.Constant(true)),
                            DoLines(codeBlockToken.Lines))));


                    // expressions.Add(Expression.IfThen(GetBoolExpression(callToken.Arguments[0]),
                    //     DoLines(codeBlockToken.Lines)));

                    break;
                }
                case TokenLine { Type: LineType.ElseIfStatement } tokenLine
                    when tokenLine.Tokens[1] is CallLikePosToken callToken:
                {
                    var elseState = GetVariable("$$elseState", mustBeAtCurrent: true);
                    expressions.Add(Expression.IfThen(Expression.AndAlso(Expression.Not(elseState),
                            GetBoolExpression(callToken.Arguments[0])),
                        Expression.Block(
                            Expression.Assign(elseState, Expression.Constant(true)),
                            DoLines(((CodeBlockToken)tokenLine.Tokens[2]).Lines))));
                    break;
                }
                case TokenLine { Type: LineType.ElseStatement } tokenLine:
                {
                    var elseState = GetVariable("$$elseState", mustBeAtCurrent: true);
                    expressions.Add(Expression.IfThen(Expression.Not(elseState),
                        Expression.Block(
                            Expression.Assign(elseState, Expression.Constant(true)),
                            DoLines(((CodeBlockToken)tokenLine.Tokens[1]).Lines))));
                    break;
                }
                case TokenLine { Type: LineType.TryBlock } tokenLine
                    when tokenLine.Tokens[1] is CodeBlockToken codeBlockToken:
                {
                    bool TryGetFrom(int index, LineType what, out CodeBlockToken? cbt)
                    {
                        var l = lines[index];
                        if (l is TokenLine tokenLine && tokenLine.Type == what &&
                            tokenLine.Tokens[1] is CodeBlockToken cbt2)
                        {
                            cbt = cbt2;
                            return true;
                        }

                        cbt = null;
                        return false;
                    }

                    var catchBlock = TryGetFrom(index + 1, LineType.CatchBlock, out var cbt) ? cbt : null;
                    var finallyBlock = TryGetFrom(index + (catchBlock is null ? 1 : 2), LineType.FinallyBlock, out cbt)
                        ? cbt
                        : null;

                    CatchBlock? catchExp = null;
                    if (catchBlock != null)
                    {
                        var expVar = Expression.Variable(typeof(Exception), "exception");
                        var c = new ContextWithSpecialVariables
                        {
                            SpecialVariables = _newVariableDict()
                        };
                        c.SpecialVariables.Add("exception", expVar);
                        contextStack.Push(c);
                        catchExp = Expression.Catch(expVar, DoLines(catchBlock.Lines, useCurrent: true,
                            theCurrentToUse: c));
                        Assert(contextStack.Pop() == c);
                    }

                    var tryBlock = Expression.TryCatchFinally(
                        DoLines(codeBlockToken.Lines),
                        finallyBlock is null ? null : DoLines(finallyBlock.Lines),
                        catchExp is null
                            ? null
                            : new[] { catchExp });
                    index += catchBlock is null ? 1 : (finallyBlock is null ? 1 : 2);
                    expressions.Add(tryBlock);
                    break;
                }
                case TokenLine { Type: LineType.SwitchStatement } tokenLine:
                {
                    var switchValue = GetHighExpression(((CallLikePosToken)tokenLine.Tokens[0]).Arguments[0]);
                    var cases = new List<SwitchCase>();
                    var casesBlock = (CodeBlockToken)tokenLine.Tokens[1];
                    Expression? defaultBody = null;
                    for (var i = 1; i < casesBlock.Lines.Count - 1; i++)
                    {
                        var caseLine = ((TokenLine)casesBlock.Lines[i]);
                        var caseBlock = (CodeBlockToken)caseLine.Tokens[1];
                        var body = DoLines(caseBlock.Lines);
                        if (caseLine.Tokens[0] is KeywordToken { String: "default" })
                        {
                            defaultBody = body;
                            continue;
                        }
                        var caseValue = GetSingleExpression(caseLine.Tokens[0]);
                        cases.Add(Expression.SwitchCase(body, caseValue));
                    }

                    // todo: I guess will have to make switch cases compile to else ifs but keep this special case when every case is a constant
                    switchValue = switchValue.EnsureIsType(cases[0].TestValues[0].Type);
                    expressions.Add(Expression.Switch(switchValue, defaultBody, cases.ToArray()));
                    break;
                }
                case TokenLine { Type: LineType.AssignerAssignment } tokenLine
                    when tokenLine.Tokens[0] is DotGroupPosToken dotGroupPosToken:
                {
                    // var assign = GetDotGroupExpression(dotGroupPosToken, doLast: true);
                    var assign = GetDotGroupExpression(dotGroupPosToken, doLast: false);
                    var lastToken = dotGroupPosToken.Tokens[^1];
                    switch (lastToken)
                    {
                        case KeywordToken keywordToken:
                        {
                            assign = Expression.Dynamic(
                                new RCaronSetMemberBinder(keywordToken.String, true, parsed.FileScope), typeof(object),
                                assign, GetHighExpression(tokenLine.Tokens[2..]));
                            break;
                        }
                        case IndexerToken indexerToken:
                        {
                            assign = Expression.Dynamic(Binder.SetIndex(CSharpBinderFlags.None, null, new[]
                                {
                                    CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                                    CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                                    CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
                                }), typeof(object), assign,
                                GetHighExpression(indexerToken.Tokens),
                                GetHighExpression(tokenLine.Tokens[2..]));
                            break;
                        }
                        default:
                            throw new("Unsupported last token for AssignerAssignment");
                    }

                    // expressions.Add(Expression.Assign(assign, GetHighExpression(tokenLine.Tokens[2..])));
                    expressions.Add(assign);

                    break;
                }
                case TokenLine { Type: LineType.UnaryOperation } tokenLine:
                {
                    var variableName = ((VariableToken)tokenLine.Tokens[0]).Name;
                    var varExp = GetVariable(variableName);
                    switch (tokenLine.Tokens[1])
                    {
                        case OperationPosToken { Operation: OperationEnum.UnaryIncrement }:
                            // expressions.Add(Expression.Dynamic(Binder.BinaryOperation(CSharpBinderFlags.None, ExpressionType.PostIncrementAssign,
                            //     null, new[]
                            //     {
                            //         CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
                            //     }), typeof(object), GetVariable(variableName)));
                            expressions.Add(Expression.Assign(varExp,
                                Expression.Dynamic(Binder.BinaryOperation(CSharpBinderFlags.None, ExpressionType.Add,
                                    null, new[]
                                    {
                                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
                                    }), typeof(object), varExp, Expression.Constant(1L))));
                            break;
                        case OperationPosToken { Operation: OperationEnum.UnaryDecrement }:
                            // expressions.Add(Expression.Dynamic(Binder.BinaryOperation(CSharpBinderFlags.None, ExpressionType.PostDecrementAssign,
                            //     null, new[]
                            //     {
                            //         CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
                            //     }), typeof(object), GetVariable(variableName)));
                            expressions.Add(Expression.Assign(varExp,
                                Expression.Dynamic(Binder.BinaryOperation(CSharpBinderFlags.None,
                                    ExpressionType.Subtract,
                                    null, new[]
                                    {
                                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
                                    }), typeof(object), varExp, Expression.Constant(1L))));
                            break;
                    }

                    IfFakedAssignVariable(variableName, varExp, expressions);
                    break;
                }
                case { Type: LineType.BlockStuff }:
                    break;
                case ForLoopLine { Type: LineType.ForLoop or LineType.QuickForLoop } forLoopLine:
                {
                    if (forLoopLine.Initializer is not null)
                    {
                        expressions.Add(DoLines(new[] { forLoopLine.Initializer }, useCurrent: true));
                    }

                    var @break = Expression.Label();
                    var @continue = Expression.Label();
                    var loopContext = new LoopContext() { BreakLabel = @break, ContinueLabel = @continue };
                    contextStack.Push(loopContext);
                    // expressions.Add(initializer ?? Expression.Empty());
                    var loop = Expression.Loop(
                        Expression.Block(
                            Expression.IfThen(
                                Expression.NotEqual(Expression.Constant(true),
                                    GetBoolExpression(forLoopLine.CallToken.Arguments[1])),
                                Expression.Break(@break)),
                            DoLines(forLoopLine.Body.Lines),
                            // Expression.Label(@continue),
                            DoLines(new[] { forLoopLine.Iterator })
                            // ,
                            //  Expression.Goto(@break)
                        ),
                        @break,
                        @continue);
                    expressions.Add(loop);
                    var pop = contextStack.Pop();
                    Assert(pop == loopContext);
                    break;
                }
                case TokenLine { Type: LineType.WhileLoop or LineType.DoWhileLoop } whileLoopLine
                    when whileLoopLine.Tokens[0] is CallLikePosToken callToken &&
                         whileLoopLine.Tokens[1] is CodeBlockToken cbt:
                {
                    var @break = Expression.Label();
                    var @continue = Expression.Label();
                    var loopContext = new LoopContext() { BreakLabel = @break, ContinueLabel = @continue };
                    contextStack.Push(loopContext);
                    // expressions.Add(initializer ?? Expression.Empty());
                    var loop = Expression.Loop(
                        whileLoopLine.Type == LineType.WhileLoop
                            ? Expression.Block(
                                Expression.IfThen(Expression.NotEqual(Expression.Constant(true),
                                    GetBoolExpression(callToken.Arguments[0])), Expression.Break(@break)),
                                DoLines(cbt.Lines))
                            : Expression.Block(
                                DoLines(cbt.Lines),
                                Expression.IfThen(Expression.NotEqual(Expression.Constant(true),
                                    GetBoolExpression(callToken.Arguments[0])), Expression.Break(@break)))
                        ,
                        @break,
                        @continue);
                    expressions.Add(loop);
                    var pop = contextStack.Pop();
                    Assert(pop == loopContext);
                    break;
                }
                case TokenLine { Type: LineType.ForeachLoop } whileLoopLine
                    when whileLoopLine.Tokens[0] is CallLikePosToken callToken &&
                         whileLoopLine.Tokens[1] is CodeBlockToken cbt:
                {
                    var currentVariableName = ((VariableToken)callToken.Arguments[0][0]).Name;
                    var enumerator = Expression.Variable(typeof(IEnumerator));
                    contextStack.Peek().Variables ??= _newVariableDict();
                    contextStack.Peek().Variables.Add("\u0159enumerator", enumerator);
                    expressions.Add(Expression.Assign(enumerator,
                        Expression.Call(Expression.Dynamic(Binder.Convert(CSharpBinderFlags.None, typeof(IEnumerable),
                                null), typeof(IEnumerable), GetHighExpression(callToken.Arguments[0].Segment(2..))),
                            "GetEnumerator", Array.Empty<Type>())));
                    var @break = Expression.Label();
                    var @continue = Expression.Label();
                    var loopContext = new LoopContext() { BreakLabel = @break, ContinueLabel = @continue };
                    contextStack.Push(loopContext);
                    // expressions.Add(initializer ?? Expression.Empty());

                    var loopBodyContext = new Context();
                    contextStack.Push(loopBodyContext);
                    loopBodyContext.Variables ??= _newVariableDict();
                    var currentVariable = Expression.Variable(typeof(object), "current");
                    loopBodyContext.Variables.Add(currentVariableName, currentVariable);
                    Expression loopBody = DoLines(cbt.Lines, useCurrent: true, prependLines: new List<Expression>
                    {
                        // currentVariable,
                        Expression.Assign(currentVariable,
                            Expression.Dynamic(Binder.Convert(CSharpBinderFlags.None, typeof(object),
                                null), typeof(object), Expression.Property(enumerator, "Current")))
                    }, theCurrentToUse: loopBodyContext);
                    var pop = contextStack.Pop();
                    Assert(pop == loopBodyContext);

                    var loop = Expression.Loop(
                        Expression.Block(
                            Expression.IfThen(Expression.NotEqual(Expression.Constant(true),
                                Expression.Call(enumerator, "MoveNext", null)), Expression.Break(@break)),
                            loopBody
                        ),
                        @break,
                        @continue);
                    expressions.Add(loop);
                    pop = contextStack.Pop();
                    Assert(pop == loopContext);
                    break;
                }
                case TokenLine { Type: LineType.LoopLoop } tokenLine:
                {
                    var cbt = (CodeBlockToken)tokenLine.Tokens[1];
                    var @break = Expression.Label();
                    var @continue = Expression.Label();
                    var loopContext = new LoopContext() { BreakLabel = @break, ContinueLabel = @continue };
                    contextStack.Push(loopContext);
                    // expressions.Add(initializer ?? Expression.Empty());
                    var loop = Expression.Loop(
                        Expression.Block(
                            DoLines(cbt.Lines)
                        ),
                        @break,
                        @continue);
                    expressions.Add(loop);
                    var pop = contextStack.Pop();
                    Assert(pop == loopContext);
                    break;
                }
                case TokenLine { Type: LineType.DotGroupCall } tokenLine:
                    expressions.Add(GetSingleExpression(tokenLine.Tokens[0]));
                    break;
                default:
                    throw new NotImplementedException($"line type {line.Type} is not implemented");
            }
        }

        BlockExpression DoLines(IList<Line> lines, bool returnWorthy = false, bool useCurrent = false,
            List<Expression>? prependLines = null, Context? theCurrentToUse = null)
        {
            var isRoot = contextStack.Count == 0;
            Context c = null;
            if (!useCurrent)
            {
                c = new Context
                {
                    ReturnWorthy = returnWorthy, ReturnLabel = returnWorthy ? Expression.Label(typeof(object)) : null
                };
                contextStack.Push(c);
            }
            else
                c = theCurrentToUse;

            var exps = prependLines ?? new List<Expression>();
            if (isRoot && fakedMotor is { GlobalScope.Variables.Count: > 0 })
            {
                c.Variables ??= _newVariableDict();
                foreach (var (name, value) in fakedMotor.GlobalScope.Variables)
                {
                    var variable = Expression.Variable(typeof(object));
                    c.Variables.Add(name, variable);
                    exps.Add(Expression.Assign(variable, Expression.Constant(value, typeof(object))));
                }
            }

            for (var i = 0; i < lines.Count; i++)
            {
                DoLine(lines[i], exps, lines, ref i);
            }

            if (c?.ReturnLabel != null)
                exps.Add(Expression.Label(c.ReturnLabel, Expression.Constant(NoReturnValue, typeof(object))));
            if (!useCurrent)
            {
                Assert(contextStack.Pop() == c);
            }

            return c == null ? Expression.Block(exps) : Expression.Block(c.Variables?.Select(g => g.Value), exps);
        }

        // do functions
        // todo(perf): could parallelize this in the future
        if (parsed.FileScope.Functions != null)
            foreach (var function in parsed.FileScope.Functions)
            {
                ParameterExpression[]? argumentsInner = null;
                if (function.Value.Arguments != null)
                {
                    argumentsInner = new ParameterExpression[function.Value.Arguments.Length];
                    for (var i = 0; i < function.Value.Arguments.Length; i++)
                    {
                        argumentsInner[i] = Expression.Parameter(typeof(object), function.Value.Arguments[i].Name);
                    }
                }

                // var argumentsOuter = Expression.Parameter(typeof(object[]));
                // var argsAssigner = new List<Expression>();
                // if (argumentsInner is not null or {Length:0})
                //     for (var i = 0; i < argumentsInner.Length; i++)
                //     {
                //         argsAssigner.Add(Expression.Assign(argumentsInner[i],
                //             Expression.ArrayIndex(argumentsOuter, Expression.Constant(i))));
                //     }

                var c = new FunctionContext() { ArgumentsInner = argumentsInner };
                contextStack.Push(c);
                var body = DoLines(function.Value.CodeBlock.Lines, true);
                Assert(contextStack.Pop() == c);

                functions[function.Key] = new CompiledFunction()
                {
                    Arguments = argumentsInner,
                    Body = body,
                    // Body = Expression.Block(body.Variables),
                    // Body = Expression.Block(new[] { argumentsOuter }.Concat(body.Variables),
                    //     argsAssigner.Concat(body.Expressions)),
                    OriginalFunction = function.Value
                };
            }

        // do main
        var block = DoLines(parsed.FileScope.Lines);

        // if (fakedMotor != null)
        //     variableExpressions = variableExpressions.Concat(new[] { fakedMotor });

        return (block);
    }

    private static Dictionary<string, ParameterExpression> _newVariableDict()
        => new(StringComparer.InvariantCultureIgnoreCase);

    private class Context
    {
        public Dictionary<string, ParameterExpression>? Variables { get; set; } = null;
        public bool ReturnWorthy { get; init; } = false;
        public LabelTarget? ReturnLabel { get; set; } = null;
    }

    private class ContextWithSpecialVariables : Context
    {
        public required Dictionary<string, ParameterExpression> SpecialVariables { get; init; }
    }
    private class LoopContext : Context
    {
        public LabelTarget BreakLabel { get; init; }
        public LabelTarget ContinueLabel { get; init; }
    }

    private class FunctionContext : Context
    {
        public ParameterExpression[]? ArgumentsInner { get; set; } = null;
    }

    public static void Assert(bool condition)
    {
        if (condition)
            return;
        throw new("doesn't pass an assert");
    }
}

public class CompiledFunction
{
    public required ParameterExpression[]? Arguments { get; init; }
    public required BlockExpression Body { get; init; }

    public required Function OriginalFunction { get; init; }
    // private Func<object[], object>? _compiled = null;
    //
    // private void PreCompile()
    // {
    //     var lambda = Expression.Lambda<Func<object[], object>>(Body, Arguments);
    //     _compiled = lambda.Compile();
    // }

    [UsedImplicitly]
    public object Invoke(params object?[]? args)
    {
        // if (_compiled == null)
        //     PreCompile();
        // return _compiled(args);
        if (_compiledDelegate == null)
            CompileDelegate();
        return args is null or { Length: 0 }
            ? _compiledDelegate.FastDynamicInvoke()
            : _compiledDelegate.FastDynamicInvoke(args);
    }


    private Delegate? _compiledDelegate = null;

    public void CompileDelegate()
    {
        var lambda = Expression.Lambda(Body, Arguments);
        _compiledDelegate = lambda.Compile();
    }
}

public record CompiledContext(Dictionary<string, CompiledFunction> Functions, FileScope FileScope,
    ConstantExpression FakedMotorConstant);