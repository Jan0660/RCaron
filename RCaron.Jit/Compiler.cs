﻿using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using Dynamitey;
using JetBrains.Annotations;
using Microsoft.CSharp.RuntimeBinder;
using RCaron.Classes;
using RCaron.Jit.Binders;
using RCaron.Binders;
using RCaron.Parsing;
using Binder = Microsoft.CSharp.RuntimeBinder.Binder;

namespace RCaron.Jit;

public class Compiler
{
    private static readonly object NoReturnValue = RCaronInsideEnum.NoReturnValue;
    private static readonly object ReturnWithoutValue = RCaronInsideEnum.ReturnWithoutValue;

    public static BlockExpression CompileToBlock(
        RCaronParserContext parsed, Motor? fakedMotor = null)
        => Compile(parsed, fakedMotor).blockExpression;

    public static (BlockExpression blockExpression, CompiledContext compiledContext) Compile(
        RCaronParserContext parsed, Motor? fakedMotor = null)
    {
        var contextStack = new NiceStack<Context>();
        var fakedMotorConstant = Expression.Constant(fakedMotor, typeof(Motor));
        Lazy<MethodInfo> consoleWriteMethod =
            new(() => typeof(Console).GetMethod(nameof(Console.Write), new[] { typeof(object) })!);
        Lazy<MethodInfo> consoleWriteLineMethod =
            new(() => typeof(Console).GetMethod(nameof(Console.WriteLine), Array.Empty<Type>())!);
        Lazy<MethodInfo> consoleWriteLineWithArgMethod =
            new(() => typeof(Console).GetMethod(nameof(Console.WriteLine), new[] { typeof(object) })!);
        Lazy<MethodInfo> log73Debug =
            new(() => typeof(Log73.Console).GetMethod(nameof(Log73.Console.Debug), new[] { typeof(object) })!);
        var functions = new Dictionary<string, CompiledFunction>(StringComparer.InvariantCultureIgnoreCase);
        var classes = new List<CompiledClass>();
        var compiledContext = new CompiledContext(functions, parsed.FileScope, fakedMotorConstant, classes, fakedMotor,
            new() { new MultiFileMethods() });
        compiledContext.CompiledContextConstant = Expression.Constant(compiledContext);

        Expression? GetVariableNullable(string name, bool mustBeAtCurrent = false)
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

                if (context is ContextWithSpecialVariables spec &&
                    spec.SpecialVariables.TryGetValue(name, out var specialVariable))
                {
                    return specialVariable;
                }

                if (context.ReturnWorthy && contextStack.At(0) is FunctionContext functionContext)
                {
                    // function arguments
                    if (functionContext.ArgumentsInner is not null or { Length: 0 })
                        foreach (var argExpression in functionContext.ArgumentsInner)
                        {
                            if (argExpression.Name!.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                            {
                                return argExpression;
                            }
                        }

                    // class properties
                    if (functionContext.ClassDefinition is not null)
                    {
                        // instance property
                        var propertyIndex = functionContext.ClassDefinition.GetPropertyIndex(name);
                        if (propertyIndex != -1)
                        {
                            var classInstance = functionContext.ArgumentsInner![0];
                            return Expression.ArrayAccess(Expression.Property(classInstance, "PropertyValues"),
                                Expression.Constant(propertyIndex));
                        }
                    }

                    // class static properties (inside instance function or static function)
                    if ((functionContext.ClassDefinition ?? functionContext.InsideStaticClassDefinition) is
                        { } definition)
                    {
                        // static property
                        var staticPropertyIndex = definition.GetStaticPropertyIndex(name);
                        if (staticPropertyIndex != -1)
                        {
                            return Expression.ArrayAccess(
                                Expression.Constant(definition.StaticPropertyValues),
                                Expression.Constant(staticPropertyIndex));
                        }
                    }
                }

                if (context.ReturnWorthy)
                    break;
            }

            return null;
        }

