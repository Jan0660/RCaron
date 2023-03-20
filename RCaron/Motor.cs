using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Dynamitey;
using JetBrains.Annotations;
using RCaron.BaseLibrary;
using RCaron.Binders;
using RCaron.Classes;
using RCaron.Parsing;
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
}

public class Motor
{
    public IList<Line> Lines { get; set; }

    public class StackThing
    {
        public StackThing(bool isBreakWorthy, bool isReturnWorthy,
            LocalScope? scope, FileScope fileScope, Line? lineForTrace = null)
        {
            this.IsBreakWorthy = isBreakWorthy;
            this.IsReturnWorthy = isReturnWorthy;
            this.Scope = scope;
            this.FileScope = fileScope;
            this.LineForTrace = lineForTrace;
        }

        public bool IsBreakWorthy { get; init; }
        public bool IsReturnWorthy { get; init; }
        public LocalScope? Scope { get; set; }
        public FileScope FileScope { get; init; }
        public Line? LineForTrace { get; init; }
    }

    public NiceStack<StackThing> BlockStack { get; set; } = new();
    public FileScope MainFileScope { get; set; }
    public MotorOptions Options { get; }

    [UsedImplicitly]
    internal Func<Motor, string, ArraySegment<PosToken>, FileScope, object?>? InvokeRunExecutable { get; set; } = null;

    /// <summary>
    /// If true and meets an else(if), it will be skipped.
    /// </summary>
    public bool ElseState { get; set; } = false;

    public LocalScope GlobalScope { get; set; } = new();

#pragma warning disable CS8618
    public Motor(RCaronParserContext parserContext, MotorOptions? options = null)
#pragma warning restore CS8618
    {
        UseContext(parserContext);
        Options = options ?? new();
        // Modules = new List<IRCaronModule>(1) { new LoggingModule() };
    }

    public void UseContext(RCaronParserContext parserContext, bool withFileScope = true)
    {
#pragma warning disable CS8601
        Lines = parserContext.FileScope?.Lines;
#pragma warning restore CS8601
        if (withFileScope && parserContext.FileScope != null!)
        {
            MainFileScope = parserContext.FileScope;
            MainFileScope.Modules ??= new List<IRCaronModule>(1) { new LoggingModule(), new ExperimentalModule() };
        }
    }

    /// <summary>
    /// current line index
    /// </summary>
    private int _curIndex;

    public int CurrentLineIndex => _curIndex;

    public object? Run(int startIndex = 0)
    {
        _curIndex = startIndex;
        for (; _curIndex < Lines.Count; _curIndex++)
        {
            if (_curIndex >= Lines.Count)
                break;
            var line = Lines[_curIndex];
            var res = RunLine(line);
            if (res.Exit)
                return res.Result;
        }

        return RCaronInsideEnum.NoReturnValue;
    }

    public int GetLineNumber(FileScope? fileScope = null, int? position = null)
    {
        fileScope ??= GetFileScope();
        var lineNumber = 1;
        var pos = position ?? Lines[_curIndex] switch
        {
            TokenLine tl => tl.Tokens[0].Position.Start,
            SingleTokenLine stl => stl.Token.Position.Start,
            CodeBlockLine cbl => cbl.Token.Position.Start,
            _ => throw new ArgumentOutOfRangeException()
        };
        var raw = fileScope.Raw.AsSpan();
        for (var i = 0; i < pos; i++)
        {
            if (raw[i] == '\n')
                lineNumber++;
        }

        return lineNumber;
    }

