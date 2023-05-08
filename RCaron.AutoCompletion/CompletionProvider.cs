using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using RCaron.Classes;
using RCaron.LibrarySourceGenerator;
using RCaron.Parsing;

namespace RCaron.AutoCompletion;

public partial class CompletionProvider
{
    static CompletionProvider()
    {
        foreach (var builtInFunction in BuiltInFunctions)
        {
            if (builtInFunction.Detail == null)
                builtInFunction.Detail =
                    $"{(builtInFunction.DetailPreface != null ? builtInFunction.DetailPreface + "\n" : "")}(method) {builtInFunction.Word}(???)";
            builtInFunction.Modifier |= CompletionItemModifier.BuiltIn;
        }
    }

    private readonly ConcurrentDictionary<IRCaronModule, CompletionThing[]> _moduleCompletions = new();
    public List<ICompletionExtension>? Extensions { get; set; }

    private class CompletionContext
    {
        public List<string>? Variables { get; set; }
        public bool IsReturnWorthy { get; set; }
    }

    private class FunctionCompletionContext : CompletionContext
    {
        public ClassDefinition? ClassDefinition { get; set; }
        public Function Function { get; set; }
        public bool IsStaticFunction { get; set; }
    }

    private class ClassDefinitionCompletionContext : CompletionContext
    {
        public required ClassDefinition ClassDefinition { get; init; }
    }

