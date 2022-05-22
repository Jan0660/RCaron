using System.Diagnostics;
using System.Text;
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

    public record StackThing(int LineIndex, int BlockDepth, int BlockNumber, bool IsBreakWorthy,
        Conditional Conditional, bool IsReturnWorthy, int PreviousLineIndex);

    public Stack<StackThing>
        // public Stack<(int LineIndex, int BlockDepth, int BlockNumber, bool IsBreakWorthy, Conditional Conditional)>
        BlockStack { get; set; } = new();

    public Conditional LastConditional { get; set; }
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
                var variableName = line.Tokens[0].ToString(Raw)[1..];
                var obj = SimpleEvaluateExpressionHigh(line.Tokens[2..]);
                Variables[variableName] = obj;
                Console.Debug($"variable '{variableName}' set to '{obj}'");
                break;
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
            case LineType.BlockStuff:
                if (line.Tokens[0] is BlockPosToken { Type: TokenType.BlockStart } bpt)
                {
                    if (LastConditional is { IsTrue: true })
                        BlockStack.Push(new StackThing(curIndex, bpt.Depth, bpt.Number, LastConditional.IsBreakWorthy,
                            LastConditional, false, curIndex));
                    else
                    {
                        curIndex = Array.FindIndex(Lines,
                            l => l.Tokens[0] is BlockPosToken { Type: TokenType.BlockEnd } bpt2 &&
                                 bpt2.Depth == bpt.Depth && bpt2.Number == bpt.Number);
                        return;
                    }
                }
                else if (line.Tokens[0] is BlockPosToken { Type: TokenType.BlockEnd })
                {
                    var curBlock = BlockStack.Peek();
                    if (curBlock.Conditional is { IsTrue: true, IsOnce: true })
                        return;
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
                        curIndex = curBlock.PreviousLineIndex;
                }

                break;
            case LineType.LoopLoop:
                LastConditional = new Conditional(lineIndex: curIndex, isOnce: false,
                    isTrue: true, isBreakWorthy: true, null);
                break;
            case LineType.ForLoop when line.Tokens[0] is CallLikePosToken callToken:
                var falseI = 0;
                RunLine(RCaronRunner.GetLine(callToken.Arguments[0].ToList(), ref falseI, Raw));
                falseI = 0;
                LastConditional = new ForLoopConditional(lineIndex: curIndex, isOnce: false,
                    isTrue: true, isBreakWorthy: true, callToken.Arguments[1],
                    RCaronRunner.GetLine(callToken.Arguments[2].ToList(), ref falseI, Raw));
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
                var args = new object[callToken.Arguments.Length];
                for (var ind = 0; ind < callToken.Arguments.Length; ind++)
                    args[ind] = SimpleEvaluateExpressionHigh(callToken.Arguments[ind]);
                MethodCall(callToken.GetName(Raw), args);
                break;
            }
            case LineType.KeywordPlainCall:
            {
                var keyword = line.Tokens[0];
                var keywordString = keyword.ToString(Raw);
                var args = line.Tokens[1..];
                switch (keywordString)
                {
                    case "println":
                        foreach (var token in args)
                            System.Console.WriteLine(SimpleEvaluateExpressionSingle(token));
                        return;
                    case "break":
                        var g = BlockStack.Pop();
                        while (!g.IsBreakWorthy)
                            g = BlockStack.Pop();
                        // i = g.LineIndex;
                        curIndex = Array.FindIndex(Lines,
                            l => l is { Type: LineType.BlockStuff }
                                 && l.Tokens[0] is BlockPosToken { Type: TokenType.BlockEnd }) + 1;
                        // Rider doesn't support this stuff yet and thinks it's an error, not gonna use it for now i guess
                        // i = Array.FindIndex(Lines,
                        //     l => l is
                        //     {
                        //         Type: LineType.BlockStuff, Tokens:  [BlockPosToken
                        //         {
                        //             Type: TokenType.BlockEnd
                        //         },
                        //         ..]});
                        return;
                }

                if (Options.EnableDebugging)
                    switch (keywordString)
                    {
                        case "dbg_println":
                            Console.Debug(SimpleEvaluateExpressionHigh(args));
                            return;
                        case "dbg_assert_is_one":
                            Variables["$$assertResult"] = (long)SimpleEvaluateExpressionHigh(args) == 1;
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
                            curIndex = (int)(long)SimpleEvaluateExpressionHigh(args);
                            return;
                    }

                if (Functions.TryGetValue(keywordString, out var func))
                {
                    var st = (BlockPosToken)Lines[func.startLineIndex].Tokens[0];
                    BlockStack.Push(new StackThing(func.startLineIndex, st.Depth, st.Number, false, null, true,
                        curIndex + 1));
                    curIndex = func.startLineIndex;
                    return;
                }

                throw new RCaronException($"keyword '{keywordString}' is invalid", RCaronExceptionTime.Runtime);
            }
            default:
                // wtf
                Debugger.Break();
                break;
        }
    }

    public object? MethodCall(string name, object[] arguments)
    {
        switch (name)
        {
            case "string":
                return arguments[0].ToString()!;
            case "sum":
                return Horrors.Sum(arguments[0], arguments[1]);
            case "printfunny":
                Console.WriteLine(arguments[0]);
                return null;
        }

        throw new RCaronException($"method '{name}' is invalid", RCaronExceptionTime.Runtime);
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
                return Int64.Parse(token.ToString(Raw));
            case TokenType.DecimalNumber:
                return Decimal.Parse(token.ToString(Raw));
            case TokenType.String:
                // todo(perf): maybe building with on a span first would be cool?
                var s = token.ToString(Raw)[1..^1];
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
                var args = new object[callToken.Arguments.Length];
                for (var i = 0; i < callToken.Arguments.Length; i++)
                    args[i] = SimpleEvaluateExpressionHigh(callToken.Arguments[i]);
                return MethodCall(callToken.GetName(Raw), args);
        }

        throw new Exception("yo wtf");
    }

    public object SimpleEvaluateExpressionValue(PosToken[] tokens)
    {
        // if (tokens.Length == 1)
        //     return SimpleEvaluateExpressionSingle(tokens[0]);
        // repeat action something math
        var index = 0;
        object? value = null;
        while (index < tokens.Length - 1)
        {
            var first = index == 0 ? SimpleEvaluateExpressionSingle(tokens[index]) : value;
            var op = tokens[index + 1].ToString(Raw);
            var second = SimpleEvaluateExpressionSingle(tokens[index + 2]);
            switch (op)
            {
                case Operations.SumOp:
                    value = Horrors.Sum(first, second);
                    break;
                case Operations.SubtractOp:
                    value = Horrors.Subtract(first, second);
                    break;
                case Operations.MultiplyOp:
                    value = Horrors.Multiply(first, second);
                    break;
                case Operations.DivideOp:
                    value = Horrors.Divide(first, second);
                    break;
                case Operations.ModuloOp:
                    value = Horrors.Modulo(first, second);
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
        for (var p = 0; p < tokens.Length; p++)
        {
            if (p > 0)
                return false;
            var expr1 = SimpleEvaluateExpressionSingle(tokens[p]);
            var op = tokens[p + 1].ToString(Raw);
            var expr2 = SimpleEvaluateExpressionSingle(tokens[p + 2]);
            switch (op)
            {
                case Operations.IsEqualOp:
                    return expr1.Equals(expr2);
                case Operations.IsNotEqualOp:
                    return !expr1.Equals(expr2);
                case Operations.IsGreaterOp:
                    return Horrors.IsGreater(expr1, expr2);
                // todo: unlazy myself lol
                case Operations.IsGreaterOrEqualOp:
                    return expr1.Equals(expr2) || Horrors.IsGreater(expr1, expr2);
                case Operations.IsLessOp:
                    return !expr1.Equals(expr2) && !Horrors.IsGreater(expr1, expr2);
                case Operations.IsLessOrEqualOp:
                    return expr1.Equals(expr2) || !Horrors.IsGreater(expr1, expr2);
            }
        }

        return false;
    }
}