using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using JetBrains.Annotations;
using Console = Log73.Console;

namespace RCaron;

public class MotorOptions
{
    public bool EnableDebugging { get; set; } = false;
    public bool EnableDumb { get; set; } = false;
}

public class Motor
{
    public string Raw { get; set; }
    public Line[] Lines { get; set; }
    public Dictionary<string, object> Variables { get; set; } = new();

    public record StackThing(int LineIndex, bool IsBreakWorthy, bool IsReturnWorthy,
        Conditional? Conditional, int PreviousLineIndex);

    public Stack<StackThing>
        // public Stack<(int LineIndex, int BlockDepth, int BlockNumber, bool IsBreakWorthy, Conditional Conditional)>
        BlockStack { get; set; } = new();

    public Conditional? LastConditional { get; set; }
    public MotorOptions Options { get; }
    public Dictionary<string, (int startLineIndex, int endLineIndex)> Functions { get; set; } = new();

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

        public ForLoopConditional(int lineIndex, bool isOnce, bool isTrue, bool isBreakWorthy, PosToken[]? evalTokens,
            Line lastExec) : base(lineIndex, isOnce, isTrue, isBreakWorthy, evalTokens)
        {
            LastExecute = lastExec;
        }

        public override bool Evaluate(Motor m)
        {
            m.RunLine(LastExecute);
            return m.SimpleEvaluateBool(EvaluateTokens);
        }
    }

    public Motor(RCaronRunnerContext runnerContext, MotorOptions? options = null)
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
        for (curIndex = 0; curIndex < Lines.Length; curIndex++)
        {
            if (curIndex >= Lines.Length)
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
                var variableName = line.Tokens[0].ToString(Raw)[1..];
                var obj = SimpleEvaluateExpressionHigh(line.Tokens[2..]);
                Variables[variableName] = obj;
                Console.Debug($"variable '{variableName}' set to '{obj}'");
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
                var variableName = line.Tokens[0].ToString(Raw)[1..];
                if (line.Tokens[1].EqualsString(Raw, "++"))
                {
                    Horrors.AddTo(ref CollectionsMarshal.GetValueRefOrNullRef(Variables, variableName), (long)1);
                }
                else if (line.Tokens[1].EqualsString(Raw, "--"))
                    Variables[variableName] = Horrors.Subtract(SimpleEvaluateExpressionSingle(line.Tokens[0]), (long)1);

                break;
            }
            case LineType.BlockStuff:
                if (line.Tokens[0] is BlockPosToken { Type: TokenType.BlockStart } bpt)
                {
                    if (LastConditional is { IsTrue: true })
                        BlockStack.Push(new StackThing(curIndex, LastConditional.IsBreakWorthy, false,
                            LastConditional, curIndex));
                    else
                    {
                        curIndex = Array.FindIndex(Lines,
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
                var end = Array.IndexOf(Lines, (Line l) =>
                    l.Tokens[0] is BlockPosToken { Type: TokenType.BlockEnd } bpt && bpt.Number == start.Number);
                Functions[line.Tokens[1].ToString(Raw)] = (curIndex + 1, end);
                break;
            case LineType.KeywordCall when line.Tokens[0] is CallLikePosToken callToken:
            {
                // todo(cleanup): code duplcation with inside of SimpleEvaluateExpressionSingle
                // todo(perf): Span
                // var args = new object[callToken.Arguments.Length];
                // for (var ind = 0; ind < callToken.Arguments.Length; ind++)
                //     args[ind] = SimpleEvaluateExpressionHigh(callToken.Arguments[ind]);
                MethodCall(callToken.GetName(Raw), callToken: callToken);
                break;
            }
            case LineType.KeywordPlainCall:
            {
                var keyword = line.Tokens[0];
                var keywordString = keyword.ToString(Raw);
                // todo: span
                var args = line.Tokens[1..];
                switch (keywordString)
                {
                    case "break":
                    {
                        var g = BlockStack.Pop();
                        while (!g.IsBreakWorthy)
                            g = BlockStack.Pop();
                        curIndex = Array.FindIndex(Lines,
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
                            Console.Debug(SimpleEvaluateExpressionHigh(args));
                            return;
                        case "dbg_assert_is_one":
                            Variables["$$assertResult"] = (long)SimpleEvaluateExpressionSingle(args[0]) == 1;
                            return;
                        case "dbg_sum_three":
                            Variables["$$assertResult"] = Horrors.Sum(
                                Horrors.Sum(SimpleEvaluateExpressionSingle(args[0]),
                                    SimpleEvaluateExpressionSingle(args[1])), SimpleEvaluateExpressionSingle(args[2]));
                            return;
                    }

                if (Options.EnableDumb)
                    switch (keywordString)
                    {
                        case "goto_line":
                            curIndex = (int)(long)SimpleEvaluateExpressionSingle(args[0]);
                            return;
                    }

                if (Functions.TryGetValue(keywordString, out var func))
                {
                    BlockStack.Push(new StackThing(func.startLineIndex, false, true, null,
                        curIndex));
                    curIndex = func.startLineIndex;
                    return;
                }
                
                // todo(perf): Span
                
                MethodCall(keywordString, line.Tokens.AsSpan()[1..]);
                break;
                //
                // throw new RCaronException($"keyword '{keywordString}' is invalid", RCaronExceptionTime.Runtime);
            }
            default:
                // wtf
                Debugger.Break();
                break;
        }
    }

    // todo: make local func to get arguments as object[] and instead in the method get a token array or smth
    public object? MethodCall(string name, Span<PosToken> tokens = default, CallLikePosToken? callToken = null)
    {
        object At(in Span<PosToken> tokens, int index)
        {
            if (callToken != null)
                return SimpleEvaluateExpressionHigh(callToken.Arguments[index]);
            return SimpleEvaluateExpressionSingle(tokens[index]);
        }

        object[] All(in Span<PosToken> tokens)
        {
            if (callToken != null)
            {
                var res = new object[callToken.Arguments.Length];
                for (var ind = 0; ind < callToken.Arguments.Length; ind++)
                    res[ind] = SimpleEvaluateExpressionHigh(callToken.Arguments[ind]);
                return res;
            }
            return EvaluateMultipleValues(tokens);
        }
        switch (name)
        {
            case "string":
                return At(tokens, 0).ToString()!;
            case "sum":
                return Horrors.Sum(At(tokens, 0), At(tokens, 1));
            case "printfunny":
            case "println":
                foreach (var arg in All(tokens))
                    Console.WriteLine(arg);
                return null;
            case "print":
            {
                var args = All(tokens);
                for (var i = 0; i < args.Length; i++)
                {
                    if(i != 0)
                        Console.Out.Write(' ');
                    Console.Out.Write(args[i]);
                }
                Console.Out.WriteLine();
                return null;
            }
        }

        throw new RCaronException($"method '{name}' is invalid", RCaronExceptionTime.Runtime);
    }

    public object[] EvaluateMultipleValues(in Span<PosToken> tokens, int tokensStartIndex = 0)
    {
        var objs = new object[tokens.Length-tokensStartIndex];
        for (var ind = tokensStartIndex; ind < tokens.Length; ind++)
            objs[ind-tokensStartIndex] = SimpleEvaluateExpressionSingle(tokens[ind]);
        return objs;
    }

    public object SimpleEvaluateExpressionSingle(PosToken token)
    {
        switch (token.Type)
        {
            case TokenType.VariableIdentifier:
                var name = token.ToString(Raw)[1..];
                if (Variables.ContainsKey(name))
                    return Variables[name];
                throw new RCaronException($"variable '{name}' does not exist", RCaronExceptionTime.Runtime);
            case TokenType.Number:
                return Int64.Parse(token.ToSpan(Raw));
            case TokenType.DecimalNumber:
                return Decimal.Parse(token.ToSpan(Raw));
            case TokenType.String:
                // todo(perf): maybe building with on a span first would be cool?
                var s = token.ToSpan(Raw)[1..^1];
                var str = new StringBuilder(s.Length);
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
                return MethodCall(callToken.GetName(Raw), callToken: callToken);
        }

        throw new Exception("yo wtf");
    }

    [CollectionAccess(CollectionAccessType.Read)]
    public object SimpleEvaluateExpressionValue(PosToken[] tokens)
    {
        // repeat action something math
        var index = 0;
        object value = SimpleEvaluateExpressionSingle(tokens[0]);
        while (index < tokens.Length - 1)
        {
            var op = tokens[index + 1].ToString(Raw);
            var second = SimpleEvaluateExpressionSingle(tokens[index + 2]);
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

    public object SimpleEvaluateExpressionHigh(PosToken[] tokens)
        => tokens.Length switch
        {
            1 => SimpleEvaluateExpressionSingle(tokens[0]),
            > 2 => SimpleEvaluateExpressionValue(tokens),
            _ => throw new Exception("what he fuck")
        };

    public bool SimpleEvaluateBool(PosToken[] tokens)
    {
        var val1 = SimpleEvaluateExpressionSingle(tokens[0]);
        // todo: cant switch case with a Span yet -- rider doesnt support
        var op = tokens[1].ToString(Raw);
        var val2 = SimpleEvaluateExpressionSingle(tokens[2]);
        switch (op)
        {
            case Operations.IsEqualOp:
                return val1.Equals(val2);
            case Operations.IsNotEqualOp:
                return !val1.Equals(val2);
            case Operations.IsGreaterOp:
                return Horrors.IsGreater(val1, val2);
            // todo: doesn't feel quite right to do this
            case Operations.IsGreaterOrEqualOp:
                return val1.Equals(val2) || Horrors.IsGreater(val1, val2);
            case Operations.IsLessOp:
                return !val1.Equals(val2) && !Horrors.IsGreater(val1, val2);
            case Operations.IsLessOrEqualOp:
                return val1.Equals(val2) || !Horrors.IsGreater(val1, val2);
            default:
                throw new RCaronException($"unknown operator: {op}", RCaronExceptionTime.Runtime);
        }
    }
}