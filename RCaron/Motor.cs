using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Dynamitey;
using JetBrains.Annotations;
using Log73;
using RCaron.BaseLibrary;
using RCaron.Classes;
using Console = Log73.Console;
using ExceptionCode = RCaron.RCaronExceptionCode;

namespace RCaron;

public enum RCaronInsideEnum : byte
{
    VariableNotFound,
    NoDefaultValue,
    NotAssigned,
    MethodNotFound,

    // todo(current): use everywhere instead of null
    NoReturnValue,
    Breaked,
    Continued,
    // todo: fix the name of this and NoReturnValue to make sense
    ReturnWithoutValue,
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
        public StackThing(bool isBreakWorthy, bool isReturnWorthy,
            LocalScope? scope)
        {
            this.IsBreakWorthy = isBreakWorthy;
            this.IsReturnWorthy = isReturnWorthy;
            this.Scope = scope;
        }

        public bool IsBreakWorthy { get; init; }
        public bool IsReturnWorthy { get; init; }
        public LocalScope? Scope { get; set; }
    }

    public NiceStack<StackThing> BlockStack { get; set; } = new();
    public FileScope FileScope { get; set; } = new();
    public MotorOptions Options { get; }
    public List<IRCaronModule> Modules { get; set; }

    /// <summary>
    /// If true and meets an else(if), it will be skipped.
    /// </summary>
    public bool ElseState { get; set; } = false;

    public record Function(CodeBlockToken CodeBlock, FunctionArgument[]? Arguments);

    public class FunctionArgument
    {
        public FunctionArgument(string Name)
        {
            this.Name = Name;
        }

        public string Name { get; }
        public object? DefaultValue { get; set; } = RCaronInsideEnum.NoDefaultValue;
    }

    public Dictionary<string, Function> Functions { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);
    public LocalScope GlobalScope { get; set; } = new();
    public List<ClassDefinition>? ClassDefinitions { get; set; }

#pragma warning disable CS8618
    public Motor(RCaronRunnerContext runnerContext, MotorOptions? options = null)
