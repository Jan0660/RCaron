using System.Diagnostics;
using System.Text;

namespace RCaron.Shell;

public static class RunExecutable
{
    public static object? Run(Motor motor, string name, ReadOnlySpan<PosToken> tokens, string code, IPipeline? pipeline, bool isLeftOfPipeline)
    {
        var startInfo = ParseArgs(motor, name, tokens, code);
        if (pipeline is StreamPipeline)
            startInfo.RedirectStandardInput = true;
        if(isLeftOfPipeline)
            startInfo.RedirectStandardOutput = true;
        using var process = Process.Start(startInfo);
        if (process == null)
            return null;
        if (pipeline is StreamPipeline streamPipeline)
        {
            while (!streamPipeline.StreamReader.EndOfStream)
            {
                process.StandardInput.WriteLine(streamPipeline.StreamReader.ReadLine());
            }
            process.StandardInput.Close();
        }
        if (isLeftOfPipeline)
            return new StreamPipeline(process.StandardOutput);
        process.WaitForExit();
        return process.ExitCode;
    }

    public static ProcessStartInfo ParseArgs(Motor motor, string name, ReadOnlySpan<PosToken> tokens, string code)
    {
        static void doTokens(Motor motor, StringBuilder stringBuilder, ProcessStartInfo startInfo,
            ReadOnlySpan<PosToken> tokens, string code)
        {
            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (i != 0 && tokens[i - 1].Position.End != token.Position.Start)
                {
                    startInfo.ArgumentList.Add(stringBuilder.ToString());
                    // reuse the StringBuilder
                    stringBuilder.Clear();
                }

                switch (token)
                {
                    case ConstToken constToken:
                    {
                        if (constToken.Value.ToString() is { } str)
                            stringBuilder.Append(str);
                        break;
                    }
                    case VariableToken variableToken:
                    {
                        var val = motor.EvaluateVariable(variableToken.Name);
                        if (val?.ToString() is { } str)
                            stringBuilder.Append(str);
                        break;
                    }
                    case KeywordToken keywordToken:
                    {
                        stringBuilder.Append(keywordToken.String);
                        break;
                    }
                    case OperationPosToken operationPosToken:
                    {
                        stringBuilder.Append(operationPosToken.ToSpan(code));
                        break;
                    }
                    case ValueOperationValuePosToken valueOperationValuePosToken:
                    {
                        stringBuilder.Append(valueOperationValuePosToken.ToSpan(code));
                        break;
                    }
                    case GroupValuePosToken groupValuePosToken:
                    {
                        stringBuilder.Append(motor.EvaluateExpressionHigh(groupValuePosToken.Tokens));
                        break;
                    }
                    case DotGroupPosToken dotGroupPosToken:
                    {
                        stringBuilder.Append(motor.EvaluateDotThings(dotGroupPosToken.Tokens));
                        break;
                    }
                    case ExternThingToken externThingToken:
                    {
                        stringBuilder.Append('#' + externThingToken.String);
                        break;
                    }
                    case TokenGroupPosToken tokenGroupPosToken:
                    {
                        doTokens(motor, stringBuilder, startInfo, tokenGroupPosToken.Tokens, code);
                        break;
                    }
                    case { Type: TokenType.Range }:
                        stringBuilder.Append("..");
                        break;
                    case { Type: TokenType.Dot }:
                        stringBuilder.Append('.');
                        break;
                    case { Type: TokenType.NativePipelineOperator }:
                        stringBuilder.Append("|>");
                        break;
                    case { Type: TokenType.Colon }:
                        stringBuilder.Append(':');
                        break;
                    default:
                        throw new($"Unexpected token type {token.Type}");
                }
            }
        }

        var startInfo = new ProcessStartInfo(name);
        var stringBuilder = new StringBuilder();
        doTokens(motor, stringBuilder, startInfo, tokens, code);

        // add the last argument
        if (stringBuilder.Length > 0)
            startInfo.ArgumentList.Add(stringBuilder.ToString());

        return startInfo;
    }
}