    public List<Completion> GetCompletions(string code, int caretPosition, int maxCompletions = 40,
        LocalScope? localScope = null, IList<IRCaronModule>? modules = null,
        CancellationToken cancellationToken = default)
    {
        var list = new List<Completion>(maxCompletions);
        var parsed = RCaronParser.Parse(code, returnDescriptive: true, errorHandler: new ParsingErrorDontCareHandler());
        cancellationToken.ThrowIfCancellationRequested();
        var contextStack = new NiceStack<CompletionContext>(10);
        DoLines(parsed.FileScope.Lines);

        static bool IsBetween(int caretPosition, int start, int end) => start <= caretPosition && caretPosition <= end;

        void DoTokens(IList<PosToken> tokens)
        {
            if (!IsBetween(caretPosition, tokens[0].Position.Start, tokens[^1].Position.End)) return;
            foreach (var token in tokens)
            {
                if (list.Count >= maxCompletions) return;
                // check caret position is within token
                if (caretPosition > token.Position.Start && caretPosition <= token.Position.End)
                {
                    switch (token)
                    {
                        case KeywordToken keywordToken:
                        {
                            foreach (var builtInFunction in BuiltInFunctions)
                            {
                                if (list.Count >= maxCompletions) return;
                                if (builtInFunction.Word.StartsWith(keywordToken.String,
                                        StringComparison.InvariantCultureIgnoreCase))
                                    list.Add(new Completion(builtInFunction, token.Position));
                            }

                            foreach (var keyword in Keywords)
                            {
                                if (list.Count >= maxCompletions) return;
                                if (keyword.Word.StartsWith(keywordToken.String,
                                        StringComparison.InvariantCultureIgnoreCase))
                                    list.Add(new Completion(keyword, token.Position));
                            }

                            if (parsed.FileScope.Functions != null)
                            {
                                if (list.Count >= maxCompletions) return;
                                StringBuilder detail = new();
                                foreach (var function in parsed.FileScope.Functions)
                                {
                                    detail.Clear();
                                    if (list.Count >= maxCompletions) return;
                                    if (function.Key.StartsWith(keywordToken.String,
                                            StringComparison.InvariantCultureIgnoreCase))
                                    {
                                        detail.Append("(function) ");
                                        detail.Append(function.Key);
                                        detail.Append('(');
                                        for (var i = 0; i < function.Value.Arguments?.Length; i++)
                                        {
                                            var argument = function.Value.Arguments[i];
                                            if (i != 0)
                                                detail.Append(", ");
                                            detail.Append('$');
                                            detail.Append(argument.Name);
                                            if (argument.DefaultValue?.Equals(RCaronInsideEnum.NoDefaultValue) ??
                                                true) continue;
                                            detail.Append(" = ");
                                            if (argument.DefaultValue is string)
                                            {
                                                detail.Append('"');
                                                detail.Append(argument.DefaultValue);
                                                detail.Append('"');
                                            }
                                            else
                                                detail.Append(argument.DefaultValue);
                                        }

                                        detail.Append(')');
                                        list.Add(new Completion(new CompletionThing
                                        {
                                            Word = function.Key,
                                            Detail = detail.ToString(),
                                            Kind = CompletionItemKind.Function,
                                        }, token.Position));
                                    }
                                }
                            }

                            if (modules != null)
                            {
                                var documentation = new StringBuilder();
                                foreach (var module in modules)
                                {
                                    if (list.Count >= maxCompletions) return;
                                    if (!_moduleCompletions.TryGetValue(module, out var completionThings))
                                    {
                                        var completionThingsList = new List<CompletionThing>();
                                        var type = module.GetType();
                                        foreach (var method in type.GetMethods(
                                                     BindingFlags.Public | BindingFlags.Instance |
                                                     BindingFlags.Static))
                                        {
                                            documentation.Clear();

                                            if (list.Count >= maxCompletions) return;
                                            var methodAttribute = method.GetCustomAttribute<MethodAttribute>();
                                            if (methodAttribute == null) continue;
                                            var parameters = method.GetParameters();
                                            var argsString = parameters.Length == 1 ? null : new StringBuilder();
                                            for (var i = 1; i < parameters.Length; i++)
                                            {
                                                Debug.Assert(argsString != null, nameof(argsString) + " != null");
                                                var parameter = parameters[i];
                                                if (list.Count >= maxCompletions) return;
                                                if (i != 1) argsString.Append(", ");
                                                argsString.Append($"#{parameter.ParameterType.Name} ${parameter.Name}");
                                                if (parameter.HasDefaultValue)
                                                    argsString.Append($" = {parameter.DefaultValue}");
                                            }

                                            documentation.AppendLine($"(From {type.FullName}).");
                                            if (methodAttribute.Description != null)
                                            {
                                                documentation.AppendLine();
                                                documentation.AppendLine(methodAttribute.Description);
                                            }

                                            completionThingsList.Add(new CompletionThing()
                                            {
                                                Word = methodAttribute.Name,
                                                Kind = CompletionItemKind.Method,
                                                Detail =
                                                    $"(module method) {methodAttribute.Name}({argsString?.ToString() ?? string.Empty})",
                                                Documentation = documentation.ToString(),
                                            });
                                        }

                                        completionThings = completionThingsList.ToArray();
                                        _moduleCompletions.TryAdd(module, completionThings);
                                    }

                                    foreach (var completionThing in completionThings)
                                    {
                                        if (list.Count >= maxCompletions) return;
                                        if (completionThing.Word.StartsWith(keywordToken.String,
                                                StringComparison.InvariantCultureIgnoreCase))
                                            list.Add(new Completion(completionThing, token.Position));
                                    }
                                }
                            }

                            break;
                        }
                        case CodeBlockToken codeBlockToken:
                            DoLines(codeBlockToken.Lines);
                            break;
                        case VariableToken variableToken:
                            foreach (var constant in Constants)
                            {
                                if (list.Count >= maxCompletions) return;
                                if (constant.Word.AsSpan().StartsWith(variableToken.ToSpan(code),
                                        StringComparison.InvariantCultureIgnoreCase))
                                    list.Add(new Completion(constant, token.Position));
                            }

                            if (localScope is { Variables: { } })
                            {
                                foreach (var variable in localScope.Variables)
                                {
                                    if (list.Count >= maxCompletions) return;
                                    if (variable.Key.AsSpan().StartsWith(variableToken.ToSpan(code)[1..],
                                            StringComparison.InvariantCultureIgnoreCase))
                                        list.Add(new Completion(new CompletionThing()
                                        {
                                            Word = '$' + variable.Key,
                                            Kind = CompletionItemKind.Variable,
                                            Detail = $"(global variable) {variable.Key}",
                                            Documentation = variable.Value == null
                                                ? "Value is `null`."
                                                : $"Current value(of type `{variable.Value.GetType()}`): `{variable.Value}`",
                                        }, token.Position));
                                }
                            }

                            for (var i = contextStack.Count - 1; i >= 0; i--)
                            {
                                var context = contextStack.At(i);

                                // function arguments
                                if (context is FunctionCompletionContext functionCompletionContext)
                                {
                                    var function = functionCompletionContext.Function;

                                    if (function.Arguments != null)
                                        foreach (var arg in function.Arguments)
                                        {
                                            if (list.Count >= maxCompletions) return;
                                            if (arg.Name.AsSpan().StartsWith(variableToken.Name,
                                                    StringComparison.InvariantCultureIgnoreCase))
                                                list.Add(new Completion(new CompletionThing
                                                {
                                                    Word = '$' + arg.Name,
                                                    Kind = CompletionItemKind.Variable,
                                                    Detail = $"(function argument) {arg.Name}",
                                                }, token.Position));
                                        }

                                    if (functionCompletionContext.ClassDefinition != null)
                                    {
                                        void DoVarList(IList<string> variables, string detailInsidePrefix)
                                        {
                                            foreach (var arg in variables)
                                            {
                                                if (list.Count >= maxCompletions) return;
                                                if (arg.AsSpan().StartsWith(variableToken.Name,
                                                        StringComparison.InvariantCultureIgnoreCase))
                                                    list.Add(new Completion(new CompletionThing
                                                    {
                                                        Word = '$' + arg,
                                                        Kind = CompletionItemKind.Variable,
                                                        Detail = $"({detailInsidePrefix}) {arg}",
                                                    }, token.Position));
                                            }
                                        }

                                        if (functionCompletionContext.ClassDefinition.StaticPropertyNames != null)
                                            DoVarList(functionCompletionContext.ClassDefinition.StaticPropertyNames,
                                                "static property");
                                        if (!functionCompletionContext.IsStaticFunction &&
                                            functionCompletionContext.ClassDefinition.PropertyNames != null)
                                            DoVarList(functionCompletionContext.ClassDefinition.PropertyNames,
                                                "property");
                                    }
                                }

                                // local variables
                                {
                                    if (context.Variables != null)
                                        foreach (var variable in context.Variables)
                                        {
                                            if (list.Count >= maxCompletions) return;
                                            if (variable.AsSpan().StartsWith(variableToken.Name,
                                                    StringComparison.InvariantCultureIgnoreCase))
                                                list.Add(new Completion(new CompletionThing
                                                {
                                                    Word = '$' + variable,
                                                    Kind = CompletionItemKind.Variable,
                                                    Detail = $"(local variable) {variable}",
                                                }, token.Position));
                                        }
                                }

                                if (context.IsReturnWorthy)
                                    break;
                            }

                            break;
                    }

                    if (list.Count >= maxCompletions) return;
                    if (Extensions != null)
                        foreach (var extension in Extensions)
                            extension.OnToken(list, token, caretPosition, maxCompletions, localScope, modules, parsed,
                                cancellationToken);
                }
            }
        }

        void DoLine(Line line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (list.Count >= maxCompletions) return;
            switch (line)
            {
                case TokenLine tokenLine:
                    if (tokenLine.Type is LineType.Function or LineType.StaticFunction)
                    {
                        var context = new FunctionCompletionContext
                        {
                            IsReturnWorthy = true,
                            IsStaticFunction = line.Type == LineType.StaticFunction,
                        };
                        var name = ((CallLikePosToken)tokenLine.Tokens[context.IsStaticFunction ? 2 : 1]).Name;
                        // try to find encapsulating class definition above
                        if (contextStack.TryPeek(out var contextAbove))
                            if (contextAbove is ClassDefinitionCompletionContext classDefinitionContext)
                            {
                                context.ClassDefinition = classDefinitionContext.ClassDefinition;
                                context.Function = classDefinitionContext.ClassDefinition.Functions![name];
                            }

                        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                        context.Function ??= parsed.FileScope.Functions![name];
                        contextStack.Push(context);
                        DoLines(((CodeBlockToken)tokenLine.Tokens[context.IsStaticFunction ? 3 : 2]).Lines,
                            false);
                        var popped = contextStack.Pop();
                        Debug.Assert(popped == context);
                    }
                    else if (tokenLine.Type == LineType.ClassDefinition)
                    {
                        var name = ((KeywordToken)tokenLine.Tokens[1]).String;
                        if (!Motor.TryGetClassDefinition(name, parsed.FileScope, out var classDefinition))
                            return;
                        var context = new ClassDefinitionCompletionContext()
                        {
                            IsReturnWorthy = true,
                            ClassDefinition = classDefinition,
                        };
                        contextStack.Push(context);
                        DoLines(((CodeBlockToken)tokenLine.Tokens[2]).Lines, false);
                        var popped = contextStack.Pop();
                        Debug.Assert(popped == context);
                    }
                    else if (tokenLine.Type == LineType.CatchBlock)
                    {
                        var context = new CompletionContext()
                        {
                            Variables = new() { "$exception" },
                        };
                        contextStack.Push(context);
                        DoLines(((CodeBlockToken)tokenLine.Tokens[1]).Lines, false);
                        var popped = contextStack.Pop();
                        Debug.Assert(popped == context);
                    }
                    else if (tokenLine.Type == LineType.VariableAssignment)
                    {
                        var variableToken = (VariableToken)tokenLine.Tokens[0];
                        var ls = contextStack.Peek().Variables ??= new();
                        if (!ls.Contains(variableToken.Name))
                            ls.Add(variableToken.Name);
                        DoTokens(tokenLine.Tokens);
                    }
                    else
                    {
                        DoTokens(tokenLine.Tokens);
                    }

                    break;
                case ForLoopLine forLoopLine:
                    if (forLoopLine.Initializer != null)
                        DoLine(forLoopLine.Initializer);
                    DoTokens(forLoopLine.CallToken.Arguments[1]);
                    if (forLoopLine.Iterator != null)
                        DoLine(forLoopLine.Iterator);
                    DoLines(forLoopLine.Body.Lines);
                    break;
                case SingleTokenLine singleTokenLine:
                    if (!IsBetween(caretPosition, singleTokenLine.Token.Position.Start,
                            singleTokenLine.Token.Position.End))
                        return;
                    DoTokens(new[] { singleTokenLine.Token });
                    break;
                case CodeBlockLine codeBlockLine:
                    DoLines(codeBlockLine.Token.Lines);
                    break;
            }
        }

        void DoLines(IList<Line> lines, bool createContext = true, bool isReturnWorthy = false)
        {
            CompletionContext? context = null;
            if (createContext)
                contextStack.Push(context = new CompletionContext { IsReturnWorthy = isReturnWorthy });
            foreach (var line in lines)
                DoLine(line);
            if (context != null)
            {
                var popped = contextStack.Pop();
                Debug.Assert(popped == context);
            }
        }

        return list;
    }

    public void ClearCache()
    {
        _moduleCompletions.Clear();
    }
}