#pragma warning restore CS8618
    {
        UseContext(runnerContext);
        Options = options ?? new();
        Modules = new List<IRCaronModule>(1) { new LoggingModule() };
    }

    public void UseContext(RCaronRunnerContext runnerContext)
    {
        Lines = runnerContext.Lines;
        Raw = runnerContext.Code;
        ClassDefinitions = runnerContext.ClassDefinitions;
    }

    /// <summary>
    /// current line index
    /// </summary>
    private int curIndex;

    public object? Run(int startIndex = 0)
    {
        curIndex = startIndex;
        for (; curIndex < Lines.Count; curIndex++)
        {
            if (curIndex >= Lines.Count)
                break;
            var line = Lines[curIndex];
            var res = RunLine(line);
            if (res.Exit)
                return res.Result;
        }

        return RCaronInsideEnum.NoReturnValue;
    }

    public int GetLineNumber()
    {
        var lineNumber = -1;
        var pos = Lines[curIndex] switch
        {
            TokenLine tl => tl.Tokens[0].Position.Start,
            SingleTokenLine stl => stl.Token.Position.Start,
            CodeBlockLine cbl => cbl.Token.Position.Start,
            _ => throw new ArgumentOutOfRangeException()
        };
        var linesEn = Raw.AsSpan().EnumerateLines();
        var hgtrfdews = 0;
        while (linesEn.MoveNext())
        {
            hgtrfdews += linesEn.Current.Length;
            lineNumber++;
            if (hgtrfdews >= pos)
                break;
        }

        return lineNumber + 1;
    }

    public (bool Exit, object? Result) RunLine(Line baseLine)
    {
        Debug.WriteLine(baseLine is TokenLine tokenLine
            ? Raw[tokenLine.Tokens[0].Position.Start..tokenLine.Tokens[^1].Position.End]
            : (
                baseLine is CodeBlockLine ? "CodeBlockLine" : "invalid line type?"));
        if (baseLine is CodeBlockLine codeBlockLine)
        {
            BlockStack.Push(new(false, false, null));
            var res = RunCodeBlock(codeBlockLine.Token);
            if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
            {
                return (true, res);
            }

            return (false, RCaronInsideEnum.NoReturnValue);
        }

        if (baseLine is not TokenLine line)
        {
            switch (baseLine.Type)
            {
                case LineType.ForLoop when baseLine is ForLoopLine forLoopLine:
                {
                    var falseI = 0;
                    RunLine(forLoopLine.Initializer);
                    while (SimpleEvaluateBool(forLoopLine.CallToken.Arguments[1]))
                    {
                        BlockStack.Push(new StackThing(true, false, null));
                        var res = RunCodeBlock(forLoopLine.Body);
                        if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                        {
                            if (res?.Equals(RCaronInsideEnum.Breaked) ?? false)
                                break;
                            if (res?.Equals(RCaronInsideEnum.Continued) ?? false)
                                continue;
                            return (true, res);
                        }

                        RunLine(forLoopLine.Iterator);
                    }

                    break;
                }
                case LineType.QuickForLoop when baseLine is ForLoopLine forLoopLine:
                {
                    var falseI = 0;
                    RunLine(forLoopLine.Initializer);
                    var scope = new StackThing(true, false, null);
                    while (SimpleEvaluateBool(forLoopLine.CallToken.Arguments[1]))
                    {
                        BlockStack.Push(scope);
                        var res = RunCodeBlock(forLoopLine.Body);
                        if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                        {
                            if (res?.Equals(RCaronInsideEnum.Breaked) ?? false)
                                break;
                            if (res?.Equals(RCaronInsideEnum.Continued) ?? false)
                                continue;
                            return (true, res);
                        }

                        RunLine(forLoopLine.Iterator);
                    }

                    break;
                }
                default:
                    throw new("invalid line");
            }

            return (false, RCaronInsideEnum.NoReturnValue);
        }

        switch (line.Type)
        {
            case LineType.VariableAssignment:
            {
                var variableName = ((VariableToken)line.Tokens[0]).Name;
                var obj = SimpleEvaluateExpressionHigh(line.Tokens.Segment(2..));
                SetVar(variableName, obj);
                Debug.WriteLine($"variable '{variableName}' set to '{obj}'");
                break;
            }
            case LineType.AssignerAssignment:
            {
                IAssigner assigner = null;
                if (line.Tokens[0] is DotGroupPosToken dotGroup)
                {
                    assigner = GetAssigner(dotGroup.Tokens);
                }
                else
                {
                    assigner = GetAssigner(MemoryMarshal.CreateSpan(ref line.Tokens[0], 1));
                }

                assigner.Assign(SimpleEvaluateExpressionHigh(line.Tokens.Segment(2..)));
                break;
            }
            case LineType.IfStatement when line.Tokens[0] is CallLikePosToken callToken:
            {
                ElseState = false;
                if (SimpleEvaluateBool(callToken.Arguments[0]))
                {
                    ElseState = true;
                    BlockStack.Push(new(false, false, null));
                    var res = RunCodeBlock((CodeBlockToken)line.Tokens[1]);
                    if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                    {
                        return (true, res);
                    }
                }

                break;
            }
            case LineType.ElseIfStatement when line.Tokens[1] is CallLikePosToken callToken:
            {
                if (!ElseState && SimpleEvaluateBool(callToken.Arguments[0]))
                {
                    ElseState = true;
                    BlockStack.Push(new(false, false, null));
                    var res = RunCodeBlock((CodeBlockToken)line.Tokens[2]);
                    if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                    {
                        return (true, res);
                    }
                }

                break;
            }
            case LineType.ElseStatement:
            {
                if (!ElseState)
                {
                    ElseState = true;
                    BlockStack.Push(new(false, false, null));
                    var res = RunCodeBlock((CodeBlockToken)line.Tokens[1]);
                    if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                    {
                        return (true, res);
                    }
                }

                break;
            }
            case LineType.WhileLoop when line.Tokens[0] is CallLikePosToken callToken:
            {
                var body = (CodeBlockToken)line.Tokens[1];
                while (SimpleEvaluateBool(callToken.Arguments[0]))
                {
                    BlockStack.Push(new StackThing(true, false, null));
                    var res = RunCodeBlock(body);
                    if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                    {
                        if (res?.Equals(RCaronInsideEnum.Breaked) ?? false)
                            break;
                        if (res?.Equals(RCaronInsideEnum.Continued) ?? false)
                            continue;
                        return (true, res);
                    }
                }

                break;
            }
            case LineType.DoWhileLoop when line.Tokens[0] is CallLikePosToken callToken:
            {
                var body = (CodeBlockToken)line.Tokens[1];
                do
                {
                    BlockStack.Push(new StackThing(true, false, null));
                    var res = RunCodeBlock(body);
                    if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                    {
                        if (res?.Equals(RCaronInsideEnum.Breaked) ?? false)
                            break;
                        if (res?.Equals(RCaronInsideEnum.Continued) ?? false)
                            continue;
                        return (true, res);
                    }
                } while (SimpleEvaluateBool(callToken.Arguments[0]));

                break;
            }
            case LineType.ForeachLoop when line.Tokens[0] is CallLikePosToken callToken:
            {
                var body = (CodeBlockToken)line.Tokens[1];
                var varName = ((VariableToken)callToken.Arguments[0][0]).Name;
                foreach (var item in (IEnumerable)SimpleEvaluateExpressionHigh(callToken.Arguments[0][2..])!)
                {
                    var scope = new LocalScope();
                    scope.SetVariable(varName, item);
                    BlockStack.Push(new StackThing(true, false, scope));
                    var res = RunCodeBlock(body);
                    if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                    {
                        if (res?.Equals(RCaronInsideEnum.Breaked) ?? false)
                            break;
                        if (res?.Equals(RCaronInsideEnum.Continued) ?? false)
                            continue;
                        return (true, res);
                    }
                }

                break;
            }
            case LineType.LoopLoop:
            {
                var body = (CodeBlockToken)line.Tokens[1];
                while (true)
                {
                    BlockStack.Push(new StackThing(true, false, null));
                    var res = RunCodeBlock(body);
                    if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                    {
                        if (res?.Equals(RCaronInsideEnum.Breaked) ?? false)
                            break;
                        if (res?.Equals(RCaronInsideEnum.Continued) ?? false)
                            continue;
                        return (true, res);
                    }
                }

                break;
            }
            case LineType.UnaryOperation:
            {
                var variableName = ((VariableToken)line.Tokens[0]).Name;
                switch (line.Tokens[1])
                {
                    case OperationPosToken { Operation: OperationEnum.UnaryIncrement }:
                        Horrors.AddTo(ref GetVarRef(variableName)!, (long)1);
                        break;
                    case OperationPosToken { Operation: OperationEnum.UnaryDecrement }:
                        SetVar(variableName,
                            Horrors.Subtract(SimpleEvaluateExpressionSingle(line.Tokens[0]).NotNull(), (long)1));
                        break;
                }

                break;
            }
            case LineType.BlockStuff:
                if (line.Tokens[0] is { Type: TokenType.BlockEnd })
                {
                    // if (!ReturnValue?.Equals(RCaronInsideEnum.Breaked) ?? false)
                    //     BlockStack.Pop();
                    BlockStack.Pop();
                    return (true, RCaronInsideEnum.NoReturnValue);
                }

                break;
            case LineType.Function:
            {
                string name;
                FunctionArgument[]? arguments = null;
                if (line.Tokens[1] is CallLikePosToken callToken)
                {
                    name = callToken.Name;
                    arguments = new FunctionArgument[callToken.Arguments.Length];
                    for (int i = 0; i < callToken.Arguments.Length; i++)
                    {
                        var cur = callToken.Arguments[i];
                        var argName = ((VariableToken)cur[0]).Name;
                        arguments[i] = new FunctionArgument(argName);
                        if (cur is [_, OperationPosToken { Operation: OperationEnum.Assignment }, ..])
                        {
                            arguments[i].DefaultValue = SimpleEvaluateExpressionHigh(cur[2..]);
                        }
                    }
                }
                else if (line.Tokens[1] is KeywordToken keywordToken)
                    name = keywordToken.String;
                else
                    throw new Exception("Invalid function name token");

                Functions[name] = new Function((CodeBlockToken)line.Tokens[2], arguments);
                break;
            }
            case LineType.KeywordCall when line.Tokens[0] is CallLikePosToken callToken:
            {
                MethodCall(callToken.Name, callToken: callToken, instance: null
                    // instanceTokens: MemoryMarshal.CreateSpan(ref callToken.OriginalToken, 1)
                );
                break;
            }
            case LineType.DotGroupCall:
            {
                EvaluateDotThings(((DotGroupPosToken)line.Tokens[0]).Tokens);
                break;
            }
            case LineType.SwitchStatement:
            {
                var switchValue = SimpleEvaluateExpressionHigh(((CallLikePosToken)line.Tokens[0]).Arguments[0]);
                var cases = (CodeBlockToken)line.Tokens[1];
                for (var i = 1; i < cases.Lines.Count - 1; i++)
                {
                    var caseLine = ((TokenLine)cases.Lines[i]);
                    var value = SimpleEvaluateExpressionSingle(caseLine.Tokens[0]);
                    if (caseLine.Tokens[0] is not KeywordToken { String: "default" } &&
                        ((switchValue == null && value == null) ||
                         (!switchValue?.Equals(value) ?? false)))
                        continue;
                    BlockStack.Push(new(true, false, null));
                    var res = RunCodeBlock((CodeBlockToken)caseLine.Tokens[1]);
                    if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                    {
                        return (true, res);
                    }

                    break;
                }

                break;
            }
            case LineType.KeywordPlainCall:
            {
                var keyword = (KeywordToken)line.Tokens[0];
                var keywordString = keyword.String;
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
                        return (true, RCaronInsideEnum.Breaked);
                    }
                    case "return":
                    {
                        var res = args.Length == 0
                            ? RCaronInsideEnum.ReturnWithoutValue
                            : SimpleEvaluateExpressionHigh(ArgsArray());
                        var g = BlockStack.Pop();
                        while (!g.IsReturnWorthy)
                            g = BlockStack.Pop();
                        return (true, res);
                    }
                    case "continue":
                    {
                        var g = BlockStack.Pop();
                        while (!g.IsBreakWorthy)
                            g = BlockStack.Pop();
                        return (true, RCaronInsideEnum.Continued);
                    }
                }

                if (Options.EnableDebugging)
                    switch (keywordString)
                    {
                        case "dbg_println":
                            Console.Debug(SimpleEvaluateExpressionHigh(ArgsArray()));
                            return (false, RCaronInsideEnum.NoReturnValue);
                        case "dbg_assert_is_one":
                            GlobalScope.SetVariable("$$assertResult",
                                SimpleEvaluateExpressionSingle(args[0]).Expect<long>() == 1);
                            return (false, RCaronInsideEnum.NoReturnValue);
                        case "dbg_sum_three":
                            GlobalScope.SetVariable("$$assertResult", Horrors.Sum(
                                Horrors.Sum(SimpleEvaluateExpressionSingle(args[0]).NotNull(),
                                    SimpleEvaluateExpressionSingle(args[1]).NotNull()),
                                SimpleEvaluateExpressionSingle(args[2]).NotNull()));
                            return (false, RCaronInsideEnum.NoReturnValue);
                        case "dbg_exit":
                            return (true, RCaronInsideEnum.NoReturnValue);
                        case "dbg_throw":
                            throw new("dbg_throw");
                    }

                if (Options.EnableDumb)
                    switch (keywordString)
                    {
                        case "goto_line":
                            curIndex = (int)SimpleEvaluateExpressionSingle(args[0]).Expect<long>();
                            return (false, RCaronInsideEnum.NoReturnValue);
                    }

                if (Functions.TryGetValue(keywordString, out var func))
                {
                    FunctionCall(func, null, line.Tokens.Segment(1..));
                    return (false, RCaronInsideEnum.NoReturnValue);
                }

                MethodCall(keywordString, line.Tokens.Segment(1..));
                break;
            }
            case LineType.TryBlock when line.Tokens[1] is CodeBlockToken codeBlockToken:
            {
                CodeBlockToken? catchBlock = null;
                CodeBlockToken? finallyBlock = null;
                if (Lines.Count - curIndex > 1 && Lines[curIndex + 1] is TokenLine
                    {
                        Type: LineType.CatchBlock
                    } catchBlockLine)
                    catchBlock = (CodeBlockToken)catchBlockLine.Tokens[1];
                else if (Lines.Count - curIndex > 1 && Lines[curIndex + 1] is TokenLine
                         {
                             Type: LineType.FinallyBlock
                         } finallyBlockLine)
                    finallyBlock = (CodeBlockToken)finallyBlockLine.Tokens[1];
                if (Lines.Count - curIndex > 2 && Lines[curIndex + 2] is TokenLine
                    {
                        Type: LineType.FinallyBlock
                    } finallyBlockLine2)
                    finallyBlock = (CodeBlockToken)finallyBlockLine2.Tokens[1];
                var pastIndex = curIndex;
                var pastLines = Lines;
                try
                {
                    BlockStack.Push(new StackThing(false, false, null));
                    var res = RunCodeBlock(codeBlockToken);
                    if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                        return (true, res);
                }
                catch (Exception exc)
                {
                    BlockStack.Pop();
                    if (catchBlock is not null)
                    {
                        var scope = new LocalScope();
                        scope.SetVariable("exception", exc);
                        BlockStack.Push(new StackThing(false, false, scope));
                        var res = RunCodeBlock(catchBlock);
                        if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                            return (true, res);
                    }
                    else
                        throw;
                }
                finally
                {
                    curIndex = pastIndex;
                    Lines = pastLines;
                    if (finallyBlock is not null)
                    {
                        BlockStack.Push(new StackThing(false, false, null));
                        var res = RunCodeBlock(finallyBlock);
                        if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                            throw new("cannot return from finally block");
                    }
                }

                break;
            }
        }

        return (false, RCaronInsideEnum.NoReturnValue);
    }

    public object? RunCodeBlock(CodeBlockToken codeBlock)
    {
        var prevIndex = curIndex;
        var prevLines = Lines;
        curIndex = 0;
        Lines = codeBlock.Lines;
        var r = Run();
        curIndex = prevIndex;
        Lines = prevLines;
        return r;
    }

    public object? MethodCall(string nameArg, ArraySegment<PosToken> argumentTokens = default,
        CallLikePosToken? callToken = null
        // , Span<PosToken> instanceTokens = default
        , object? instance = null
    )
    {
        // // lowercase the string if not all characters are lowercase
        // for (var i = 0; i < name.Length; i++)
        // {
        //     if (!char.IsLower(name[i]))
        //     {
        Span<char> name = stackalloc char[nameArg.Length];
        MemoryExtensions.ToLowerInvariant(nameArg, name);
        //         break;
        //     }
        // }

        object? At(in Span<PosToken> tokens, int index)
        {
            if (callToken != null)
                return SimpleEvaluateExpressionHigh(callToken.Arguments[index]);
            return SimpleEvaluateExpressionSingle(tokens[index]);
        }

        // todo(perf): could use a ref struct enumerator for when callToken != null but no idea what to do whet callToken == null
        object?[] All(in ArraySegment<PosToken> tokens)
        {
            if (tokens.Count == 0 && (callToken?.ArgumentsEmpty() ?? false))
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

        if (callToken?.Name == "@")
            return All(in argumentTokens);

        switch (name)
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
                var variableName = At(argumentTokens, 0).Expect<string>();
                var val = GlobalScope.GetVariable(variableName);
                if (val.Equals(RCaronInsideEnum.VariableNotFound))
                    throw RCaronException.VariableNotFound(variableName);
                return val;
            }
            case "globalset":
            {
                GlobalScope.SetVariable(At(argumentTokens, 0).Expect<string>(), At(argumentTokens, 1));
                return RCaronInsideEnum.NoReturnValue;
            }
            case "println":
                foreach (var arg in All(argumentTokens))
                    Console.WriteLine(arg);
                return RCaronInsideEnum.NoReturnValue;
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
                return RCaronInsideEnum.NoReturnValue;
            }
            case "open":
                FileScope.UsedNamespaces ??= new();
                FileScope.UsedNamespaces.AddRange(Array.ConvertAll(All(argumentTokens), t => t.Expect<string>()));
                return RCaronInsideEnum.NoReturnValue;
            case "open_ext":
                FileScope.UsedNamespacesForExtensionMethods ??= new();
                FileScope.UsedNamespacesForExtensionMethods.AddRange(Array.ConvertAll(All(argumentTokens),
                    t => t.Expect<string>()));
                return RCaronInsideEnum.NoReturnValue;
            case "throw":
                throw (Exception)At(argumentTokens, 0);
        }

        if (Functions.TryGetValue(nameArg, out var func))
            return FunctionCall(func, callToken, argumentTokens);

        if (name[0] == '#' || instance != null)
        {
            var args = All(in argumentTokens);
            Type? type = null;
            string methodName;
            object? target = null;
            object? variable = null;
            if (instance != null)
            {
                var obj = instance;
                variable = obj;
                if (obj is RCaronType rCaronType)
                    type = rCaronType.Type;
                else
                {
                    target = obj;
                    type = obj.GetType();
                }

                goto resolveMethod;
            }

            resolveMethod: ;
            var methodsOrg = type.GetMethods()!;
            var methodsLength = methodsOrg.Length;
            var arr = ArrayPool<MethodBase>.Shared.Rent(methodsLength);
            var count = 0;
            for (var i = 0; i < methodsLength; i++)
            {
                var method = methodsOrg[i];
                if (!MemoryExtensions.Equals(method.Name, name, StringComparison.InvariantCultureIgnoreCase))
                    continue;
                arr[count++] = method;
            }

            var methods = arr.Segment(..count);
            // constructors
            if (MemoryExtensions.Equals(name, "new", StringComparison.InvariantCultureIgnoreCase))
            {
                var foundMethods = methods.ToList();
                foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                {
                    foundMethods.Add(constructor);
                }

                methods = foundMethods.ToArray();
            }

            if (methods.Count == 0)
            {
                var foundMethods = new List<MethodBase>();
                // extension methods
                args = args.Prepend(variable!).ToArray();
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
                            if (MemoryExtensions.Equals(method.Name, name,
                                    StringComparison.InvariantCultureIgnoreCase) &&
                                (FileScope.UsedNamespacesForExtensionMethods?.Contains(exportedType.Namespace!) ??
                                 false))
                            {
                                foundMethods.Add(method);
                            }
                        }
                    }
                }

                methods = foundMethods.ToArray();
            }

            Span<uint> scores = stackalloc uint[methods.Count];
            Span<bool> needsNumericConversions = stackalloc bool[methods.Count];
            for (var i = 0; i < methods.Count; i++)
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
                    // todo: support actual generic parameters constraints
                    else if (parameters[j].ParameterType.IsGenericType
                             && ListEx.IsAssignableToGenericType(args[j].GetType(),
                                 parameters[j].ParameterType.GetGenericTypeDefinition()))
                        // parameters[j].ParameterType.GetGenericParameterConstraints()
                    {
                        score += 10;
                    }
                    else if (parameters[j].ParameterType.IsNumericType() && args[j].GetType().IsNumericType())
                    {
                        score += 10;
                        needsNumericConversions[i] = true;
                    }
                    else if (parameters[j].ParameterType.IsGenericParameter)
                    {
                        score += 5;
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
                throw new RCaronException($"cannot find a match for method '{name}'",
                    ExceptionCode.MethodNoSuitableMatch);

            var bestMethod = methods[bestIndex];
            // attention: do not use arr or methods after this point
            ArrayPool<MethodBase>.Shared.Return(arr, true);

            // mismatch count arguments -> equate it out with default values
            // is equate even a word?
            var paramss = bestMethod.GetParameters();
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
            // numeric conversions
            if (needsNumericConversions[bestIndex])
            {
                for (var i = 0; i < args.Length; i++)
                {
                    if(paramss[i].ParameterType.IsNumericType() && args[i].GetType().IsNumericType())
                        args[i] = Convert.ChangeType(args[i], paramss[i].ParameterType);
                }
            }

            // generic method handling aaa
            if (bestMethod.IsGenericMethod)
            {
                // todo(perf): probably not the fastest
                var t = bestMethod.DeclaringType!;
                // static class
                if (t.IsSealed && t.IsAbstract)
                {
                    var staticContext = InvokeContext.CreateStatic;
                    if (bestMethod is MethodInfo mi && mi.ReturnType == typeof(void))
                    {
                        Dynamic.InvokeMemberAction(target, bestMethod.Name, args);
                        return RCaronInsideEnum.NoReturnValue;
                    }

                    return Dynamic.InvokeMember(staticContext(t), bestMethod.Name, args);
                }
                else
                {
                    if (bestMethod is MethodInfo mi && mi.ReturnType == typeof(void))
                    {
                        Dynamic.InvokeMemberAction(target, bestMethod.Name, args);
                        return RCaronInsideEnum.NoReturnValue;
                    }

                    return Dynamic.InvokeMember(target, bestMethod.Name, args);
                }
            }

            if (bestMethod is ConstructorInfo constructorInfo)
            {
                return constructorInfo.Invoke(args);
            }

            return bestMethod.Invoke(target, args);
        }

        foreach (var module in Modules)
        {
            var v = module.RCaronModuleRun(name, this, argumentTokens, callToken);
            if (!v?.Equals(RCaronInsideEnum.MethodNotFound) ?? false)
                return v;
        }

        throw new RCaronException($"method '{name}' not found", ExceptionCode.MethodNotFound);
    }

    public IAssigner GetAssigner(Span<PosToken> tokens)
    {
        object? val = null;
        Type type = null;
        if (tokens.Length != 1)
        {
            val = EvaluateDotThings(tokens[..^1]);
            type = val.GetType();
        }

        if (val is RCaronType rCaronType)
            type = rCaronType.Type;

        var last = tokens[^1];
        if (last.Type == TokenType.Keyword || (tokens.Length == 1 && tokens[0].Type == TokenType.ExternThing))
        {
            string str;
            if (last is KeywordToken keywordToken)
            {
                if (val is ClassInstance classInstance)
                {
                    if (classInstance.PropertyValues == null)
                        throw new RCaronException("Class has no properties", RCaronExceptionCode.ClassNoProperties);
                    var i = classInstance.GetPropertyIndex(keywordToken.String);
                    if (i != -1)
                        return new ClassAssigner(classInstance, i);
                    throw new RCaronException($"Class property of name '{keywordToken.String}' not found",
                        RCaronExceptionCode.ClassPropertyNotFound);
                }

                str = keywordToken.String;
            }
            else if (tokens[0] is ExternThingToken ett)
            {
                var name = ett.String;
                str = name[(name.LastIndexOf('.') + 1)..];
            }
            else
            {
                throw new();
            }

            var p = type!.GetProperty(str,
                BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Static);
            if (p != null)
                return new PropertyAssigner(p, val);

            var f = type.GetField(str,
                BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Static);
            if (f != null)
                return new FieldAssigner(f, val);
        }

        if (val is IList iList && last is IndexerToken arrayAccessorToken)
        {
            return new InterfaceListAssigner(iList, arrayAccessorToken, this);
        }

        throw new Exception("unsupported stuff for GetAssigner");
    }

    public object? EvaluateDotThings(Span<PosToken> instanceTokens)
    {
        if (instanceTokens.Length == 1 && instanceTokens[0] is DotGroupPosToken dotGroupPosToken)
            instanceTokens = dotGroupPosToken.Tokens;
        var val = SimpleEvaluateExpressionSingle(instanceTokens[0]);
        if (val == null)
            throw RCaronException.NullInTokens(instanceTokens, Raw, 0);
        Type? type;
        if (val is RCaronType rCaronType)
            type = rCaronType.Type;
        else if (val is ClassDefinition)
            type = null;
        else
            type = val.GetType();

        for (int i = 1; i < instanceTokens.Length; i++)
        {
            if (instanceTokens[i].Type == TokenType.Dot || instanceTokens[i].Type == TokenType.Colon)
                continue;
            if (i != instanceTokens.Length - 1 && val == null)
                throw RCaronException.NullInTokens(instanceTokens, Raw, i);
            if (instanceTokens[i] is IndexerToken arrayAccessorToken)
            {
                var evaluated = SimpleEvaluateExpressionHigh(arrayAccessorToken.Tokens);

                if (FileScope.IndexerImplementations != null)
                {
                    var broke = false;
                    for (var j = 0; j < FileScope.IndexerImplementations.Count; j++)
                    {
                        if (FileScope.IndexerImplementations[j].Do(this, evaluated, ref val, ref type))
                        {
                            broke = true;
                            break;
                        }
                    }

                    if (broke)
                        continue;
                }

                if (val is IDictionary dict)
                {
                    var args = type.GetGenericArguments();
                    var keyType = args[0];
                    val = dict[Convert.ChangeType(evaluated, keyType)!];
                    type = val?.GetType();
                    continue;
                }

                // todo(perf): for some reason non-throwing Convert methods don't exist
                object? asInt;
                try
                {
                    asInt = Convert.ChangeType(evaluated, typeof(int));
                }
                catch (FormatException)
                {
                    asInt = null;
                }

                if (asInt != null)
                {
                    var intIndex = (int)asInt;
                    if (val is IList list)
                    {
                        val = list[intIndex];
                        type = val?.GetType();
                        continue;
                    }

                    if (val is Array array)
                    {
                        val = array.GetValue(intIndex);
                        type = val?.GetType();
                        continue;
                    }
                }

                throw new RCaronException("could not get array accessor",
                    RCaronExceptionCode.NoSuitableIndexerImplementation);
            }

            if (instanceTokens[i] is CallLikePosToken callLikePosToken)
            {
                if (val is ClassDefinition classDefinition &&
                    callLikePosToken.Name.Equals("new", StringComparison.InvariantCultureIgnoreCase))
                {
                    var classInstance = new ClassInstance(classDefinition);
                    val = classInstance;
                    if (classDefinition.PropertyInitializers != null)
                        for (var j = 0; j < classDefinition.PropertyInitializers.Length; j++)
                        {
                            if (classDefinition.PropertyInitializers[j] != null)
                                classInstance.PropertyValues![j] =
                                    SimpleEvaluateExpressionHigh(classDefinition.PropertyInitializers[j]!);
                        }

                    type = null;
                    continue;
                }
                else if (val is ClassInstance classInstance)
                {
                    var func = classInstance.Definition.Functions?[callLikePosToken.Name];
                    if (func == null)
                        throw new RCaronException($"Class function '{callLikePosToken.Name}' not found",
                            RCaronExceptionCode.ClassFunctionNotFound);
                    BlockStack.Push(new StackThing(false, true, new ClassFunctionScope(classInstance)));
                    val = RunCodeBlock(func);
                    type = val?.GetType();
                    continue;
                }

                var d = MethodCall(callLikePosToken.Name, callToken: callLikePosToken, instance: val);
                val = d;
                type = val?.GetType();
                continue;
            }

            if (val is ClassInstance classInstance1)
            {
                val = classInstance1.PropertyValues[
                    classInstance1.GetPropertyIndex(((KeywordToken)instanceTokens[i]).String)];
                type = val?.GetType();
                continue;
            }

            var str = instanceTokens[i] switch
            {
                KeywordToken keywordToken => keywordToken.String,
                // ExternThingToken externThingToken => externThingToken.String,
                _ => throw new Exception("unsupported stuff for EvaluateDotThings")
            };

            if (FileScope.PropertyAccessors != null)
            {
                var broke = false;
                for (var j = 0; j < FileScope.PropertyAccessors.Count; j++)
                {
                    if (FileScope.PropertyAccessors[j].Do(this, str, ref val, ref type))
                    {
                        broke = true;
                        break;
                    }
                }

                if (broke)
                    continue;
            }

            var instanceOrStatic = val is RCaronType ? BindingFlags.Static : BindingFlags.Instance;
            var p = type!.GetProperty(str,
                BindingFlags.Public | BindingFlags.IgnoreCase | instanceOrStatic);
            if (p != null)
            {
                val = p.GetValue(val);
                type = val?.GetType();
                continue;
            }

            var f = type.GetField(str, BindingFlags.Public | BindingFlags.IgnoreCase | instanceOrStatic);
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

            throw new RCaronException(
                $"cannot resolve '{str}'(index={i}) in '{Raw[instanceTokens[0].Position.Start..instanceTokens[^1].Position.End]}'",
                ExceptionCode.CannotResolveInDotThing);
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
            case "current_motor":
                return this;
        }

        var val = GetVar(name);
        if (val?.Equals(RCaronInsideEnum.VariableNotFound) ?? false)
            throw RCaronException.VariableNotFound(name);
        return val;
    }

    public object? SimpleEvaluateExpressionSingle(PosToken token)
    {
        if (token is ConstToken constToken)
            return constToken.Value;
        if (token is VariableToken variableToken)
            return EvaluateVariable(variableToken.Name);
        switch (token.Type)
        {
            case TokenType.DumbShit when token is MathValueGroupPosToken valueGroupPosToken:
                return SimpleEvaluateExpressionValue(valueGroupPosToken.ValueTokens);
            case TokenType.KeywordCall when token is CallLikePosToken callToken:
                return MethodCall(callToken.Name, callToken: callToken);
            case TokenType.CodeBlock when token is CodeBlockToken codeBlockToken:
                BlockStack.Push(new(false, true, null));
                return RunCodeBlock(codeBlockToken);
            case TokenType.Keyword when token is KeywordToken keywordToken:
                return keywordToken.String;
            case TokenType.DotGroup:
                return EvaluateDotThings(MemoryMarshal.CreateSpan(ref token, 1));
            case TokenType.ExternThing when token is ExternThingToken externThingToken:
            {
                var name = externThingToken.String;
                // try RCaron ClassDefinition
                if (ClassDefinitions != null)
                    for (var i = 0; i < ClassDefinitions.Count; i++)
                        if (name.Equals(ClassDefinitions[i].Name, StringComparison.InvariantCultureIgnoreCase))
                            return ClassDefinitions[i];

                // get Type via TypeResolver
                var type = TypeResolver.FindType(externThingToken.String, FileScope)!;
                if (type == null)
                    throw RCaronException.TypeNotFound(externThingToken.String);
                return new RCaronType(type);
            }
            case TokenType.EqualityOperationGroup when token is ComparisonValuePosToken comparisonValuePosToken:
                return EvaluateComparisonOperation(comparisonValuePosToken);
            case TokenType.LogicalOperationGroup
                when token is LogicalOperationValuePosToken logicalOperationValuePosToken:
                return EvaluateLogicalOperation(logicalOperationValuePosToken);
        }

        throw new Exception($"invalid tokentype to evaluate: {token.Type}");
    }

    [CollectionAccess(CollectionAccessType.Read)]
    public object SimpleEvaluateExpressionValue(ArraySegment<PosToken> tokens)
    {
        // repeat action something math
        var index = 0;
        object value = SimpleEvaluateExpressionSingle(tokens[0])!;
        if (tokens[1] is IndexerToken)
        {
        }

        while (index < tokens.Count - 1)
        {
            var op = (ValueOperationValuePosToken)tokens[index + 1];
            var second = SimpleEvaluateExpressionSingle(tokens[index + 2])!;
            switch (op.Operation)
            {
                case OperationEnum.Sum:
                    Horrors.AddTo(ref value, second);
                    break;
                case OperationEnum.Subtract:
                    value = Horrors.Subtract(value, second);
                    break;
                case OperationEnum.Multiply:
                    value = Horrors.Multiply(value, second);
                    break;
                case OperationEnum.Divide:
                    value = Horrors.Divide(value, second);
                    break;
                case OperationEnum.Modulo:
                    value = Horrors.Modulo(value, second);
                    break;
                case OperationEnum.Range:
                {
                    var long1 = value as long?;
                    var long2 = second as long?;
                    if (!long1.HasValue)
                        long1 = Convert.ToInt64(value);
                    if (!long2.HasValue)
                        long2 = Convert.ToInt64(second);
                    value = new RCaronRange(long1.Value, long2.Value);
                    break;
                }
                default:
                    throw new Exception($"invalid operation: {op}");
            }

            index += 2;
        }

        return value;
    }

    public object? SimpleEvaluateExpressionHigh(ArraySegment<PosToken> tokens)
        => tokens.Count switch
        {
            > 0 when tokens[0] is KeywordToken keywordToken => MethodCall(keywordToken.String,
                argumentTokens: tokens.Segment(1..)),
            1 => SimpleEvaluateExpressionSingle(tokens[0]),
            > 2 => SimpleEvaluateExpressionValue(tokens),
            _ => throw new Exception("what he fuck")
        };

    public object? FunctionCall(Function function, CallLikePosToken? callToken = null,
        ArraySegment<PosToken> argumentTokens = default)
    {
        LocalScope scope = null;
        if (function.Arguments != null)
        {
            scope ??= new();
            Span<bool> assignedArguments = stackalloc bool[function.Arguments.Length];
            var enumerator = callToken != null
                ? new ArgumentEnumerator(callToken)
                : new ArgumentEnumerator(argumentTokens);
            while (enumerator.MoveNext())
            {
                if (enumerator.CurrentName != null)
                {
                    var index = 0;
                    for (; index < function.Arguments.Length; index++)
                    {
                        if (function.Arguments[index].Name.SequenceEqual(enumerator.CurrentName))
                            break;
                        else if (index == function.Arguments.Length - 1)
                            throw RCaronException.NamedArgumentNotFound(enumerator.CurrentName);
                    }

                    scope.Variables ??= new();
                    scope.Variables[function.Arguments[index].Name] =
                        SimpleEvaluateExpressionHigh(enumerator.CurrentTokens);
                    assignedArguments[index] = true;
                }
                else if (!enumerator.HitNamedArgument)
                {
                    scope.Variables ??= new();
                    for (var i = 0; i < function.Arguments.Length; i++)
                    {
                        if (!scope.Variables.ContainsKey(function.Arguments[i].Name))
                        {
                            scope.Variables[function.Arguments[i].Name] =
                                SimpleEvaluateExpressionHigh(enumerator.CurrentTokens);
                            assignedArguments[i] = true;
                            break;
                        }
                        else if (i == function.Arguments.Length - 1)
                            throw RCaronException.LeftOverPositionalArgument();
                    }
                }
                else
                    throw new RCaronException("hit positional argument after a named one",
                        RCaronExceptionCode.PositionalArgumentAfterNamedArgument);
            }

            // assign default values
            for (var i = 0; i < function.Arguments.Length; i++)
            {
                if (!assignedArguments[i])
                {
                    scope.Variables ??= new();
                    scope.Variables[function.Arguments[i].Name] = function.Arguments[i].DefaultValue;
                }
            }

            // check if all arguments are assigned
            for (var i = 0; i < function.Arguments.Length; i++)
            {
                if (function.Arguments[i].DefaultValue?.Equals(RCaronInsideEnum.NoDefaultValue) ?? false)
                {
                    if (!assignedArguments[i])
                        throw RCaronException.ArgumentsLeftUnassigned();
                }
            }
        }


        BlockStack.Push(new StackThing(false, true, scope));
        return RunCodeBlock(function.CodeBlock);
    }

    public bool SimpleEvaluateBool(PosToken[] tokens)
        => tokens switch
        {
            { Length: 1 } when tokens[0] is ComparisonValuePosToken comparisonToken => EvaluateComparisonOperation(
                comparisonToken),
            { Length: 1 } when tokens[0] is ValuePosToken => (bool)SimpleEvaluateExpressionSingle(tokens[0])!,
            _ => throw new Exception("what he fuck")
        };

    public bool EvaluateComparisonOperation(ComparisonValuePosToken comparisonValuePosToken)
    {
        var val1 = SimpleEvaluateExpressionSingle(comparisonValuePosToken.Left);
        var val2 = SimpleEvaluateExpressionSingle(comparisonValuePosToken.Right);
        var op = comparisonValuePosToken.ComparisonToken;
        switch (op.Operation)
        {
            case OperationEnum.IsEqual:
                return val1.Equals(val2);
            case OperationEnum.IsNotEqual:
                return !val1.Equals(val2);
            case OperationEnum.IsGreater:
                return Horrors.IsGreater(val1, val2);
            case OperationEnum.IsGreaterOrEqual:
                return val1.Equals(val2) || Horrors.IsGreater(val1, val2);
            case OperationEnum.IsLess:
                return !val1.Equals(val2) && Horrors.IsLess(val1, val2);
            case OperationEnum.IsLessOrEqual:
                return val1.Equals(val2) || Horrors.IsLess(val1, val2);
            default:
                throw new RCaronException($"unknown operator: {op}", ExceptionCode.UnknownOperator);
        }
    }

    public bool EvaluateLogicalOperation(LogicalOperationValuePosToken comparisonValuePosToken)
    {
        var val1 = (bool)SimpleEvaluateExpressionSingle(comparisonValuePosToken.Left)!;
        var val2 = (bool)SimpleEvaluateExpressionSingle(comparisonValuePosToken.Right)!;
        var op = comparisonValuePosToken.ComparisonToken;
        switch (op.Operation)
        {
            case OperationEnum.And:
                return val1 && val2;
            case OperationEnum.Or:
                return val1 || val2;
            default:
                throw new RCaronException($"unknown operator: {op}", ExceptionCode.UnknownOperator);
        }
    }

    public object? GetVar(string name)
    {
        for (var i = 0; i < BlockStack.Count; i++)
        {
            var el = BlockStack.At(^(i + 1));
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
            var el = BlockStack.At(^(i + 1));
            if (el.Scope != null)
            {
                ref object? reference = ref el.Scope.GetVariableRef(name);
                if (Unsafe.IsNullRef(ref reference))
                    continue;
                return ref reference;
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
            var el = BlockStack.At(^(i + 1));
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