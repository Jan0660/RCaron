using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Dynamitey;
using JetBrains.Annotations;
using Log73;
using Console = Log73.Console;

namespace RCaron;

public enum RCaronInsideEnum: byte
{
    VariableNotFound
}

public class MotorOptions
{
    public bool EnableDebugging { get; set; } = false;
    public bool EnableDumb { get; set; } = false;
}

public class Motor
{
    public string Raw { get; set; }
    public IList<Line> Lines { get; set; }

    // public record StackThing(int LineIndex, bool IsBreakWorthy, bool IsReturnWorthy,
    //     Conditional? Conditional, int PreviousLineIndex, LocalScope? Scope);
    public class StackThing
    {
        public StackThing(int lineIndex, bool isBreakWorthy, bool isReturnWorthy,
            Conditional? conditional, int previousLineIndex, LocalScope? scope)
        {
            this.LineIndex = lineIndex;
            this.IsBreakWorthy = isBreakWorthy;
            this.IsReturnWorthy = isReturnWorthy;
            this.Conditional = conditional;
            this.PreviousLineIndex = previousLineIndex;
            this.Scope = scope;
        }

        public int LineIndex { get; init; }
        public bool IsBreakWorthy { get; init; }
        public bool IsReturnWorthy { get; init; }
        public Conditional? Conditional { get; init; }
        public int PreviousLineIndex { get; init; }
        public LocalScope? Scope { get; set; }
    }

    public Stack<StackThing>
        // public Stack<(int LineIndex, int BlockDepth, int BlockNumber, bool IsBreakWorthy, Conditional Conditional)>
        BlockStack { get; set; } = new();

    public Conditional? LastConditional { get; set; }
    public MotorOptions Options { get; }
    public Dictionary<string, (int startLineIndex, int endLineIndex)> Functions { get; set; } = new();
    public List<string>? OpenNamespaces { get; set; }
    public List<string>? OpenNamespacesForExtensionMethods { get; set; }
    public LocalScope GlobalScope { get; set; } = new();

    public class Conditional
    {
        public Conditional(int lineIndex, bool isOnce, bool isTrue, bool isBreakWorthy, PosToken[]? evalTokens)
        {
            LineIndex = lineIndex;
            IsOnce = isOnce;
            IsTrue = isTrue;
            IsBreakWorthy = isBreakWorthy;
            EvaluateTokens = evalTokens;
        }

        public int LineIndex { get; set; }

        // is once run
        public bool IsOnce { get; set; }
        public bool IsTrue { get; set; }
        public bool IsBreakWorthy { get; set; }
        public PosToken[]? EvaluateTokens { get; set; }

        public virtual bool Evaluate(Motor m)
            // todo: handle null
            => m.SimpleEvaluateBool(EvaluateTokens);
    }

    public class ForLoopConditional : Conditional
    {
        public Line LastExecute { get; }

        public ForLoopConditional(int lineIndex, bool isOnce, bool isTrue, bool isBreakWorthy, PosToken[] evalTokens,
            Line lastExec) : base(lineIndex, isOnce, isTrue, isBreakWorthy, evalTokens)
        {
            LastExecute = lastExec;
        }

        public override bool Evaluate(Motor m)
        {
            m.RunLine(LastExecute);
            return m.SimpleEvaluateBool(EvaluateTokens!);
        }
    }

#pragma warning disable CS8618
    public Motor(RCaronRunnerContext runnerContext, MotorOptions? options = null)
#pragma warning restore CS8618
    {
        UseContext(runnerContext);
        Options = options ?? new();
    }

    public void UseContext(RCaronRunnerContext runnerContext)
    {
        Lines = runnerContext.Lines;
        Raw = runnerContext.Code;
    }

    /// <summary>
    /// current line index
    /// </summary>
    private int curIndex;

    public void Run()
    {
        for (curIndex = 0; curIndex < Lines.Count; curIndex++)
        {
            if (curIndex >= Lines.Count)
                break;
            var line = Lines[curIndex];
            RunLine(line);
        }
    }

