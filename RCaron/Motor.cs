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
    public Stack<(int LineIndex, int BlockDepth, int BlockNumber)> BlockStack { get; set; } = new();
    public Conditional LastConditional { get; set; }
    public MotorOptions Options { get; }
    public class Conditional
    {
        public Conditional(int lineIndex, bool isOnce, bool isTrue)
        {
            LineIndex = lineIndex;
            IsOnce = isOnce;
            IsTrue = isTrue;
        }

        public int LineIndex { get; set; }
        public bool IsOnce { get; set; }
        public bool IsTrue { get; set; }
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
                    LastConditional = new Conditional(lineIndex: i, isOnce: true,
                        isTrue: SimpleEvaluateBool(line.Tokens[2..^1]));
                    break;
                case LineType.BlockStuff:
                    if (line.Tokens[0] is BlockPosToken { Type: TokenType.BlockStart } bpt)
                    {
                        if (LastConditional is { IsTrue: true, IsOnce: true })
                        {
                            BlockStack.Push((i, bpt.Depth, bpt.Number));
                        }
                        else
                        {
                            i = Array.FindIndex(Lines,
                                l => l.Tokens[0] is BlockPosToken { Type: TokenType.BlockEnd } bpt2 &&
                                     bpt2.Depth == bpt.Depth && bpt2.Number == bpt.Number);
                            continue;
                        }
                    }

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
                    }
                    if(Options.EnableDebugging)
                        switch (keywordString)
                        {
                            case "dbg_println":
                                Console.Debug(SimpleEvaluateExpressionHigh(args));
                                continue;
                        }
                    if(Options.EnableDumb)
                        switch (keywordString)
                        {
                            case "goto_line":
                                i = (int)(long)SimpleEvaluateExpressionHigh(args);
                                continue;
                        }
                    Console.Warn($"keyword '{keywordString}' is invalid");

                    break;
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
                return token.ToString(Raw)[1..^1];
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
        while (index < tokens.Length-1)
        {
            var first = index == 0 ? SimpleEvaluateExpressionSingle(tokens[index]) : value;
            var op = tokens[index+1].ToString(Raw);
            var second = SimpleEvaluateExpressionSingle(tokens[index+2]);
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

            index+=2;
        }

        return value;
    }

    public object SimpleEvaluateExpressionHigh(PosToken[] tokens)
    {
        if (tokens.Length == 1)
            return SimpleEvaluateExpressionSingle(tokens[0]);
        if (tokens.Length > 2)
        {
            if (tokens.Any(t => t is PosToken { Type: TokenType.Operation }))
            {
                
            }
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
            // future: maybe get the actual end of a multi token expression by having "ending" operations?
            var expr1 = SimpleEvaluateExpressionSingle(tokens[p]);
            var op = tokens[p + 1].ToString(Raw);
            var expr2 = SimpleEvaluateExpressionSingle(tokens[p + 2]);
            switch (op)
            {
                case Operations.IsEqualOp:
                    return expr1.Equals(expr2);
                case Operations.IsNotEqualOp:
                    return !expr1.Equals(expr2);
            }
        }

        return false;
    }
}