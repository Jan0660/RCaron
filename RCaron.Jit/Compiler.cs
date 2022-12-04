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
    public static (BlockExpression blockExpression, ParameterExpression? fakedMotor) CompileToBlock(
        RCaronRunnerContext parsed, bool isMain = false)
    {
        var contextStack = new NiceStack<Context>();
        var fakedMotor = isMain ? Expression.Parameter(typeof(Motor), "\u0159motor") : null;
        System.Lazy<MethodInfo> consoleWriteMethod =
            new(() => typeof(Console).GetMethod(nameof(Console.Write), new[] { typeof(object) })!);
        System.Lazy<MethodInfo> consoleWriteLineMethod =
            new(() => typeof(Console).GetMethod(nameof(Console.WriteLine), Array.Empty<Type>())!);

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
                throw new($"variable {name} not found");
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
                                new RCaronInvokeMemberBinder(callToken.Name, false, new CallInfo(exps.Length,
                                    // todo: named args https://learn.microsoft.com/en-us/dotnet/api/system.dynamic.callinfo?view=net-6.0#examples
                                    Array.Empty<string>())),
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

        void IfFakedAssignVariable(string variableName, Expression varExp, List<Expression> expressions)
        {
            if (fakedMotor != null)
            {
                expressions.Add(Expression.Call(fakedMotor, typeof(Motor).GetMethod(nameof(Motor.SetVar))!,
                    Expression.Constant(variableName), varExp));
            }
        }

        void DoLine(Line line, List<Expression> expressions)
        {
            switch (line)
            {
                // variable
                case TokenLine { Type: LineType.VariableAssignment } tokenLine:
                {
                    var vt = (VariableToken)tokenLine.Tokens[0];
                    ParameterExpression? varExp = null;
                    varExp = GetVariableNullable(vt.Name);
                    if (varExp == null)
                    {
                        var peak = contextStack.Peek();
                        varExp = Expression.Variable(typeof(object), vt.Name);
                        peak.Variables ??= _newVariableDict();
                        peak.Variables.Add(vt.Name, varExp);
                        // expressions.Add(varExp);
                    }

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
                        default:
                            throw new($"keyword plain call to '{name}' not implemented");
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
                // case ForLoopLine { Type: LineType.ForLoop or LineType.QuickForLoop } forLoopLine:
                // {
                //     Expression? initializer = null;
                //     string? iteratorVariableName = null;
                //     if (forLoopLine.Initializer is not null &&
                //         forLoopLine.Initializer.Type == LineType.VariableAssignment)
                //     {
                //         initializer = GetHighExpression(((TokenLine)forLoopLine.Initializer).Tokens.AsSpan()[2..]);
                //         iteratorVariableName = ((VariableToken)((TokenLine)forLoopLine.Initializer).Tokens[0]).Name;
                //     }
                //
                //     var loopContext = new LoopContext(iteratorVariableName);
                // }
                default:
                    throw new NotImplementedException($"line type {line.Type} is not implemented");
            }
        }

        BlockExpression DoLines(IList<Line> lines, bool returnWorthy = false)
        {
            var c = new Context { ReturnWorthy = returnWorthy };
            contextStack.Push(c);
            var exps = new List<Expression>();
            foreach (var line in lines)
            {
                DoLine(line, exps);
            }

            var p = contextStack.Pop();
            Debug.Assert(object.ReferenceEquals(c, p));
            return Expression.Block(c.Variables?.Select(g => g.Value), exps);
        }

        var block = DoLines(parsed.FileScope.Lines);

        // if (fakedMotor != null)
        //     variableExpressions = variableExpressions.Concat(new[] { fakedMotor });

        return (block, fakedMotor);
    }

    private static Dictionary<string, ParameterExpression> _newVariableDict()
        => new(StringComparer.InvariantCultureIgnoreCase);

    private class Context
    {
        public Dictionary<string, ParameterExpression>? Variables { get; set; } = null;
        public bool ReturnWorthy { get; init; } = false;
    }

    private class LoopContext : Context
    {
        public string? IterationVariableName { get; set; }
    }
}