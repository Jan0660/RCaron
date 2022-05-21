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

    public Stack<(int LineIndex, int BlockDepth, int BlockNumber, bool IsBreakWorthy, Conditional Conditional)>
        BlockStack { get; set; } = new();

    public Conditional LastConditional { get; set; }
    public MotorOptions Options { get; }

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
    }

    public Motor(RCaronRunnerContext runnerContext, MotorOptions? options = null)
    {
        Lines = runnerContext.Lines;
        Raw = runnerContext.Code;
        Options = options ?? new();
    }

    public void Run()
    {
        for (var i = 0; i < Lines.Length; i++)
        {
            if (i >= Lines.Length)
                break;
            var line = Lines[i];
            switch (line.Type)
            {
                case LineType.VariableAssignment:
                    var variableName = line.Tokens[0].ToString(Raw)[1..];
                    var obj = SimpleEvaluateExpressionHigh(line.Tokens[2..]);
                    Variables[variableName] = obj;
                    Console.Debug($"variable '{variableName}' set to '{obj}'");
                    break;
                case LineType.IfStatement:
                {
                    var tokens = line.Tokens[2..^1];
                    LastConditional = new Conditional(lineIndex: i, isOnce: true,
                        isTrue: SimpleEvaluateBool(tokens), isBreakWorthy: false, evalTokens: null);
                    break;
                }
                case LineType.WhileLoop:
                {
                    var tokens = line.Tokens[2..^1];
                    LastConditional = new Conditional(lineIndex: i, isOnce: false,
                        isTrue: SimpleEvaluateBool(tokens), isBreakWorthy: true, evalTokens: tokens);
                    break;
                }
                case LineType.BlockStuff:
                    if (line.Tokens[0] is BlockPosToken { Type: TokenType.BlockStart } bpt)
                    {
                        if (LastConditional is { IsTrue: true, IsOnce: false })
                        {
                            BlockStack.Push((i, bpt.Depth, bpt.Number, LastConditional.IsBreakWorthy, LastConditional));
                        }
                        else if (LastConditional is { IsTrue: true, IsOnce: true })
                        {
                            BlockStack.Push((i, bpt.Depth, bpt.Number, LastConditional.IsBreakWorthy, LastConditional));
                        }
                        else
                        {
                            i = Array.FindIndex(Lines,
                                l => l.Tokens[0] is BlockPosToken { Type: TokenType.BlockEnd } bpt2 &&
                                     bpt2.Depth == bpt.Depth && bpt2.Number == bpt.Number);
                            continue;
                        }
                    }
                    else if (line.Tokens[0] is BlockPosToken { Type: TokenType.BlockEnd })
                    {
                        var curBlock = BlockStack.Peek();
                        if (curBlock.Conditional is { IsTrue: true, IsOnce: true })
                            // i = curBlock.LineIndex;
                            // i++;
                            continue;
                        else if (curBlock.Conditional is { IsOnce: false })
                        {
                            if (curBlock.Conditional.EvaluateTokens == null)
                                i = curBlock.LineIndex;
                            else if (SimpleEvaluateBool(curBlock.Conditional.EvaluateTokens!))
                                i = curBlock.LineIndex;
                        }
                    }
                    break;
                case LineType.LoopLoop:
                    LastConditional = new Conditional(lineIndex: i, isOnce: false,
                        isTrue: true, isBreakWorthy: true, null);
                    break;
                case LineType.KeywordPlainCall:
                    var keyword = line.Tokens[0];
                    var keywordString = keyword.ToString(Raw);
                    var args = line.Tokens[1..];
                    switch (keywordString)
                    {
                        case "println":
                            System.Console.WriteLine(SimpleEvaluateExpressionHigh(args));
                            continue;
                        case "break":
                            var g = BlockStack.Pop();
                            while (!g.IsBreakWorthy)
                                g = BlockStack.Pop();
                            // i = g.LineIndex;
                            i = Array.FindIndex(Lines,
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
                            continue;
                    }

                    if (Options.EnableDebugging)
                        switch (keywordString)
                        {
                            case "dbg_println":
                                Console.Debug(SimpleEvaluateExpressionHigh(args));
                                continue;
                            case "dbg_assert_is_one":
                                Variables[$"$$assertResult"] = (long)SimpleEvaluateExpressionHigh(args) == 1;
                                continue;
                        }

                    if (Options.EnableDumb)
                        switch (keywordString)
                        {
                            case "goto_line":
                                i = (int)(long)SimpleEvaluateExpressionHigh(args);
                                continue;
                        }

                    throw new RCaronException($"keyword '{keywordString}' is invalid", RCaronExceptionTime.Runtime);
            }
        }
    }

    public object SimpleEvaluateExpressionSingle(PosToken token)
    {
        switch (token.Type)
        {
            case TokenType.VariableIdentifier:
                var name = token.ToString(Raw)[1..];
                if (Variables.ContainsKey(name))
                    return Variables[name];
                System.Console.Error.WriteLine("Variable '{0}' not found", name);
                Environment.Exit(-1);
                break;
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
        }

        throw new Exception("yo wtf");
    }

    public object SimpleEvaluateExpressionValue(PosToken[] tokens)
    {
        // repeat action something math
        var index = 0;
        object value = null;
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
            }

            index += 2;
        }

        return value;
    }

    public object SimpleEvaluateExpressionHigh(PosToken[] tokens)
    {
        if (tokens.Length == 1)
            return SimpleEvaluateExpressionSingle(tokens[0]);
        if (tokens.Length > 2)
        {
            return SimpleEvaluateExpressionValue(tokens);
        }

        return new Exception("what he fuck");
    }

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
            }
        }

        return false;
    }
}