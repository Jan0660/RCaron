using System.Collections;
using System.Diagnostics;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotNext.Linq.Expressions;
using Dynamitey;
using Dynamitey.DynamicObjects;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Scripting.Actions;
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
        var functions = new Dictionary<string, CompiledFunction>(StringComparer.InvariantCultureIgnoreCase);

        ParameterExpression? GetVariableNullable(string name)
        {
            for (var i = contextStack.Count - 1; i >= 0; i--)
            {
                var context = contextStack.At(i);
                if (context.Variables?.TryGetValue(name, out var variable) ?? false)
                {
                    return variable;
                }

                if (context.ReturnWorthy)
                    break;
            }

            return null;
        }

        ParameterExpression GetVariable(string name)
        {
            var v = GetVariableNullable(name);
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
                {
                    var value = GetSingleExpression(dotGroup.Tokens[0]);
                    for (var i = 1; i < dotGroup.Tokens.Length; i++)
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

                            value = Expression.Dynamic(
                                Binder.InvokeMember(CSharpBinderFlags.None, callToken.Name, null, null,
                                    exps.Select(exp => CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null))
                                        .ToArray()), typeof(object), exps);
                            // value = Expression.Dynamic(
                            //     new RCaronInvokeMemberBinder(callToken.Name, true, new CallInfo(exps.Length,
                            //         // todo: named args https://learn.microsoft.com/en-us/dotnet/api/system.dynamic.callinfo?view=net-6.0#examples
                            //         Array.Empty<string>())),
                            //     typeof(object),
                            //     exps);


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
                                Binder.GetMember(CSharpBinderFlags.None, keywordToken.String, null,
                                    new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) }),
                                typeof(object),
                                value);
                        }
                        else if (t is IndexerToken indexerToken)
                        {
                            var indexExpression = GetHighExpression(indexerToken.Tokens);
                            value = DynamicExpression.Dynamic(
                                Binder.GetIndex(CSharpBinderFlags.None, null, new[]
                                {
                                    CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                                    CSharpArgumentInfo.Create(
                                        indexExpression is ConstantExpression
                                            ? CSharpArgumentInfoFlags.UseCompileTimeType |
                                              CSharpArgumentInfoFlags.Constant
                                            : CSharpArgumentInfoFlags.None, null)
                                }),
                                typeof(object),
                                value,
                                indexExpression);
                        }
                        else
                        {
                            throw new Exception($"invalid dot group token: {t.Type}");
                        }
                    }

                    return value;
                }
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

        Expression GetHighExpression(ReadOnlySpan<PosToken> tokens)
            => tokens.Length switch
            {
                // > 0 when tokens[0] is KeywordToken keywordToken => MethodCall(keywordToken.String,
                //     argumentTokens: tokens.Segment(1..)),
                1 => GetSingleExpression(tokens[0]),
                > 2 => GetMathExpression(tokens),
                _ => throw new Exception("what he fuck")
            };

        Expression GetBoolExpression(ReadOnlySpan<PosToken> tokens)
            => tokens switch
            {
                { Length: 1 } when tokens[0] is ComparisonValuePosToken comparisonToken => GetComparisonExpression(
                    comparisonToken),
                { Length: 1 } when tokens[0] is LogicalOperationValuePosToken logicaltoken => GetLogicalExpression(
                    logicaltoken),
                [VariableToken { Name: "true" }] => Expression.Constant(true),
                [VariableToken { Name: "false" }] => Expression.Constant(false),
                // todo: variable and constant
                // { Length: 1 } when tokens[0] is ValuePosToken => (bool)SimpleEvaluateExpressionSingle(tokens[0])!,
                _ => throw new Exception("what he fuck")
            };

        Expression MethodCall(string name, TokenLine? tokenLine = null, CallLikePosToken? callToken = null)
        {
            if (callToken != null)
            {
                switch(callToken.Name.ToLowerInvariant())
                {
                    case "int32":
                        return Expression.Convert(GetHighExpression(callToken.Arguments[0]), typeof(Int32));
                    case "int64":
                        return Expression.Convert(GetHighExpression(callToken.Arguments[0]), typeof(Int64));
                    case "float":
                        return Expression.Convert(GetHighExpression(callToken.Arguments[0]), typeof(Single));
                    case "string":
                        return Expression.Convert(GetHighExpression(callToken.Arguments[0]), typeof(string));
                }
            }
            // todo: doesn't do named arguments won't what
            // todo(perf): cache CompiledContext
            var site = new KeywordCallCallSite(name, new CompiledContext(functions, parsed.FileScope));
            Expression args;
            var enumerator = tokenLine != null
                // todo(perf): use ArraySegment
                ? new ArgumentEnumerator(tokenLine.Tokens[1..])
                : new ArgumentEnumerator(callToken!);
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
                    throw new("hit named argument after positional argument");
                }
            }

            args = Expression.New(
                typeof(KeywordCallCallSite.Arguments).GetConstructors().First(),
                new Expression[]
                {
                    Expression.NewArrayInit(typeof(object), positionalArgs.Select(arg => arg.Type == typeof(object)
                        ? arg
                        : Expression.Convert(arg, typeof(object)))),
                    Expression.Constant(namedArgs.Select(a => a.Key).ToArray()),
                    Expression.NewArrayInit(typeof(object), namedArgs.Select(a => a.Value))
                });

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

            return Expression.Call(Expression.Constant(site), nameof(site.Run), null, args);
        }

        void IfFakedAssignVariable(string variableName, Expression varExp, List<Expression> expressions)
        {
            if (fakedMotor != null)
            {
                expressions.Add(Expression.Call(fakedMotorConstant, typeof(Motor).GetMethod(nameof(Motor.SetVar))!,
                    Expression.Constant(variableName), varExp));
            }
        }

        void DoLine(Line line, List<Expression> expressions)
        {
            ParameterExpression GetOrNewVariable(string name)
            {
                var varExp = GetVariableNullable(name);
                if (varExp == null)
                {
                    var peak = contextStack.Peek();
                    varExp = Expression.Variable(typeof(object), name);
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

                    var right = GetMathExpression(tokenLine.Tokens.AsSpan()[2..]);
                    if (right.Type != typeof(object))
                        right = Expression.Convert(right, typeof(object));
                    expressions.Add(Expression.Assign(varExp, right));

                    IfFakedAssignVariable(vt.Name, varExp, expressions);

                    break;
                }
                case TokenLine { Type: LineType.KeywordPlainCall } tokenLine:
                {
                    var name = ((KeywordToken)tokenLine.Tokens[0]).String;
                    switch (name)
                    {
                        case "print":
                            for (var i = 1; i < tokenLine.Tokens.Length; i++)
                            {
                                expressions.Add(Expression.Call(consoleWriteMethod.Value,
                                    GetSingleExpression(tokenLine.Tokens[i])));
                                expressions.Add(Expression.Call(consoleWriteMethod.Value,
                                    Expression.Constant(' ', typeof(object))));
                            }

                            expressions.Add(Expression.Call(consoleWriteLineMethod.Value));

                            break;
                        case "open":
                            // todo: just going to do this like this for now
                            parsed.FileScope.UsedNamespaces ??= new();
                            expressions.Add(Expression.Call(
                                Expression.Property(Expression.Constant(parsed.FileScope), "UsedNamespaces"), "Add",
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
                                    GetHighExpression(tokenLine.Tokens.AsSpan()[1..])));
                            break;
                        }
                        case "throw":
                        {
                            expressions.Add(Expression.Throw(GetHighExpression(tokenLine.Tokens.AsSpan()[1..])));
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
                            MethodCall(name, tokenLine: tokenLine);
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
                            MethodCall(name, callToken: callToken);
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
                    expressions.Add(Expression.IfThen(GetBoolExpression(callToken.Arguments[0]),
                        DoLines(codeBlockToken.Lines)));

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
                                null), typeof(IEnumerable), GetHighExpression(callToken.Arguments[0].AsSpan()[2..])),
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
                    exps.Add(Expression.Assign(variable, Expression.Constant(value)));
                }
            }

            foreach (var line in lines)
            {
                DoLine(line, exps);
            }

            if (c?.ReturnLabel != null)
                exps.Add(Expression.Label(c.ReturnLabel, Expression.Constant(NoReturnValue, typeof(object))));
            if (!useCurrent)
            {
                var p = contextStack.Pop();
                Assert(object.ReferenceEquals(c, p));
            }

            return c == null ? Expression.Block(exps) : Expression.Block(c.Variables?.Select(g => g.Value), exps);
        }

        // do functions
        if (parsed.FileScope.Functions != null)
            foreach (var function in parsed.FileScope.Functions)
            {
                ParameterExpression[]? argumentsInner = null;
                if (function.Value.Arguments != null)
                {
                    argumentsInner = new ParameterExpression[function.Value.Arguments.Length];
                    for (var i = 0; i < function.Value.Arguments.Length; i++)
                    {
                        argumentsInner[i] = Expression.Variable(typeof(object), function.Value.Arguments[i].Name);
                    }
                }

                var argumentsOuter = Expression.Parameter(typeof(object[]));
                var argsAssigner = new List<Expression>();
                if (argumentsInner != null)
                    for (var i = 0; i < argumentsInner.Length; i++)
                    {
                        argsAssigner.Add(Expression.Assign(argumentsInner[i],
                            Expression.ArrayIndex(argumentsOuter, Expression.Constant(i))));
                    }

                var body = DoLines(function.Value.CodeBlock.Lines, true);

                functions[function.Key] = new CompiledFunction()
                {
                    Arguments = argumentsOuter,
                    Body = Expression.Block(new[] { argumentsOuter }.Concat(body.Variables),
                        argsAssigner.Concat(body.Expressions)),
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
    public required ParameterExpression? Arguments { get; init; }
    public required BlockExpression Body { get; init; }
    public required Function OriginalFunction { get; init; }
    private Func<object[], object>? _compiled = null;

    private void PreCompile()
    {
        var lambda = Expression.Lambda<Func<object[], object>>(Body, Arguments);
        _compiled = lambda.Compile();
    }

    public object Invoke(params object[] args)
    {
        if (_compiled == null)
            PreCompile();
        return _compiled(args);
    }
}

public record CompiledContext(Dictionary<string, CompiledFunction> Functions, FileScope FileScope);