    public void RunLine(Line line)
    {
        switch (line.Type)
        {
            case LineType.VariableAssignment:
            {
                var variableName = Raw[(line.Tokens[0].Position.Start + 1)..line.Tokens[0].Position.End];
                var obj = SimpleEvaluateExpressionHigh(line.Tokens.Segment(2..));
                SetVar(variableName, obj);
                // Console.Debug($"variable '{variableName}' set to '{obj}'");
                break;
            }
            case LineType.IfStatement when line.Tokens[0] is CallLikePosToken callToken:
            {
                LastConditional = new Conditional(lineIndex: curIndex, isOnce: true,
                    isTrue: SimpleEvaluateBool(callToken.Arguments[0]), isBreakWorthy: false, evalTokens: null);
                break;
            }
            case LineType.WhileLoop when line.Tokens[0] is CallLikePosToken callToken:
            {
                LastConditional = new Conditional(lineIndex: curIndex, isOnce: false,
                    isTrue: SimpleEvaluateBool(callToken.Arguments[0]), isBreakWorthy: true,
                    evalTokens: callToken.Arguments[0]);
                break;
            }
            case LineType.DoWhileLoop when line.Tokens[0] is CallLikePosToken callToken:
            {
                LastConditional = new Conditional(lineIndex: curIndex, isOnce: false,
                    isTrue: true, isBreakWorthy: true, evalTokens: callToken.Arguments[0]);
                break;
            }
            case LineType.UnaryOperation:
            {
                // todo: can probably do this better
                var variableName = Raw[(line.Tokens[0].Position.Start + 1)..line.Tokens[0].Position.End];
                if (line.Tokens[1].EqualsString(Raw, "++"))
                {
                    Horrors.AddTo(ref GetVarRef(variableName)!, (long)1);
                }
                else if (line.Tokens[1].EqualsString(Raw, "--"))
                    SetVar(variableName,
                        Horrors.Subtract(SimpleEvaluateExpressionSingle(line.Tokens[0]).NotNull(), (long)1));

                break;
            }
            case LineType.BlockStuff:
                if (line.Tokens[0] is BlockPosToken { Type: TokenType.BlockStart } bpt)
                {
                    if (LastConditional is { IsTrue: true })
                        BlockStack.Push(new StackThing(curIndex, LastConditional.IsBreakWorthy, false,
                            LastConditional, curIndex, null));
                    else
                    {
                        curIndex = ListEx.FindIndex(Lines,
                            l => l.Tokens[0] is BlockPosToken { Type: TokenType.BlockEnd } bpt2 &&
                                 bpt2.Depth == bpt.Depth && bpt2.Number == bpt.Number);
                        return;
                    }
                }
                else if (line.Tokens[0] is { Type: TokenType.BlockEnd })
                {
                    var curBlock = BlockStack.Peek();
                    if (curBlock.Conditional is { IsTrue: true, IsOnce: true })
                        BlockStack.Pop();
                    else if (curBlock.Conditional is { IsOnce: false })
                    {
                        if (curBlock.Conditional.EvaluateTokens == null)
                            curIndex = curBlock.LineIndex;
                        else
                        {
                            var evaluated = curBlock.Conditional.Evaluate(this);
                            curBlock.Conditional.IsTrue = evaluated;
                            if (evaluated)
                                curIndex = curBlock.LineIndex;
                            else
                                BlockStack.Pop();
                        }
                    }
                    else if (curBlock.Conditional == null)
                    {
                        BlockStack.Pop();
                        curIndex = curBlock.PreviousLineIndex;
                    }
                }

                break;
            case LineType.LoopLoop:
                LastConditional = new Conditional(lineIndex: curIndex, isOnce: false,
                    isTrue: true, isBreakWorthy: true, null);
                break;
            case LineType.ForLoop when line.Tokens[0] is CallLikePosToken callToken:
                var falseI = 0;
                RunLine(RCaronRunner.GetLine(callToken.Arguments[0], ref falseI, Raw));
                falseI = 0;
                LastConditional = new ForLoopConditional(lineIndex: curIndex, isOnce: false,
                    isTrue: true, isBreakWorthy: true, callToken.Arguments[1],
                    RCaronRunner.GetLine(callToken.Arguments[2], ref falseI, Raw));
                break;
            case LineType.Function:
                var start = (BlockPosToken)Lines[curIndex + 1].Tokens[0];
                var end = ListEx.IndexOf(Lines, (l) =>
                    l.Tokens[0] is BlockPosToken { Type: TokenType.BlockEnd } bp && bp.Number == start.Number);
                Functions[line.Tokens[1].ToString(Raw)] = (curIndex + 1, end);
                break;
            case LineType.KeywordCall when line.Tokens[0] is CallLikePosToken callToken:
            {
                MethodCall(callToken.GetName(Raw), callToken: callToken);
                break;
            }
            case LineType.KeywordPlainCall:
            {
                var keyword = line.Tokens[0];
                var keywordString = keyword.ToString(Raw);
                var args = line.Tokens.AsSpan()[1..];

                ArraySegment<PosToken> ArgsArray()
                    => line.Tokens.Segment(1..);

                switch (keywordString)
                {
                    case "break":
                    {
                        var g = BlockStack.Pop();
                        while (!g.IsBreakWorthy)
                            g = BlockStack.Pop();
                        curIndex = ListEx.FindIndex(Lines,
                            l => l.Type == LineType.BlockStuff
                                 && l.Tokens[0].Type == TokenType.BlockEnd) + 1;
                        return;
                    }
                    case "return":
                    {
                        var g = BlockStack.Pop();
                        while (!g.IsReturnWorthy)
                            g = BlockStack.Pop();
                        curIndex = g.PreviousLineIndex;
                        return;
                    }
                }

                if (Options.EnableDebugging)
                    switch (keywordString)
                    {
                        case "dbg_println":
                            Console.Debug(SimpleEvaluateExpressionHigh(ArgsArray()));
                            return;
                        case "dbg_assert_is_one":
                            GlobalScope.SetVariable("$$assertResult",
                                SimpleEvaluateExpressionSingle(args[0]).Expect<long>() == 1);
                            return;
                        case "dbg_sum_three":
                            GlobalScope.SetVariable("$$assertResult", Horrors.Sum(
                                Horrors.Sum(SimpleEvaluateExpressionSingle(args[0]).NotNull(),
                                    SimpleEvaluateExpressionSingle(args[1]).NotNull()), SimpleEvaluateExpressionSingle(args[2]).NotNull()));
                            return;
                    }

                if (Options.EnableDumb)
                    switch (keywordString)
                    {
                        case "goto_line":
                            curIndex = (int)SimpleEvaluateExpressionSingle(args[0]).Expect<long>();
                            return;
                    }

                if (Functions.TryGetValue(keywordString, out var func))
                {
                    BlockStack.Push(new StackThing(func.startLineIndex, false, true, null,
                        curIndex, null));
                    curIndex = func.startLineIndex;
                    return;
                }

                MethodCall(keywordString, line.Tokens.AsSpan()[1..]);
                break;
            }
            default:
                // wtf
                Debugger.Break();
                break;
        }
    }

