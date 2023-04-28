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
    internal Func<Motor, string, ArraySegment<PosToken>, FileScope, Pipeline?, bool, object?>? InvokeRunExecutable
    {
        get;
        set;
    } = null;

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
                    if (forLoopLine.Initializer != null)
                        RunLine(forLoopLine.Initializer);
                    while (EvaluateBool(forLoopLine.CallToken.Arguments[1]))
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

                        if (forLoopLine.Iterator != null)
                            RunLine(forLoopLine.Iterator);
                    }

                    break;
                }
                case LineType.QuickForLoop when baseLine is ForLoopLine forLoopLine:
                {
                    if (forLoopLine.Initializer != null)
                        RunLine(forLoopLine.Initializer);
                    var scope = new StackThing(true, false, null, GetFileScope());
                    while (EvaluateBool(forLoopLine.CallToken.Arguments[1]))
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

                        if (forLoopLine.Iterator != null)
                            RunLine(forLoopLine.Iterator);
                    }

                    break;
                }
                case LineType.PipelineRun when baseLine is SingleTokenLine
                {
                    Token: PipelineValuePosToken pipeline
                }:
                {
                    var val = EvaluateExpressionSingle(pipeline);
                    return (false, val);
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
                var obj = EvaluateExpressionHigh(line.Tokens.Segment(2..));
                SetVar(variableName, obj);
                Debug.WriteLine($"variable '{variableName}' set to '{obj}'");
                break;
            }
            case LineType.LetVariableAssignment:
            {
                var variableName = ((VariableToken)line.Tokens[1]).Name;
                var obj = EvaluateExpressionHigh(line.Tokens.Segment(3..));
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

                assigner.Assign(EvaluateExpressionHigh(line.Tokens.Segment(2..)));
                break;
            }
            case LineType.IfStatement when line.Tokens[0] is CallLikePosToken callToken:
            {
                ElseState = false;
                if (EvaluateBool(callToken.Arguments[0]))
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
                if (!ElseState && EvaluateBool(callToken.Arguments[0]))
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
                while (EvaluateBool(callToken.Arguments[0]))
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
                } while (EvaluateBool(callToken.Arguments[0]));

                break;
            }
            case LineType.ForeachLoop when line.Tokens[0] is CallLikePosToken callToken:
            {
                var body = (CodeBlockToken)line.Tokens[1];
                var varName = ((VariableToken)callToken.Arguments[0][0]).Name;
                var enumerator = EvaluateExpressionHigh(callToken.Arguments[0][2..]) switch
                {
                    IEnumerable enumerable => enumerable.GetEnumerator(),
                    IEnumerator enumeratorr => enumeratorr,
                };
                while (enumerator.MoveNext())
                {
                    var scope = new LocalScope();
                    scope.SetVariable(varName, enumerator.Current);
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

                if (enumerator is IDisposable disposable)
                    disposable.Dispose();

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
            case LineType.UnaryOperation when line is UnaryOperationLine unaryOperationLine:
            {
                var variableName = ((VariableToken)line.Tokens[0]).Name;
                unaryOperationLine.CallSite ??= BinderUtil.GetBinaryOperationCallSite(
                    line.Tokens[1] is OperationPosToken { Operation: OperationEnum.UnaryIncrement }
                        ? OperationEnum.Sum
                        : OperationEnum.Subtract);
                ref var variable = ref GetVarRef(variableName);
                variable = unaryOperationLine.CallSite.Target(unaryOperationLine.CallSite, variable!, (long)1);
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
                var switchValue = EvaluateExpressionHigh(((CallLikePosToken)line.Tokens[0]).Arguments[0]);
                var cases = (CodeBlockToken)line.Tokens[1];
                for (var i = 1; i < cases.Lines.Count - 1; i++)
                {
                    var caseLine = ((TokenLine)cases.Lines[i]);
                    var value = EvaluateExpressionSingle(caseLine.Tokens[0]);
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
                            : EvaluateExpressionHigh(ArgsArray());
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
                            Console.Debug(EvaluateExpressionHigh(ArgsArray()));
                            return (false, RCaronInsideEnum.NoReturnValue);
                        case "dbg_assert_is_one":
                            GlobalScope.SetVariable("$$assertResult",
                                EvaluateExpressionSingle(args[0]).Expect<long>() == 1);
                            return (false, RCaronInsideEnum.NoReturnValue);
                        case "dbg_sum_three":
                            GlobalScope.SetVariable("$$assertResult",
                                EvaluateExpressionSingle(args[0]).Expect<long>() +
                                EvaluateExpressionSingle(args[1]).Expect<long>() +
                                EvaluateExpressionSingle(args[2]).Expect<long>());
                            return (false, RCaronInsideEnum.NoReturnValue);
                        case "dbg_exit":
                            return (true, RCaronInsideEnum.NoReturnValue);
                        case "dbg_throw":
                            throw new("dbg_throw");
                    }

                if (TryCallFunction(keywordString, GetFileScope(), null, line.Tokens.Segment(1..), null,
                        out var result))
                    return (false, result);

                MethodCall(keywordString, line.Tokens.Segment(1..));
                break;
            }
            case LineType.PathCall when line.Tokens[0] is ConstToken pathToken:
            {
                if (InvokeRunExecutable == null)
                    throw new("InvokeRunExecutable is null");
                InvokeRunExecutable(this, (string)pathToken.Value, line.Tokens.Segment(1..), GetFileScope(), null,
                    false);
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
        , object? instance = null, Pipeline? pipeline = null, bool isLeftOfPipeline = false)
    {
        FileScope? fileScope = null;
        Span<char> name = stackalloc char[nameArg.Length];
        MemoryExtensions.ToLowerInvariant(nameArg, name);

        object? At(in Span<PosToken> tokens, int index)
        {
            if (callToken != null)
                return EvaluateExpressionHigh(callToken.Arguments[index]);
            return EvaluateExpressionSingle(tokens[index]);
        }

        object?[] All(in ArraySegment<PosToken> tokens)
        {
            if (tokens.Count == 0 && (callToken?.ArgumentsEmpty() ?? false))
                return Array.Empty<object>();
            if (callToken != null)
            {
                var res = new object?[callToken.Arguments.Length];
                for (var ind = 0; ind < callToken.Arguments.Length; ind++)
                    res[ind] = EvaluateExpressionHigh(callToken.Arguments[ind]);
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

        if (TryCallFunction(nameArg, (fileScope ??= GetFileScope()), callToken, argumentTokens, null, out var result,
                instance is ClassDefinition classDefinition ? classDefinition : null, pipeline))
            return result;

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
                var v = module.RCaronModuleRun(name, this, argumentTokens, callToken, pipeline, isLeftOfPipeline);
                if (!v?.Equals(RCaronInsideEnum.MethodNotFound) ?? true)
                    return v;
            }
        }

        if (InvokeRunExecutable != null && instance == null && callToken == null)
        {
            var ret = InvokeRunExecutable(this, nameArg, argumentTokens, fileScope ??= GetFileScope(), pipeline,
                isLeftOfPipeline);
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

                if (val is ClassDefinition classDefinition)
                {
                    var i = classDefinition.GetStaticPropertyIndex(keywordToken.String);
                    if (i != -1)
                        return new ClassStaticAssigner(classDefinition, i);
                    throw RCaronException.ClassStaticPropertyNotFound(keywordToken.String);
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

    public object? EvaluateDotThings(Span<PosToken> instanceTokens, Pipeline? pipeline = null)
    {
        if (instanceTokens.Length == 1 && instanceTokens[0] is DotGroupPosToken dotGroupPosToken)
            instanceTokens = dotGroupPosToken.Tokens;
        var val = EvaluateExpressionSingle(instanceTokens[0]);
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
                var evaluated = EvaluateExpressionHigh(arrayAccessorToken.Tokens);

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
                                    EvaluateExpressionHigh(classDefinition.PropertyInitializers[j]!);
                        }

                    type = null;
                    continue;
                }
                else if (val is ClassInstance classInstance)
                {
                    if (callLikePosToken.Name.Equals("gettype", StringComparison.InvariantCultureIgnoreCase))
                    {
                        val = typeof(ClassInstance);
                        type = typeof(Type);
                        continue;
                    }

                    var func = classInstance.Definition.Functions?[callLikePosToken.Name];
                    if (func == null)
                        throw RCaronException.ClassFunctionNotFound(callLikePosToken.Name);
                    val = FunctionCall(func, callLikePosToken, classInstance: classInstance, pipeline: pipeline);
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

            if (val is ClassDefinition classDefinition1)
            {
                var name = ((KeywordToken)instanceTokens[i]).String;
                if (classDefinition1.TryGetStaticPropertyValue(name, out val))
                    type = val?.GetType();
                else
                    throw RCaronException.ClassStaticPropertyNotFound(name);
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
            objs[ind - tokensStartIndex] = EvaluateExpressionSingle(tokens[ind]);
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

    public object? EvaluateExpressionSingle(PosToken token, bool isLeftOfPipeline = false, Pipeline? pipeline = null)
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
        if (token is PipelineValuePosToken pipelineV)
        {
            if (pipelineV.Left[0] is KeywordToken { IsExecutable: false } keywordToken)
                keywordToken.IsExecutable = true;
            var left = RunLeftPipeline(pipelineV.Left);
            if (pipelineV.Right[0] is KeywordToken { IsExecutable: false } keywordToken2)
                keywordToken2.IsExecutable = true;
            var right = EvaluateExpressionHigh(pipelineV.Right, pipeline: left, isLeftOfPipeline: isLeftOfPipeline);
            return right;
        }

        switch (token.Type)
        {
            case TokenType.Group when token is GroupValuePosToken valueGroupPosToken:
                return EvaluateExpressionHigh(valueGroupPosToken.Tokens);
            case TokenType.KeywordCall when token is CallLikePosToken callToken:
                return MethodCall(callToken.Name, callToken: callToken);
            // case TokenType.CodeBlock when token is CodeBlockToken codeBlockToken:
            //     BlockStack.Push(new(false, true, null, GetFileScope()));
            //     return RunCodeBlock(codeBlockToken);
            case TokenType.Keyword when token is KeywordToken keywordToken:
                return keywordToken.String;
            case TokenType.DotGroup:
                return EvaluateDotThings(MemoryMarshal.CreateSpan(ref token, 1), pipeline);
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
                return EvaluateExpressionHigh(tokenGroupPosToken.Tokens);
        }

        throw new Exception($"invalid tokentype to evaluate: {token.Type}");
    }

    [CollectionAccess(CollectionAccessType.Read)]
    public object EvaluateExpressionValue(ArraySegment<PosToken> tokens)
    {
        // repeat action something math
        var index = 0;
        object value = EvaluateExpressionSingle(tokens[0])!;
        if (tokens[1] is IndexerToken)
        {
        }

        while (index < tokens.Count - 1)
        {
            var op = (ValueOperationValuePosToken)tokens[index + 1];
            var second = EvaluateExpressionSingle(tokens[index + 2])!;
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

    public object? EvaluateExpressionHigh(ArraySegment<PosToken> tokens, Pipeline? pipeline = null,
        bool isLeftOfPipeline = false)
        => tokens.Count switch
        {
            > 0 when tokens[0] is KeywordToken { IsExecutable: true } keywordToken => MethodCall(keywordToken.String,
                argumentTokens: tokens.Segment(1..), pipeline: pipeline, isLeftOfPipeline: isLeftOfPipeline),
            1 => EvaluateExpressionSingle(tokens[0], isLeftOfPipeline, pipeline),
            > 2 => EvaluateExpressionValue(tokens),
            _ => throw new Exception("something has gone very wrong with the parsing most probably")
        };

    public object? FunctionCall(Function function, CallLikePosToken? callToken = null,
        ArraySegment<PosToken> argumentTokens = default, ClassInstance? classInstance = null,
        ClassDefinition? staticClassDefinition = null, Pipeline? pipeline = null)
    {
        var scope = classInstance == null
            ? (staticClassDefinition == null ? new LocalScope() : new ClassStaticFunctionScope(staticClassDefinition))
            : new ClassFunctionScope(classInstance);
        if (function.Arguments != null)
        {
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

                    var variables = scope.GetVariables();
                    variables[function.Arguments[index].Name] =
                        EvaluateExpressionHigh(enumerator.CurrentTokens);
                    assignedArguments[index] = true;
                }
                else if (!enumerator.HitNamedArgument)
                {
                    var variables = scope.GetVariables();
                    for (var i = 0; i < function.Arguments.Length; i++)
                    {
                        if (!variables.ContainsKey(function.Arguments[i].Name))
                        {
                            variables[function.Arguments[i].Name] =
                                EvaluateExpressionHigh(enumerator.CurrentTokens);
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
                    var variables = scope.GetVariables();
                    variables[function.Arguments[i].Name] = function.Arguments[i].DefaultValue;
                }
            }

            // check if all arguments are assigned
            for (var i = 0; i < function.Arguments.Length; i++)
            {
                if (function.Arguments[i].DefaultValue == RCaronParser.FromPipelineObject)
                {
                    var variables = scope.GetVariables();
                    variables[function.Arguments[i].Name] = pipeline;
                }

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

    public bool EvaluateBool(PosToken[] tokens)
        => tokens switch
        {
            { Length: 1 } when tokens[0] is ComparisonValuePosToken comparisonToken => EvaluateComparisonOperation(
                comparisonToken),
            { Length: 1 } when tokens[0] is ValuePosToken => (bool)EvaluateExpressionSingle(tokens[0])!,
            _ => throw new Exception($"Invalid tokens to evaluate for {nameof(EvaluateBool)}"),
        };

    public bool EvaluateComparisonOperation(ComparisonValuePosToken comparisonValuePosToken)
    {
        comparisonValuePosToken.CallSite ??=
            BinderUtil.GetComparisonOperationCallSite(comparisonValuePosToken.ComparisonToken.Operation);
        return (bool)comparisonValuePosToken.CallSite.Target(comparisonValuePosToken.CallSite,
            EvaluateExpressionSingle(comparisonValuePosToken.Left),
            EvaluateExpressionSingle(comparisonValuePosToken.Right));
    }

    public bool EvaluateLogicalOperation(LogicalOperationValuePosToken comparisonValuePosToken)
    {
        bool Evaluate(ValuePosToken token)
            => (bool)EvaluateExpressionSingle(token)!;

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

    public Pipeline RunLeftPipeline(PosToken[] tokens, Pipeline? pipelineIn = null)
    {
        var val = EvaluateExpressionHigh(tokens, pipelineIn, true);
        return val switch
        {
            Pipeline pipeline => pipeline,
            IEnumerator enumerator => new EnumeratorPipeline(enumerator),
            IEnumerable enumerable => new EnumeratorPipeline(enumerable.GetEnumerator()),
            _ => new SingleObjectPipeline(val),
        };
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

    public bool TryCallFunction(string name, FileScope fileScope, CallLikePosToken? callToken,
        ArraySegment<PosToken> argumentTokens, ClassInstance? classInstance, out object? result,
        ClassDefinition? classDefinition = null, Pipeline? pipeline = null)
    {
        if (TryGetFunction(name, fileScope, out var func))
        {
            result = FunctionCall(func, callToken, argumentTokens, classInstance, pipeline: pipeline);
            return true;
        }

        // _inside class_ or not
        if (classInstance == null && classDefinition == null)
        {
            ClassFunctionScope? scope = null;
            for (var i = BlockStack.Count - 1; i >= 0; i--)
            {
                var el = BlockStack.At(i);
                if (el.Scope is ClassFunctionScope classFunctionScope)
                {
                    scope = classFunctionScope;
                    break;
                }

                if (el.IsReturnWorthy)
                    break;
            }

            if (scope == null)
            {
                result = null;
                return false;
            }

            var definition = scope.ClassInstance.Definition;
            if (definition.Functions?.TryGetValue(name, out var func2) ?? false)
            {
                result = FunctionCall(func2, callToken, argumentTokens, classInstance, pipeline: pipeline);
                return true;
            }
        }

        // static class function
        if (classDefinition != null)
        {
            if (classDefinition.StaticFunctions?.TryGetValue(name, out var func3) ?? false)
            {
                result = FunctionCall(func3, callToken, argumentTokens, staticClassDefinition: classDefinition,
                    pipeline: pipeline);
                return true;
            }
        }

        result = null;
        return false;
    }
}