// See https://aka.ms/new-console-template for more information

using System.Linq.Expressions;
using System.Reflection;
using RCaron;

Console.WriteLine("Hello, World!");

var parsed = RCaronRunner.Parse(@"$h = 0 + 1;
print $h $h;
print 'a string';");
var variables = new Dictionary<string, ParameterExpression>(StringComparer.InvariantCultureIgnoreCase);
var expressions = new List<Expression>();
Lazy<MethodInfo> consoleWriteMethod =
    new(() => typeof(Console).GetMethod(nameof(Console.Write), new[] { typeof(object) })!);
Lazy<MethodInfo> consoleWriteLineMethod =
    new(() => typeof(Console).GetMethod(nameof(Console.WriteLine), Array.Empty<Type>())!);

Expression GetSingleExpression(PosToken token)
{
    if (token is ConstToken constToken)
        return Expression.Constant(constToken.Value);
    if (token is VariableToken variableToken)
    {
        if (!variables.TryGetValue(variableToken.Name, out var variable))
            throw new Exception("variable not declared");
        return variable;
    }

    throw new Exception("Not implemented");
}

Expression GetMathExpression(ReadOnlySpan<PosToken> tokens)
{
    if (tokens.Length == 1 && tokens[0] is MathValueGroupPosToken mathToken)
        return GetMathExpression(mathToken.ValueTokens);
    Expression exp = GetSingleExpression(tokens[0]);
    for (var i = 1; i < tokens.Length; i++)
    {
        var opToken = (ValueOperationValuePosToken)tokens[i];
        switch (opToken.Operation)
        {
            case OperationEnum.Sum:
                exp = Expression.Add(exp, GetSingleExpression(tokens[++i]));
                break;
        }
    }

    return exp;
}

foreach (var line in parsed.Lines)
{
    switch (line)
    {
        // variable
        case TokenLine { Type: LineType.VariableAssignment } tokenLine:
        {
            var vt = (VariableToken)tokenLine.Tokens[0];
            ParameterExpression? varExp = null;
            if (!variables.ContainsKey(vt.Name))
            {
                varExp ??= Expression.Variable(typeof(object), vt.Name);
                variables.Add(vt.Name, varExp);
                expressions.Add(varExp);
            }
            varExp ??= variables[vt.Name];
            var right = GetMathExpression(tokenLine.Tokens.AsSpan()[2..]);
            if (right.Type != typeof(object))
                right = Expression.Convert(right, typeof(object));
            expressions.Add(Expression.Assign(varExp, right));
            break;
        }
        case TokenLine { Type: LineType.KeywordPlainCall } tokenLine:
        {
            switch (((KeywordToken)tokenLine.Tokens[0]).String)
            {
                case "print":
                    for (var i = 1; i < tokenLine.Tokens.Length; i++)
                    {
                        expressions.Add(Expression.Call(consoleWriteMethod.Value, GetSingleExpression(tokenLine.Tokens[i])));
                        expressions.Add(Expression.Call(consoleWriteMethod.Value, Expression.Constant(' ', typeof(object))));
                    }
                    expressions.Add(Expression.Call(consoleWriteLineMethod.Value));

                    break;
            }

            break;
        }
    }
}

var lambda = Expression.Lambda(Expression.Block(variables.Select(x => x.Value), expressions)).Compile();
lambda.DynamicInvoke();