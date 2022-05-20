using Console = Log73.Console;

namespace RCaron;

public class Motor
{
    public string Raw { get; set; }
    public Line[] Lines { get; set; }
    public Dictionary<string, object> Variables { get; set; } = new();
    public Stack<(int LineIndex, int BlockDepth, int BlockNumber)> BlockStack { get; set; } = new();
    public Conditional LastConditional { get; set; }
    public bool EnableDebugging { get; set; } = false;

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

    public Motor(Line[] lines)
    {
        Lines = lines;
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
                    var obj = SimpleEvaluateExpression(line.Tokens[2..]);
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
                            System.Console.WriteLine(SimpleEvaluateExpression(args));
                            continue;
                    }
                    if(EnableDebugging)
                        switch (keywordString)
                        {
                            case "dbg_println":
                                Console.Debug(SimpleEvaluateExpression(args));
                                continue;
                        }
                    Console.Warn($"keyword '{keywordString}' is invalid");

                    break;
            }
        }
    }

    public object SimpleEvaluateExpression(PosToken token)
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
        }

        throw new Exception("yo wtf");
    }

    // todo: boxing and other retarded allocations
    public object SimpleEvaluateExpression(PosToken[] tokens)
    {
        if (tokens.Length == 1)
            return SimpleEvaluateExpression(tokens[0]);
        if (tokens.Length > 2)
        {
            // repeat action something
            var index = 0;
            object value = null;
            while (index < tokens.Length-1)
            {
                var first = index == 0 ? SimpleEvaluateExpression(tokens[index]) : value;
                var op = tokens[index+1].ToString(Raw);
                var second = SimpleEvaluateExpression(tokens[index+2]);
                switch (op)
                {
                    case "+":
                        value = Horrors.Sum(first, second);
                        break;
                    case "-":
                        value= Horrors.Subtract(first, second);
                        break;
                }

                index+=2;
            }

            return value;
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
            var expr1 = SimpleEvaluateExpression(tokens[p]);
            var op = tokens[p + 1].ToString(Raw);
            var expr2 = SimpleEvaluateExpression(tokens[p + 2]);
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

    // todo: boxing and other retarded allocations
    // public object EvaluateToken(PosToken token)
    // {
    //     switch (token.Type)
    //     {
    //         case TokenType.VariableIdentifier:
    //             var name = token.ToString(Raw)[1..];
    //             if (Variables.ContainsKey(name))
    //                 return Variables[name];
    //             Console.Error.WriteLine("Variable '{0}' not found", name);
    //             Environment.Exit(-1);
    //             break;
    //         case TokenType.String:
    //             return token.ToString(Raw)[1..^1];
    //     }
    //
    //     return null;
    // }
}