    public (bool Exit, object? Result) RunLine(Line baseLine)
    {
        if (baseLine is CodeBlockLine codeBlockLine)
        {
            BlockStack.Push(new(false, false, null, GetFileScope()));
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
                    RunLine(forLoopLine.Initializer);
                    while (SimpleEvaluateBool(forLoopLine.CallToken.Arguments[1]))
                    {
                        BlockStack.Push(new StackThing(true, false, null, GetFileScope()));
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
                    RunLine(forLoopLine.Initializer);
                    var scope = new StackThing(true, false, null, GetFileScope());
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
            case LineType.LetVariableAssignment:
            {
                var variableName = ((VariableToken)line.Tokens[1]).Name;
                var obj = SimpleEvaluateExpressionHigh(line.Tokens.Segment(3..));
                SetVar(variableName, new LetVariableValue(obj?.GetType() ?? typeof(object), obj));
                Debug.WriteLine($"let-variable '{variableName}' set to '{obj}'");
                break;
            }
            case LineType.AssignerAssignment:
            {
                IAssigner assigner;
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
                    BlockStack.Push(new(false, false, null, GetFileScope()));
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
                    BlockStack.Push(new(false, false, null, GetFileScope()));
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
                    BlockStack.Push(new(false, false, null, GetFileScope()));
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
                    BlockStack.Push(new StackThing(true, false, null, GetFileScope()));
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
                    BlockStack.Push(new StackThing(true, false, null, GetFileScope()));
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
                    BlockStack.Push(new StackThing(true, false, scope, GetFileScope()));
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
                    BlockStack.Push(new StackThing(true, false, null, GetFileScope()));
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
                    BlockStack.Push(new(true, false, null, GetFileScope()));
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

                if (TryGetFunction(keywordString, GetFileScope(), out var func))
                {
                    FunctionCall(func, null, line.Tokens.Segment(1..));
                    return (false, RCaronInsideEnum.NoReturnValue);
                }

                MethodCall(keywordString, line.Tokens.Segment(1..));
                break;
            }
            case LineType.PathCall when line.Tokens[0] is ConstToken pathToken:
            {
                if (InvokeRunExecutable == null)
                    throw new("InvokeRunExecutable is null");
                InvokeRunExecutable(this, (string)pathToken.Value, line.Tokens.Segment(1..), GetFileScope());
                break;
            }
            case LineType.TryBlock when line.Tokens[1] is CodeBlockToken codeBlockToken:
            {
                CodeBlockToken? catchBlock = null;
                CodeBlockToken? finallyBlock = null;
                if (Lines.Count - _curIndex > 1 && Lines[_curIndex + 1] is TokenLine
                    {
                        Type: LineType.CatchBlock
                    } catchBlockLine)
                    catchBlock = (CodeBlockToken)catchBlockLine.Tokens[1];
                else if (Lines.Count - _curIndex > 1 && Lines[_curIndex + 1] is TokenLine
                         {
                             Type: LineType.FinallyBlock
                         } finallyBlockLine)
                    finallyBlock = (CodeBlockToken)finallyBlockLine.Tokens[1];
                if (Lines.Count - _curIndex > 2 && Lines[_curIndex + 2] is TokenLine
                    {
                        Type: LineType.FinallyBlock
                    } finallyBlockLine2)
                    finallyBlock = (CodeBlockToken)finallyBlockLine2.Tokens[1];
                var pastIndex = _curIndex;
                var pastLines = Lines;
                try
                {
                    BlockStack.Push(new StackThing(false, false, null, GetFileScope()));
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
                        BlockStack.Push(new StackThing(false, false, scope, GetFileScope()));
                        var res = RunCodeBlock(catchBlock);
                        if (!res?.Equals(RCaronInsideEnum.NoReturnValue) ?? true)
                            return (true, res);
                    }
                    else
                        throw;
                }
                finally
                {
                    _curIndex = pastIndex;
                    Lines = pastLines;
                    if (finallyBlock is not null)
                    {
                        BlockStack.Push(new StackThing(false, false, null, GetFileScope()));
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
        => RunLinesList(codeBlock.Lines);

    public object? RunLinesList(IList<Line> lines)
    {
        var prevIndex = _curIndex;
        var prevLines = Lines;
        _curIndex = 0;
        Lines = lines;
        var r = Run();
        _curIndex = prevIndex;
        Lines = prevLines;
        return r;
    }

    public object? MethodCall(string nameArg, ArraySegment<PosToken> argumentTokens = default,
        CallLikePosToken? callToken = null
        // , Span<PosToken> instanceTokens = default
        , object? instance = null
    )
    {
        FileScope? fileScope = null;
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
                return At(argumentTokens, 0)?.ToString()!;
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
                if (val?.Equals(RCaronInsideEnum.VariableNotFound) ?? false)
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
            {
                var f = GetFileScope();
                f.UsedNamespaces ??= new();
                f.UsedNamespaces.AddRange(Array.ConvertAll(All(argumentTokens), t => t.Expect<string>()));
                return RCaronInsideEnum.NoReturnValue;
            }
            case "open_ext":
            {
                var f = GetFileScope();
                f.UsedNamespacesForExtensionMethods ??= new();
                f.UsedNamespacesForExtensionMethods.AddRange(Array.ConvertAll(All(argumentTokens),
                    t => t.Expect<string>()));
                return RCaronInsideEnum.NoReturnValue;
            }
            case "throw":
                throw At(argumentTokens, 0).Expect<Exception>();
            case "range":
            {
                var long1 = At(argumentTokens, 0).Expect<long>();
                var long2 = At(argumentTokens, 1).Expect<long>();
                return new RCaronRange(long1, long2);
            }
        }

        if (TryGetFunction(nameArg, (fileScope ??= GetFileScope()), out var func))
            return FunctionCall(func, callToken, argumentTokens);

        if (name[0] == '#' || instance != null)
        {
            var args = All(in argumentTokens);
            Type? type = null;
            object? target = null;
            if (instance != null)
            {
                var obj = instance;
                if (obj is RCaronType rCaronType)
                    type = rCaronType.Type;
                else
                {
                    target = obj;
                    type = obj.GetType();
                }
            }

            var (bestMethod, needsNumericConversion, isExtensionMethod) =
                MethodResolver.Resolve(name, type, (fileScope ??= GetFileScope()), instance, args);

            if (isExtensionMethod)
            {
                var argsNew = new object?[args.Length + 1];
                Array.Copy(args, 0, argsNew, 1, args.Length);
                argsNew[0] = target;
                args = argsNew;
            }

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
            if (needsNumericConversion)
                for (var i = 0; i < args.Length; i++)
                    args[i] = Convert.ChangeType(args[i], paramss[i].ParameterType);

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

        if (((fileScope ??= GetFileScope()).Modules) != null)
        {
            Debug.Assert(fileScope.Modules != null, "fileScope.Modules != null");
            foreach (var module in fileScope.Modules)
            {
                var v = module.RCaronModuleRun(name, this, argumentTokens, callToken);
                if (!v?.Equals(RCaronInsideEnum.MethodNotFound) ?? true)
                    return v;
            }
        }

        if (InvokeRunExecutable != null && instance == null && callToken == null)
        {
            var ret = InvokeRunExecutable(this, name.ToString(), argumentTokens, fileScope ??= GetFileScope());
            if (!ret?.Equals(RCaronInsideEnum.MethodNotFound) ?? true)
                return ret;
        }

        throw new RCaronException($"method '{name}' not found", ExceptionCode.MethodNotFound);
    }

    public IAssigner GetAssigner(Span<PosToken> tokens)
    {
        object? val = null;
        Type type = null!;
        if (tokens.Length != 1)
        {
            val = EvaluateDotThings(tokens[..^1]);
            type = val?.GetType() ?? typeof(object);
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
                        throw RCaronException.ClassPropertyNotFound();
                    var i = classInstance.GetPropertyIndex(keywordToken.String);
                    if (i != -1)
                        return new ClassAssigner(classInstance, i);
                    throw RCaronException.ClassPropertyNotFound(keywordToken.String);
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

            var p = type.GetProperty(str,
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

    public static string EvaluateDotThingsAsPath(ReadOnlySpan<PosToken> tokens)
    {
        var path = new StringBuilder(128);
        foreach (var token in tokens)
        {
            if (token is DotGroupPosToken dotGroupPosToken)
            {
                path.Append(EvaluateDotThingsAsPath(dotGroupPosToken.Tokens));
                continue;
            }

            if (token is KeywordToken keywordToken)
            {
                path.Append(keywordToken.String);
                continue;
            }

            if (token is ConstToken { Type: TokenType.Path } constToken)
            {
                path.Append((string)constToken.Value);
                continue;
            }

            throw new Exception("unsupported stuff for EvaluateDotThingsAsPath");
        }

        return path.ToString();
    }

    public object? EvaluateDotThings(Span<PosToken> instanceTokens)
    {
        if (instanceTokens.Length == 1 && instanceTokens[0] is DotGroupPosToken dotGroupPosToken)
            instanceTokens = dotGroupPosToken.Tokens;
        var val = SimpleEvaluateExpressionSingle(instanceTokens[0]);
        if (val == null)
            throw RCaronException.NullInTokens(instanceTokens, GetFileScope().Raw, 0);
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
                throw RCaronException.NullInTokens(instanceTokens, GetFileScope().Raw, i);
            if (instanceTokens[i] is IndexerToken arrayAccessorToken)
            {
                var evaluated = SimpleEvaluateExpressionHigh(arrayAccessorToken.Tokens);

                arrayAccessorToken.CallSite ??=
                    CallSite<Func<CallSite, object?, object?, object?>>.Create(
                        new RCaronGetIndexBinder(new CallInfo(1), GetFileScope(), this));
                val = arrayAccessorToken.CallSite.Target(arrayAccessorToken.CallSite, val, evaluated);
                type = val?.GetType();
                continue;
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
                        throw RCaronException.ClassFunctionNotFound(callLikePosToken.Name);
                    val = FunctionCall(func, callLikePosToken, classInstance: classInstance);
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
                if (classInstance1.PropertyValues == null)
                    throw RCaronException.ClassPropertyNotFound();
                var name = ((KeywordToken)instanceTokens[i]).String;
                var index = classInstance1.GetPropertyIndex(name);
                if (index == -1)
                    throw RCaronException.ClassPropertyNotFound(name);
                val = classInstance1.PropertyValues[index];
                type = val?.GetType();
                continue;
            }

            var str = instanceTokens[i] switch
            {
                KeywordToken keywordToken => keywordToken.String,
                // ExternThingToken externThingToken => externThingToken.String,
                _ => throw new Exception("unsupported stuff for EvaluateDotThings")
            };

            var fileScopee = GetFileScope();
            if (fileScopee.PropertyAccessors != null)
            {
                var broke = false;
                for (var j = 0; j < fileScopee.PropertyAccessors.Count; j++)
                {
                    if (fileScopee.PropertyAccessors[j].Do(this, str, ref val, ref type))
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
                $"cannot resolve '{str}'(index={i}) in '{GetFileScope().Raw[instanceTokens[0].Position.Start..instanceTokens[^1].Position.End]}'",
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
        if (token is { Type: TokenType.Range })
            return "..";
        if (token is ValueOperationValuePosToken { Operation: OperationEnum.Divide })
            return "/";
        if (token is ValueOperationValuePosToken { Operation: OperationEnum.Multiply })
            return "*";
        switch (token.Type)
        {
            case TokenType.Group when token is GroupValuePosToken valueGroupPosToken:
                return SimpleEvaluateExpressionHigh(valueGroupPosToken.Tokens);
            case TokenType.KeywordCall when token is CallLikePosToken callToken:
                return MethodCall(callToken.Name, callToken: callToken);
            // case TokenType.CodeBlock when token is CodeBlockToken codeBlockToken:
            //     BlockStack.Push(new(false, true, null, GetFileScope()));
            //     return RunCodeBlock(codeBlockToken);
            case TokenType.Keyword when token is KeywordToken keywordToken:
                return keywordToken.String;
            case TokenType.DotGroup:
                return EvaluateDotThings(MemoryMarshal.CreateSpan(ref token, 1));
            case TokenType.ExternThing when token is ExternThingToken externThingToken:
            {
                var name = externThingToken.String;
                var fileScope = GetFileScope();
                // try RCaron ClassDefinition
                if (TryGetClassDefinition(name, fileScope, out var classDefinition))
                    return classDefinition;

                // get Type via TypeResolver
                var type = TypeResolver.FindType(externThingToken.String, fileScope)!;
                if (type == null)
                    throw RCaronException.TypeNotFound(externThingToken.String);
                return new RCaronType(type);
            }
            case TokenType.EqualityOperationGroup when token is ComparisonValuePosToken comparisonValuePosToken:
                return EvaluateComparisonOperation(comparisonValuePosToken);
            case TokenType.LogicalOperationGroup
                when token is LogicalOperationValuePosToken logicalOperationValuePosToken:
                return EvaluateLogicalOperation(logicalOperationValuePosToken);
            case TokenType.Dot:
                return ".";
            case TokenType.TokenGroup when token is TokenGroupPosToken tokenGroupPosToken:
                return SimpleEvaluateExpressionHigh(tokenGroupPosToken.Tokens);
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
                default:
                    op.CallSite ??= BinderUtil.GetBinaryOperationCallSite(op.Operation);
                    value = op.CallSite.Target(op.CallSite, value, second);
                    break;
            }

            index += 2;
        }

        return value;
    }

    public object? SimpleEvaluateExpressionHigh(ArraySegment<PosToken> tokens)
        => tokens.Count switch
        {
            > 0 when tokens[0] is KeywordToken { IsExecutable: true } keywordToken => MethodCall(keywordToken.String,
                argumentTokens: tokens.Segment(1..)),
            1 => SimpleEvaluateExpressionSingle(tokens[0]),
            > 2 => SimpleEvaluateExpressionValue(tokens),
            _ => throw new Exception("something has gone very wrong with the parsing most probably")
        };

    public object? FunctionCall(Function function, CallLikePosToken? callToken = null,
        ArraySegment<PosToken> argumentTokens = default, ClassInstance? classInstance = null)
    {
        LocalScope? scope = null;
        if (function.Arguments != null)
        {
            scope ??= classInstance == null ? new LocalScope() : new ClassFunctionScope(classInstance);
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
                    throw RCaronException.PositionalArgumentAfterNamedArgument();
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


        BlockStack.Push(new StackThing(false, true, scope, function.FileScope,
            Lines.Count - _curIndex > 0 ? Lines[_curIndex] : null));
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
                return val1?.Equals(val2) ?? val2 == null;
            case OperationEnum.IsNotEqual:
                return !val1?.Equals(val2) ?? val2 != null;
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
        bool Evaluate(ValuePosToken token)
            => (bool)SimpleEvaluateExpressionSingle(token)!;

        var op = comparisonValuePosToken.ComparisonToken;
        switch (op.Operation)
        {
            case OperationEnum.And:
                return Evaluate(comparisonValuePosToken.Left) && Evaluate(comparisonValuePosToken.Right);
            case OperationEnum.Or:
                return Evaluate(comparisonValuePosToken.Left) || Evaluate(comparisonValuePosToken.Right);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FileScope GetFileScope()
        => BlockStack.Count == 0 ? MainFileScope : BlockStack.Peek().FileScope;

    public static bool TryGetClassDefinition(string name, FileScope fileScope,
        [MaybeNullWhen(false)] out ClassDefinition classDefinition)
    {
        if (TryGetClassDefinition(fileScope.ClassDefinitions, name, out classDefinition))
            return true;
        if (TryGetClassDefinition(fileScope.ImportedClassDefinitions, name, out classDefinition))
            return true;
        if (fileScope.ImportedFileScopes != null)
            for (var i = 0; i < fileScope.ImportedFileScopes.Count; i++)
                if (TryGetClassDefinition(fileScope.ImportedFileScopes[i].ClassDefinitions, name, out classDefinition))
                    return true;
        classDefinition = null;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetClassDefinition(List<ClassDefinition>? classDefinitions, string name,
        [MaybeNullWhen(false)] out ClassDefinition classDefinition)
    {
        if (classDefinitions != null)
            for (var i = 0; i < classDefinitions.Count; i++)
                if (name.Equals(classDefinitions[i].Name, StringComparison.InvariantCultureIgnoreCase))
                {
                    classDefinition = classDefinitions[i];
                    return true;
                }

        classDefinition = null;
        return false;
    }

    public bool TryGetFunction(string name, FileScope fileScope, [MaybeNullWhen(false)] out Function function)
    {
        if (fileScope.Functions?.TryGetValue(name, out function) ?? false)
            return true;
        if (fileScope.ImportedFunctions?.TryGetValue(name, out function) ?? false)
            return true;
        if (fileScope.ImportedFileScopes != null)
            for (var i = 0; i < fileScope.ImportedFileScopes.Count; i++)
                if (fileScope.ImportedFileScopes[i].Functions?.TryGetValue(name, out function) ?? false)
                    return true;

        function = null;
        return false;
    }
}

// todo: move somewhere else or something, make a Util class?
public class MethodResolver
{
    public static (MethodBase, bool needsNumericConversion, bool IsExtension) Resolve(ReadOnlySpan<char> name,
        Type type, FileScope fileScope, object? instance, object?[] args)
    {
        var methodsOrg = type.GetMethods();
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

        // todo(death): would miss out on extension methods that share their name with instance methods
        // todo(perf): could search for valid extension methods after doing first pass with instance methods
        var isExtensionMethods = false;
        if (methods.Count == 0)
        {
            isExtensionMethods = true;
            if (fileScope.UsedNamespacesForExtensionMethods is not null or
                { Count: 0 } /* && instance is not RCaronType or null*/)
            {
                var foundMethods = new List<MethodBase>();
                // extension methods
                args = args.Prepend(instance!).ToArray();
                // Type? endingMatch =  null;
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var ass in assemblies)
                {
                    static void HandleTypes(Type?[] types, ReadOnlySpan<char> name, List<MethodBase> foundMethods,
                        FileScope fileScope)
                    {
                        foreach (var exportedType in types)
                        {
                            if (exportedType is null)
                                continue;
                            if (!(exportedType.IsSealed && exportedType.IsAbstract) || !exportedType.IsPublic)
                                continue;
                            if (!(fileScope.UsedNamespacesForExtensionMethods?.Contains(exportedType.Namespace!) ??
                                  false))
                                continue;
                            // if (type.FullName?.EndsWith(name, StringComparison.InvariantCultureIgnoreCase) ?? false)
                            // {
                            //     endingMatch = type;
                            // }
                            // exact match
                            foreach (var method in exportedType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                            {
                                if (MemoryExtensions.Equals(method.Name, name,
                                        StringComparison.InvariantCultureIgnoreCase))
                                {
                                    foundMethods.Add(method);
                                }
                            }
                        }
                    }

                    try
                    {
                        var types = ass.GetTypes();
                        HandleTypes(types, name, foundMethods, fileScope);
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        HandleTypes(e.Types, name, foundMethods, fileScope);
                    }
                }

                methods = foundMethods.ToArray();
            }
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

            // check if we have more args than the method has parameters
            if (parameters.Length < args.Length)
            {
                // todo(feat): support params
                if (parameters.Length == 0)
                    score = 0;
            }
            else
                for (var j = 0; j < parameters.Length; j++)
                {
                    // if method has more parameters than we have args, check if the parameter is optional
                    if (j >= args.Length)
                    {
                        if (!parameters[j].HasDefaultValue)
                            score = 0;
                        break;
                    }

                    if (parameters[j].ParameterType == args[j]?.GetType())
                    {
                        score += 100;
                    }
                    else if (parameters[j].ParameterType.IsInstanceOfType(args[j]))
                    {
                        score += 10;
                    }
                    // todo: support actual generic parameters constraints
                    else if (parameters[j].ParameterType.IsGenericType
                             && ListEx.IsAssignableToGenericType(args[j]?.GetType() ?? typeof(object),
                                 parameters[j].ParameterType.GetGenericTypeDefinition()))
                        // parameters[j].ParameterType.GetGenericParameterConstraints()
                    {
                        score += 10;
                    }
                    else if (parameters[j].ParameterType.IsNumericType() &&
                             (args[j]?.GetType().IsNumericType() ?? false))
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
        return (bestMethod, needsNumericConversions[bestIndex], isExtensionMethods);
    }
}