    public object? MethodCall(string name, Span<PosToken> argumentTokens = default, CallLikePosToken? callToken = null,
        Span<PosToken> instanceTokens = default)
    {
        object? At(in Span<PosToken> tokens, int index)
        {
            if (callToken != null)
                return SimpleEvaluateExpressionHigh(callToken.Arguments[index]);
            return SimpleEvaluateExpressionSingle(tokens[index]);
        }

        object?[] All(in Span<PosToken> tokens)
        {
            if (tokens.Length == 0 && (callToken?.ArgumentsEmpty() ?? false))
                return Array.Empty<object>();
            if (callToken != null)
            {
                var res = new object?[callToken.Arguments.Length];
                for (var ind = 0; ind < callToken.Arguments.Length; ind++)
                    res[ind] = SimpleEvaluateExpressionHigh(callToken.Arguments[ind]);
                return res;
            }

            return EvaluateMultipleValues(tokens);
        }

        if (callToken?.OriginalToken.Type == TokenType.ArrayLiteralStart)
            return All(in argumentTokens);

        switch (name.ToLowerInvariant())
        {
            #region conversions

            case "string":
                return At(argumentTokens, 0).ToString()!;
            case "float":
                return Convert.ToSingle(At(argumentTokens, 0));
            case "int32":
                return Convert.ToInt32(At(argumentTokens, 0));
            case "int64":
                return Convert.ToInt64(At(argumentTokens, 0));

            #endregion

            case "sum":
                return Horrors.Sum(At(argumentTokens, 0).NotNull(), At(argumentTokens, 1).NotNull());
            case "globalget":
            {
                var val = GlobalScope.GetVariable(At(argumentTokens, 0).Expect<string>());
                if (val.Equals(RCaronInsideEnum.VariableNotFound))
                    throw RCaronException.VariableNotFound(name);
                return val;
            }
            case "globalset":
            {
                ref var val = ref GlobalScope.GetVariableRef(At(argumentTokens, 0).Expect<string>());
                if (Unsafe.IsNullRef(ref val))
                    throw RCaronException.VariableNotFound(name);
                val = At(argumentTokens, 1);
                return null;
            }
            case "printfunny":
            case "println":
                foreach (var arg in All(argumentTokens))
                    Console.WriteLine(arg);
                return null;
            case "print":
            {
                var args = All(argumentTokens);
                for (var i = 0; i < args.Length; i++)
                {
                    if (i != 0)
                        Console.Out.Write(' ');
                    Console.Out.Write(args[i]);
                }

                Console.Out.WriteLine();
                return null;
            }
            case "open":
                OpenNamespaces ??= new();
                OpenNamespaces.AddRange(Array.ConvertAll(All(argumentTokens), t => t.Expect<string>()));
                return null;
            case "open_ext":
                OpenNamespacesForExtensionMethods ??= new();
                OpenNamespacesForExtensionMethods.AddRange(Array.ConvertAll(All(argumentTokens), t => t.Expect<string>()));
                return null;
        }

        if (name[0] == '#' || instanceTokens.Length != 0)
        {
            var args = All(in argumentTokens);
            Type? type;
            string methodName;
            object? target = null;
            object? variable = null;
            if (instanceTokens.Length != 0 && instanceTokens[0].Type != TokenType.ExternThing)
            {
                Span<PosToken> given = instanceTokens;
                if (instanceTokens[0] is DotGroupPosToken dotGroupPosToken)
                {
                    given = dotGroupPosToken.Tokens[..^2];
                    instanceTokens = dotGroupPosToken.Tokens;
                }

                var obj = EvaluateDotThings(given);
                variable = obj;
                target = obj;
                type = obj.GetType();
                // var i = name.Length - 1;
                // while (name[i] != '.')
                //     i--;
                // i++;
                // todo(perf): just steal it from name var? -- fix name var first lol maybe
                methodName = instanceTokens[^1].ToString(Raw);
                goto resolveMethod;
            }

            // todo(perf)
            // var partsCount = 1 + name.Count(c => c == '.');
            var parts = name.Split('.');
            var d = string.Join('.', parts[..^1])[1..];
            type = TypeResolver.FindType(d, usedNamespaces: OpenNamespaces);
            // Type.GetType(d, false, true);
            // if (type == null && OpenNamespaces != null)
            //     foreach (var ns in OpenNamespaces)
            //     {
            //         type = Type.GetType(ns + '.' + d, false, true);
            //         if (type != null)
            //             break;
            //     }

            if (type == null)
                throw new RCaronException($"cannot find type '{d}' for external method call",
                    RCaronExceptionTime.Runtime);

            methodName = parts[^1];
            resolveMethod: ;
            var methods = type.GetMethods()
                .Where(m => m.Name.Equals(methodName, StringComparison.InvariantCultureIgnoreCase)).ToArray();
            if (methods.Length == 0)
            {
                args = args.Prepend(variable!).ToArray();
                var extensionMethods = new List<MethodInfo>();
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                // Type? endingMatch =  null;
                foreach (var ass in assemblies)
                {
                    foreach (var exportedType in ass.GetTypes())
                    {
                        if (!(exportedType.IsSealed && exportedType.IsAbstract) || !exportedType.IsPublic)
                            continue;
                        // if (type.FullName?.EndsWith(name, StringComparison.InvariantCultureIgnoreCase) ?? false)
                        // {
                        //     endingMatch = type;
                        // }
                        // exact match
                        foreach (var method in exportedType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        {
                            if (method.Name.Equals(methodName, StringComparison.InvariantCultureIgnoreCase) &&
                                (OpenNamespacesForExtensionMethods?.Contains(exportedType.Namespace!) ?? false))
                            {
                                extensionMethods.Add(method);
                            }
                        }
                    }
                }

                methods = extensionMethods.ToArray();
            }

            Span<uint> scores = stackalloc uint[methods.Length];
            for (var i = 0; i < methods.Length; i++)
            {
                uint score = 0;
                var method = methods[i];
                var parameters = method.GetParameters();
                if (parameters.Length == 0 && args.Length == 0)
                {
                    score = uint.MaxValue;
                }

                for (var j = 0; j < parameters.Length && j < args.Length; j++)
                {
                    if (parameters[j].ParameterType == args[j].GetType())
                    {
                        score += 100;
                    }
                    else if (parameters[j].ParameterType.IsInstanceOfType(args[j]))
                    {
                        score += 10;
                    }
                    else if (parameters[j].ParameterType.IsGenericType &&
                             ListEx.IsAssignableToGenericType(args[j].GetType(),
                                 parameters[j].ParameterType.GetGenericTypeDefinition()))
                    {
                        score += 10;
                    }
                    else
                    {
                        score = 0;
                        break;
                    }
                }

                scores[i] = score;
            }

            var g = 0;
            uint best = 0;
            var bestIndex = 0;
            for (; g < scores.Length; g++)
            {
                if (scores[g] > best)
                {
                    best = scores[g];
                    bestIndex = g;
                }
            }


            if (best == 0)
                throw new RCaronException($"cannot find a match for method '{methodName}'",
                    RCaronExceptionTime.Runtime);

            // mismatch count arguments -> equate it out with default values
            // is equate even a word?
            var paramss = methods[bestIndex].GetParameters();
            if (paramss.Length > args.Length)
            {
                var argsNew = new object[paramss.Length];
                Array.Copy(args, argsNew, args.Length);
                for (var i = args.Length; i < argsNew.Length; i++)
                {
                    argsNew[i] = paramss[i].DefaultValue!;
                }

                args = argsNew;
            }

            // generic method handling aaa
            if (methods[bestIndex].IsGenericMethod)
            {
                // todo(perf): probably not the fastest
                var t = methods[bestIndex].DeclaringType!;
                // static class
                if (t.IsSealed && t.IsAbstract)
                {
                    var staticContext = InvokeContext.CreateStatic;
                    return Dynamic.InvokeMember(staticContext(t), methods[bestIndex].Name, args);
                }
                else
                {
                    return Dynamic.InvokeMember(target, methods[bestIndex].Name, args);
                }
            }

            return methods[bestIndex].Invoke(target, args);
        }

        throw new RCaronException($"method '{name}' is invalid", RCaronExceptionTime.Runtime);
    }

    public object? EvaluateDotThings(Span<PosToken> instanceTokens)
    {
        if (instanceTokens.Length == 1 && instanceTokens[0] is DotGroupPosToken dotGroupPosToken)
            instanceTokens = dotGroupPosToken.Tokens;
        var val = SimpleEvaluateExpressionSingle(instanceTokens[0]);
        if (val == null)
            throw RCaronException.NullInTokens(instanceTokens, Raw, 0);
        var type = val.GetType();
        for (int i = 2; i < instanceTokens.Length; i++)
        {
            if (instanceTokens[i].Type == TokenType.Dot)
                continue;
            if(i != instanceTokens.Length && val == null)
                throw RCaronException.NullInTokens(instanceTokens, Raw, i);
            var str = instanceTokens[i].ToString(Raw);
            var p = type!.GetProperty(str,
                BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Instance);
            if (p != null)
            {
                val = p.GetValue(val);
                type = val?.GetType();
                continue;
            }

            var f = type.GetField(str, BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Instance);
            if (f != null)
            {
                val = f.GetValue(val);
                type = val?.GetType();
                continue;
            }

            if (val is IDictionary dictionary)
            {
                var args = type.GetGenericArguments();
                var keyType = args[0];
                val = dictionary[Convert.ChangeType(str, keyType)];
                type = val?.GetType();
                continue;
            }

            var partIsInt = int.TryParse(str, out var partIntValue);
            if (val is Array array && partIsInt)
            {
                val = array.GetValue(partIntValue);
                type = val?.GetType();
                continue;
            }

            if (val is IList list && partIsInt)
            {
                val = list[partIntValue];
                type = val?.GetType();
                continue;
            }

            throw new RCaronException(
                $"cannot resolve '{str}'(index={i}) in '{Raw[instanceTokens[0].Position.Start..instanceTokens[^1].Position.End]}'",
                RCaronExceptionTime.Runtime);
        }

        return val;
    }

    public object?[] EvaluateMultipleValues(in Span<PosToken> tokens, int tokensStartIndex = 0)
    {
        var objs = new object?[tokens.Length - tokensStartIndex];
        for (var ind = tokensStartIndex; ind < tokens.Length; ind++)
            objs[ind - tokensStartIndex] = SimpleEvaluateExpressionSingle(tokens[ind]);
        return objs;
    }

    public object? EvaluateVariable(string name)
    {
        switch (name)
        {
            case "true":
                return true;
            case "false":
                return false;
            case "null":
                return null;
        }

        var val = GetVar(name);
        if (val?.Equals(RCaronInsideEnum.VariableNotFound) ?? false)
            throw RCaronException.VariableNotFound(name);
        return val;
    }

    public object? SimpleEvaluateExpressionSingle(PosToken token)
    {
        switch (token.Type)
        {
            case TokenType.VariableIdentifier:
                var name = token.ToString(Raw)[1..];
                return EvaluateVariable(name);
            case TokenType.Number:
                return Int64.Parse(token.ToSpan(Raw));
            case TokenType.DecimalNumber:
                return Decimal.Parse(token.ToSpan(Raw));
            case TokenType.String:
                var s = token.ToSpan(Raw)[1..^1];
                Span<char> g = stackalloc char[s.Length];
                var str = new SpanStringBuilder(ref g);
                for (var i = 0; i < s.Length; i++)
                {
                    var ch = s[i];
                    if (ch == '\\')
                    {
                        str.Append(s[++i]);
                        continue;
                    }

                    str.Append(s[i]);
                }

                return str.ToString();
            case TokenType.DumbShit when token is ValueGroupPosToken valueGroupPosToken:
                return SimpleEvaluateExpressionValue(valueGroupPosToken.ValueTokens);
            case TokenType.KeywordCall when token is CallLikePosToken callToken:
                return MethodCall(callToken.GetName(Raw), callToken: callToken,
                    instanceTokens: MemoryMarshal.CreateSpan(ref callToken.OriginalToken, 1));
            case TokenType.Keyword:
                return token.ToString(Raw);
            case TokenType.DotGroup:
                return EvaluateDotThings(MemoryMarshal.CreateSpan(ref token, 1));
        }

        throw new Exception("yo wtf");
    }

    [CollectionAccess(CollectionAccessType.Read)]
    public object SimpleEvaluateExpressionValue(ArraySegment<PosToken> tokens)
    {
        // repeat action something math
        var index = 0;
        object value = SimpleEvaluateExpressionSingle(tokens[0])!;
        while (index < tokens.Count - 1)
        {
            var op = tokens[index + 1].ToString(Raw);
            var second = SimpleEvaluateExpressionSingle(tokens[index + 2])!;
            switch (op)
            {
                case Operations.SumOp:
                    Horrors.AddTo(ref value, second);
                    break;
                case Operations.SubtractOp:
                    value = Horrors.Subtract(value, second);
                    break;
                case Operations.MultiplyOp:
                    value = Horrors.Multiply(value, second);
                    break;
                case Operations.DivideOp:
                    value = Horrors.Divide(value, second);
                    break;
                case Operations.ModuloOp:
                    value = Horrors.Modulo(value, second);
                    break;
            }

            index += 2;
        }

        return value;
    }

    public object? SimpleEvaluateExpressionHigh(ArraySegment<PosToken> tokens)
        => tokens.Count switch
        {
            1 => SimpleEvaluateExpressionSingle(tokens[0]),
            > 2 => SimpleEvaluateExpressionValue(tokens),
            _ => throw new Exception("what he fuck")
        };

    public bool SimpleEvaluateBool(PosToken[] tokens)
    {
        var val1 = SimpleEvaluateExpressionSingle(tokens[0])!;
        if (tokens.Length == 1)
        {
            if (val1 is bool b)
                return b;
        }

        // todo: cant switch case with a Span yet -- rider doesnt support
        var op = tokens[1].ToString(Raw);
        var val2 = SimpleEvaluateExpressionSingle(tokens[2])!;
        switch (op)
        {
            case Operations.IsEqualOp:
                return val1.Equals(val2);
            case Operations.IsNotEqualOp:
                return !val1.Equals(val2);
            case Operations.IsGreaterOp:
                return Horrors.IsGreater(val1, val2);
            case Operations.IsGreaterOrEqualOp:
                return val1.Equals(val2) || Horrors.IsGreater(val1, val2);
            case Operations.IsLessOp:
                return !val1.Equals(val2) && Horrors.IsLess(val1, val2);
            case Operations.IsLessOrEqualOp:
                return val1.Equals(val2) || Horrors.IsLess(val1, val2);
            default:
                throw new RCaronException($"unknown operator: {op}", RCaronExceptionTime.Runtime);
        }
    }

    public object? GetVar(string name)
    {
        for (var i = 0; i < BlockStack.Count; i++)
        {
            var el = BlockStack.ElementAt(^(i+1));
            if (el.Scope != null && el.Scope.TryGetVariable(name, out var value))
            {
                return value;
            }

            if (el.IsReturnWorthy)
                return RCaronInsideEnum.VariableNotFound;
        }

        if (GlobalScope.TryGetVariable(name, out var value2))
            return value2;
        return RCaronInsideEnum.VariableNotFound;
    }

    public ref object? GetVarRef(string name)
    {
        for (var i = 0; i < BlockStack.Count; i++)
        {
            var el = BlockStack.ElementAt(^(i+1));
            // todo: attempt at making a TryGetVariableRef method -- apply it in SetVar too
            if (el.Scope != null && el.Scope.VariableExists(name))
            {
                return ref el.Scope.GetVariableRef(name);
            }

            if (el.IsReturnWorthy)
                return ref Unsafe.NullRef<object?>();
        }


        if (GlobalScope.VariableExists(name))
            return ref GlobalScope.GetVariableRef(name);
        return ref Unsafe.NullRef<object?>();
    }

    public void SetVar(string name, object? value)
    {
        var hasReturnWorthy = false;
        for (var i = 0; i < BlockStack.Count; i++)
        {
            var el = BlockStack.ElementAt(^(i+1));
            if (el.Scope != null && el.Scope.VariableExists(name))
            {
                el.Scope.SetVariable(name, value);
                return;
            }

            if (el.IsReturnWorthy)
            {
                hasReturnWorthy = true;
                break;
            }
        }

        if (BlockStack.Count == 0 || (!hasReturnWorthy && GlobalScope.VariableExists(name)))
        {
            GlobalScope.SetVariable(name, value);
            return;
        }

        var currentStack = BlockStack.Peek();
        currentStack.Scope ??= new();
        currentStack.Scope.SetVariable(name, value);
    }
}