        Expression GetVariable(string name, bool mustBeAtCurrent = false)
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
                }
                case { Type: TokenType.Range }:
                    return Expression.Constant("..");
                case { Type: TokenType.Dot }:
                    return Expression.Constant(".");
                case ValueOperationValuePosToken { Operation: OperationEnum.Divide }:
                    return Expression.Constant("/");
                case ValueOperationValuePosToken { Operation: OperationEnum.Multiply }:
                    return Expression.Constant("*");
                case GroupValuePosToken groupToken:
                    return GetMathExpression(groupToken.Tokens);
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
                    // todo(perf): cache
                    return Expression.Call(null,
                        typeof(ClassOrTypeResolver).GetMethod(nameof(ClassOrTypeResolver.ResolveForUse))!,
                        Expression.Constant(externThing.String), Expression.Constant(parsed.FileScope));
                }
                case CallLikePosToken callToken:
                {
                    return MethodCall(callToken.Name, callToken: callToken);
                }
                case KeywordToken keywordToken:
                    return Expression.Constant(keywordToken.String);
                case TokenGroupPosToken tokenGroup:
                    return GetHighExpression(tokenGroup.Tokens);
            }

            throw new Exception($"Single expression {token.Type} not implemented");
        }

        Expression GetMathExpression(ReadOnlySpan<PosToken> tokens)
        {
            if (tokens is [GroupValuePosToken mathToken])
                return GetMathExpression(mathToken.Tokens);
            var exp = GetSingleExpression(tokens[0]);
            for (var i = 1; i < tokens.Length; i++)
            {
                var opToken = (ValueOperationValuePosToken)tokens[i];

                switch (opToken.Operation)
                {
                    default:
                    {
                        exp = Expression.Dynamic(BinderUtil.GetBinaryOperationBinder(opToken.Operation), typeof(object),
                            exp,
                            GetSingleExpression(tokens[++i]));
                        break;
                    }
                }
            }

            return exp;
        }

        Expression GetComparisonExpression(ComparisonValuePosToken comparisonToken)
            => Expression.Convert(Expression.Dynamic(
                BinderUtil.GetComparisonOperationBinder(comparisonToken.ComparisonToken.Operation), typeof(object),
                GetSingleExpression(comparisonToken.Left),
                GetSingleExpression(comparisonToken.Right)), typeof(bool));

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
                    throw new($"GetLogicalExpression for {logicalToken.ComparisonToken.Type} not implemented");
            }
        }

        Expression GetHighExpression(ArraySegment<PosToken> tokens)
            => tokens.Count switch
            {
                > 0 when tokens[0] is KeywordToken { IsExecutable: true } keywordToken => MethodCall(
                    keywordToken.String,
                    argumentTokens: tokens.Segment(1..)),
                1 => GetSingleExpression(tokens[0]),
                > 2 => GetMathExpression(tokens),
                _ => throw new Exception($"Invalid tokens for {nameof(GetHighExpression)}")
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
                [VariableToken variable] => GetVariable(variable.Name).EnsureIsType(typeof(bool)),
                _ => throw new Exception($"Invalid tokens for {nameof(GetBoolExpression)}")
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
                    value = MethodCall(callToken.Name, callToken: callToken, target: value);
                }
                else if (t is KeywordToken keywordToken)
                {
                    value = DynamicExpression.Dynamic(
                        new RCaronGetMemberBinder(keywordToken.String, true, compiledContext),
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
                }
                else
                {
                    throw new Exception($"invalid dot group token: {t.Type}");
                }
            }

            return value;
        }

        Expression MethodCall(string name, ArraySegment<PosToken> argumentTokens = default,
            CallLikePosToken? callToken = null, Expression? target = null)
        {
            if (callToken != null && target == null)
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
                                compiledContext), /* todo: wtf can't do typeof(string)*/ typeof(object),
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
                    case "range":
                    {
                        var first = GetHighExpression(callToken.Arguments[0]).EnsureIsType(typeof(long));
                        var second = GetHighExpression(callToken.Arguments[1]).EnsureIsType(typeof(long));
                        return Expression.New(
                            typeof(RCaronRange).GetConstructor(new[] { typeof(long), typeof(long) })!,
                            first, second);
                    }
                }
            }

            var enumerator = callToken == null
                ? new ArgumentEnumerator(argumentTokens)
                : new ArgumentEnumerator(callToken);
            if (target == null)
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
                    case "dbg_println" when fakedMotor?.Options.EnableDebugging ?? false:
                    {
                        var expressions = new List<Expression>();
                        while (enumerator.MoveNext())
                        {
                            expressions.Add(Expression.Call(log73Debug.Value,
                                GetHighExpression(enumerator.CurrentTokens).EnsureIsType(typeof(object))));
                        }

                        return Expression.Block(expressions);
                    }
                    case "dbg_assert_is_one" when fakedMotor?.Options.EnableDebugging ?? false:
                    {
                        return AssignGlobal("$$assertResult", Expression.Equal(
                            GetHighExpression(argumentTokens).EnsureIsType(typeof(long)),
                            Expression.Constant(1L)));
                    }
                    case "dbg_sum_three" when fakedMotor?.Options.EnableDebugging ?? false:
                    {
                        return AssignGlobal("$$assertResult",
                            Expression.Add(
                                Expression.Add(GetSingleExpression(argumentTokens[0]).EnsureIsType(typeof(long)),
                                    GetSingleExpression(argumentTokens[1]).EnsureIsType(typeof(long))
                                        .EnsureIsType(typeof(long))),
                                GetSingleExpression(argumentTokens[2]).EnsureIsType(typeof(long))));
                    }
                }

            var args = new List<Expression>();
            var argNames = new List<string>();
            while (enumerator.MoveNext())
            {
                var exp = GetHighExpression(enumerator.CurrentTokens);
                args.Add(exp);
                if (enumerator.CurrentName != null)
                    argNames.Add(enumerator.CurrentName);
                else if (enumerator.HitNamedArgument)
                    throw RCaronException.PositionalArgumentAfterNamedArgument();
            }

            var callInfo = new CallInfo(args.Count, argNames.ToArray());
            if (target != null)
                return Expression.Dynamic(new RCaronInvokeMemberBinder(name, true, callInfo, compiledContext),
                    typeof(object), args.Prepend(target));

            IEnumerable<Expression> argsOver = args;

            // get inside class to the context
            ClassDefinition? insideClass = null;
            for (var i = contextStack.Count - 1; i >= 0; i--)
            {
                var el = contextStack.At(i);
                if (el is FunctionContext { ClassDefinition: { } classDefinition } functionContext)
                {
                    insideClass = classDefinition;
                    argsOver = argsOver.Prepend(functionContext.ArgumentsInner![0]);
                    break;
                }

                if (el.ReturnWorthy)
                    break;
            }

            var ensureSameCallWhateveThe = new object();
            argsOver = argsOver.Prepend(Expression.Constant(ensureSameCallWhateveThe));

            return Expression.Dynamic(
                new RCaronOtherBinder(compiledContext, name,
                    callInfo, ensureSameCallWhateveThe, insideClass), typeof(object), argsOver);
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
            Expression GetOrNewVariable(string name, bool mustBeAtCurrent = false, Type? specificType = null)
            {
                var varExp = GetVariableNullable(name, mustBeAtCurrent);
                if (varExp == null)
                {
                    var peak = contextStack.Peek();
                    var @var = Expression.Variable(specificType ?? typeof(object), name);
                    varExp = @var;
                    peak.Variables ??= _newVariableDict();
                    peak.Variables.Add(name, @var);
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
                    if (varExp.Type != typeof(object) && varExp.Type != right.Type)
                        right = Expression.Condition(Expression.TypeIs(right, varExp.Type),
                            right.EnsureIsType(varExp.Type),
                            Expression.Throw(Expression.Call(
                                typeof(RCaronException).GetMethod(nameof(RCaronException.LetVariableTypeMismatch))!,
                                Expression.Constant(vt.Name),
                                Expression.Constant(varExp.Type), Expression.Constant(right.Type)), varExp.Type));
                    else
                        right = right.EnsureIsType(varExp.Type);
                    expressions.Add(Expression.Assign(varExp, right));

                    IfFakedAssignVariable(vt.Name, varExp, expressions);

                    break;
                }
                case TokenLine { Type: LineType.LetVariableAssignment } tokenLine:
                {
                    var right = GetHighExpression(tokenLine.Tokens.Segment(3..));
                    var vt = (VariableToken)tokenLine.Tokens[1];
                    var varExp = GetOrNewVariable(vt.Name, specificType: right.Type);
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

                            Assert(c.ReturnLabel != null);
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
                    var elseState = GetOrNewVariable("$$elseState", mustBeAtCurrent: true, specificType: typeof(bool));
                    expressions.Add(Expression.Assign(elseState, Expression.Constant(false)));
                    expressions.Add(Expression.IfThen(GetBoolExpression(callToken.Arguments[0]),
                        Expression.Block(
                            Expression.Assign(elseState, Expression.Constant(true)),
                            DoLines(codeBlockToken.Lines))));
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
                    bool TryGetFrom(int index, LineType what, out CodeBlockToken? cbt1)
                    {
                        var l = lines[index];
                        if (l is TokenLine tokenLine1 && tokenLine1.Type == what &&
                            tokenLine1.Tokens[1] is CodeBlockToken cbt2)
                        {
                            cbt1 = cbt2;
                            return true;
                        }

                        cbt1 = null;
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
                            expressions.Add(Expression.Assign(varExp,
                                Expression.Dynamic(Binder.BinaryOperation(CSharpBinderFlags.None, ExpressionType.Add,
                                    null, new[]
                                    {
                                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
                                    }), typeof(object), varExp, Expression.Constant(1L))));
                            break;
                        case OperationPosToken { Operation: OperationEnum.UnaryDecrement }:
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

                    BlockExpression loopBody;
                    {
                        var expr1 = Expression.IfThen(
                            Expression.NotEqual(Expression.Constant(true),
                                GetBoolExpression(forLoopLine.CallToken.Arguments[1])),
                            Expression.Break(@break));
                        var expr2 = DoLines(forLoopLine.Body.Lines);
                        if (forLoopLine.Iterator is not null)
                        {
                            var expr3 = DoLines(new[] { forLoopLine.Iterator });
                            loopBody = Expression.Block(expr1, expr2, expr3);
                        }
                        else
                            loopBody = Expression.Block(expr1, expr2);
                    }

                    var loop = Expression.Loop(loopBody, @break, @continue);
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
                    var evaluated = Expression.Variable(typeof(object));
                    var enumerator = Expression.Variable(typeof(IEnumerator));
                    // todo: not sure if this should be in loopContext, but it doesn't work when I add it there so here it is
                    var currentContext = contextStack.Peek();
                    currentContext.Variables ??= _newVariableDict();
                    currentContext.Variables.Add("\u0159enumerator", enumerator);
                    currentContext.Variables.Add("\u0159evaluated", evaluated);
                    expressions.Add(
                        Expression.Assign(evaluated, GetHighExpression(callToken.Arguments[0].Segment(2..))));
                    expressions.Add(
                        Expression.IfThenElse(Expression.TypeIs(evaluated, typeof(IEnumerator)),
                            Expression.Assign(enumerator, Expression.Convert(evaluated, typeof(IEnumerator))),
                            Expression.Assign(enumerator, Expression.Call(
                                Expression.Dynamic(Binder.Convert(CSharpBinderFlags.None, typeof(IEnumerable), null),
                                    typeof(IEnumerable), evaluated),
                                "GetEnumerator", Array.Empty<Type>())
                            ))
                    );
                    var @break = Expression.Label();
                    var @continue = Expression.Label();
                    var loopContext = new LoopContext() { BreakLabel = @break, ContinueLabel = @continue };
                    contextStack.Push(loopContext);
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
                        Expression.TryFinally(Expression.Block(
                            Expression.IfThen(Expression.NotEqual(Expression.Constant(true),
                                Expression.Call(enumerator, "MoveNext", null)), Expression.Break(@break)),
                            loopBody
                        ), Expression.IfThen(Expression.TypeIs(enumerator, typeof(IDisposable)),
                            Expression.Call(Expression.Convert(enumerator, typeof(IDisposable)), "Dispose", null))),
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
                case null:
                    throw new Exception("line is null");
                default:
                    throw new Exception($"line type {line.Type} is not implemented");
            }
        }

        BlockExpression DoLines(IList<Line> lines, bool returnWorthy = false, bool useCurrent = false,
            List<Expression>? prependLines = null, Context? theCurrentToUse = null)
        {
            var isRoot = contextStack.Count == 0;
            Context? c;
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
            if (isRoot && fakedMotor is { GlobalScope.Variables.Count: > 0 } && c != null)
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

        CompiledFunction DoFunction(Function function, ClassDefinition? insideClassDefinition = null,
            ClassDefinition? insideStaticClassDefinition = null)
        {
            ParameterExpression[]? argumentsInner = null;
            if (function.Arguments != null || insideClassDefinition != null)
            {
                argumentsInner =
                    new ParameterExpression[0 + (function.Arguments?.Length ?? 0) +
                                            (insideClassDefinition != null ? 1 : 0)];
                if (insideClassDefinition != null)
                    argumentsInner[0] = Expression.Parameter(typeof(ClassInstance), "this");
                if (function.Arguments != null)
                    for (var i = insideClassDefinition != null ? 1 : 0;
                         i - (insideClassDefinition != null ? 1 : 0) < function.Arguments.Length;
                         i++)
                    {
                        argumentsInner[i] = Expression.Parameter(typeof(object),
                            function.Arguments[i - (insideClassDefinition != null ? 1 : 0)].Name);
                    }
            }

            var c = new FunctionContext()
            {
                ArgumentsInner = argumentsInner, ClassDefinition = insideClassDefinition, ReturnWorthy = true,
                ReturnLabel = Expression.Label(typeof(object)),
                InsideStaticClassDefinition = insideStaticClassDefinition,
            };
            contextStack.Push(c);
            var body = DoLines(function.CodeBlock.Lines, useCurrent: true, theCurrentToUse: c);
            Assert(contextStack.Pop() == c);
            return new CompiledFunction()
            {
                Arguments = argumentsInner,
                Body = body,
                OriginalFunction = function
            };
        }

        // do functions
        // todo(perf): could parallelize this in the future
        if (parsed.FileScope.Functions != null)
            foreach (var function in parsed.FileScope.Functions)
                functions[function.Key] = DoFunction(function.Value);

        // do classes
        if (parsed.FileScope.ClassDefinitions != null)
            foreach (var definition in parsed.FileScope.ClassDefinitions)
            {
                Expression?[]? initializers = null;
                Dictionary<string, CompiledFunction>? classFunctions = null;
                Dictionary<string, CompiledFunction>? classStaticFunctions = null;
                // do property initializers
                if (definition.PropertyInitializers is not null or { Length: 0 })
                {
                    initializers = new Expression?[definition.PropertyInitializers.Length];
                    var i = 0;
                    foreach (var initializer in definition.PropertyInitializers)
                    {
                        if (initializer is null)
                            continue;
                        initializers[i++] = GetHighExpression(initializer);
                    }
                }

                // do functions
                if (definition.Functions is not null or { Count: 0 })
                {
                    classFunctions =
                        new Dictionary<string, CompiledFunction>(StringComparer.InvariantCultureIgnoreCase);
                    foreach (var function in definition.Functions)
                    {
                        var compiledFunction = DoFunction(function.Value, definition);
                        classFunctions[function.Key] = compiledFunction;
                    }
                }

                // do static functions
                if (definition.StaticFunctions is not null or { Count: 0 })
                {
                    classStaticFunctions ??=
                        new Dictionary<string, CompiledFunction>(StringComparer.InvariantCultureIgnoreCase);
                    foreach (var function in definition.StaticFunctions)
                    {
                        var compiledFunction = DoFunction(function.Value, insideStaticClassDefinition: definition);
                        classStaticFunctions[function.Key] = compiledFunction;
                    }
                }

                classes.Add(new CompiledClass
                {
                    Definition = definition, PropertyInitializers = initializers, Functions = classFunctions,
                    StaticFunctions = classStaticFunctions
                });
            }

        // do main
        var block = DoLines(parsed.FileScope.Lines);

        return (block, compiledContext);
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
        public required LabelTarget BreakLabel { get; init; }
        public required LabelTarget ContinueLabel { get; init; }
    }

    private class FunctionContext : Context
    {
        public ParameterExpression[]? ArgumentsInner { get; init; } = null;
        public ClassDefinition? ClassDefinition { get; init; } = null;
        public ClassDefinition? InsideStaticClassDefinition { get; init; } = null;
    }

    public static void Assert([DoesNotReturnIf(false)] bool condition)
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

    [UsedImplicitly]
    public object Invoke(params object?[]? args)
    {
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

public class CompiledClass
{
    public required ClassDefinition Definition { get; init; }
    public Expression?[]? PropertyInitializers { get; init; }
    public Dictionary<string, CompiledFunction>? Functions { get; init; }
    public Dictionary<string, CompiledFunction>? StaticFunctions { get; init; }
}

public record CompiledContext(Dictionary<string, CompiledFunction> Functions, FileScope FileScope,
    ConstantExpression FakedMotorConstant, IList<CompiledClass> Classes, Motor? FakedMotor,
    List<object> OverrideModules)
{
    public List<CompiledContext>? ImportedContexts { get; set; }
    public Dictionary<string, CompiledFunction>? ImportedFunctions { get; set; }
    public List<CompiledClass>? ImportedClasses { get; set; }
    public ConstantExpression CompiledContextConstant { get; set; } = null!;

    public CompiledClass? GetClass(ClassDefinition definition, bool includeImported = true)
    {
        for (var i = 0; i < Classes.Count; i++)
        {
            var compiledClass = Classes[i];
            if (compiledClass.Definition == definition)
                return compiledClass;
        }

        if (includeImported)
        {
            if (ImportedContexts is not null or { Count: 0 })
                foreach (var importedContext in ImportedContexts)
                {
                    var compiledClass = importedContext.GetClass(definition, false);
                    if (compiledClass != null)
                        return compiledClass;
                }

            if (ImportedClasses is not null or { Count: 0 })
                foreach (var importedClass in ImportedClasses)
                {
                    if (importedClass.Definition == definition)
                        return importedClass;
                }
        }

        return null;
    }

    public CompiledFunction? GetFunction(string name, bool includeImported = true)
    {
        if (Functions.TryGetValue(name, out var function))
            return function;

        if (includeImported)
        {
            if (ImportedContexts is not null or { Count: 0 })
                foreach (var importedContext in ImportedContexts)
                {
                    var function2 = importedContext.GetFunction(name, false);
                    if (function2 != null)
                        return function2;
                }

            if (ImportedFunctions is not null or { Count: 0 })
                if (ImportedFunctions.TryGetValue(name, out var function3))
                    return function3;
        }

        return null;
    }
}