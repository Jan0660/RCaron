using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Dynamitey;
using JetBrains.Annotations;
using Log73;
using RCaron.BaseLibrary;
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

    public Stack<StackThing> BlockStack { get; set; } = new();
    public FileScope FileScope { get; set; } = new();
    public MotorOptions Options { get; }
    public List<IRCaronModule> Modules { get; set; }

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

    public Dictionary<string, Function> Functions { get; set; } = new();
    public LocalScope GlobalScope { get; set; } = new();
    public object? ReturnValue = null;

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
    }

    /// <summary>
    /// current line index
    /// </summary>
    private int curIndex;

    public void Run(int startIndex = 0)
    {
        curIndex = startIndex;
        for (; curIndex < Lines.Count; curIndex++)
        {
            if (curIndex >= Lines.Count)
                break;
            var line = Lines[curIndex];
            var res = RunLine(line);
            if (res == RunLineResult.Exit)
                return;
        }
    }

    public enum RunLineResult : byte
    {
        Nothing = 0,
        Exit = 1,
    }

    public RunLineResult RunLine(Line baseLine)
    {
        Debug.WriteLine(baseLine is TokenLine tokenLine
            ? Raw[tokenLine.Tokens[0].Position.Start..tokenLine.Tokens[^1].Position.End]
            : (
                baseLine is CodeBlockLine ? "CodeBlockLine" : "invalid line type?"));
        if (baseLine is CodeBlockLine codeBlockLine)
        {
            BlockStack.Push(new(false, false, null));
            RunCodeBlock(codeBlockLine.Token);
            return RunLineResult.Nothing;
        }

        if (baseLine is not TokenLine line)
            return RunLineResult.Exit;
        switch (line.Type)
        {
            case LineType.VariableAssignment:
            {
                var variableName = Raw[(line.Tokens[0].Position.Start + 1)..line.Tokens[0].Position.End];
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
                if (SimpleEvaluateBool(callToken.Arguments[0]))
                {
                    BlockStack.Push(new(false, false, null));
                    RunCodeBlock(((CodeBlockLine)Lines[curIndex + 1]).Token);
                }

                curIndex += 1;
                break;
            }
            case LineType.WhileLoop when line.Tokens[0] is CallLikePosToken callToken:
            {
                while (SimpleEvaluateBool(callToken.Arguments[0]))
                {
                    BlockStack.Push(new StackThing(true, false, null));
                    RunCodeBlock(((CodeBlockLine)Lines[curIndex + 1]).Token);
                }

                curIndex++;
                break;
            }
            case LineType.DoWhileLoop when line.Tokens[0] is CallLikePosToken callToken:
            {
                do
                {
                    BlockStack.Push(new StackThing(true, false, null));
                    RunCodeBlock(((CodeBlockLine)Lines[curIndex + 1]).Token);
                } while (SimpleEvaluateBool(callToken.Arguments[0]));

                curIndex++;
                break;
            }
            case LineType.ForeachLoop when line.Tokens[0] is CallLikePosToken callToken:
            {
                var varName =
                    Raw[(callToken.Arguments[0][0].Position.Start + 1)..callToken.Arguments[0][0].Position.End];
                foreach (var item in (IEnumerable)SimpleEvaluateExpressionHigh(callToken.Arguments[0][2..])!)
                {
                    var scope = new LocalScope();
                    scope.SetVariable(varName, item);
                    BlockStack.Push(new StackThing(true, false, scope));
                    RunCodeBlock(((CodeBlockLine)Lines[curIndex + 1]).Token);
                }

                curIndex++;
                break;
            }
            case LineType.UnaryOperation:
            {
                var variableName = Raw[(line.Tokens[0].Position.Start + 1)..line.Tokens[0].Position.End];
                if (line.Tokens[1].EqualsString(Raw, "++"))
                {
                    Horrors.AddTo(ref GetVarRef(variableName)!, (long)1);
                }
                else if (line.Tokens[1].EqualsString(Raw, "--"))
                    SetVar(variableName,
                        Horrors.Subtract(SimpleEvaluateExpressionSingle(line.Tokens[0]).NotNull(), (long)1));

                break;
            }
            case LineType.BlockStuff:
                if (line.Tokens[0] is { Type: TokenType.BlockEnd })
                {
                    if (!ReturnValue?.Equals(RCaronInsideEnum.Breaked) ?? false)
                        BlockStack.Pop();
                    return RunLineResult.Exit;
                }

                break;
            case LineType.LoopLoop:
                // todo(safety): check elsewhere when ReturnValue is used that it is then set to null or something idk
                ReturnValue = null;
                while (!ReturnValue?.Equals(RCaronInsideEnum.Breaked) ?? true)
                {
                    BlockStack.Push(new StackThing(true, false, null));
                    RunCodeBlock(((CodeBlockLine)Lines[curIndex + 1]).Token);
                }

                curIndex++;
                break;
            case LineType.ForLoop when line.Tokens[0] is CallLikePosToken callToken:
            {
                var falseI = 0;
                RunLine(RCaronRunner.GetLine(callToken.Arguments[0], ref falseI, Raw));
                while (SimpleEvaluateBool(callToken.Arguments[1]))
                {
                    BlockStack.Push(new StackThing(true, false, null));
                    RunCodeBlock(((CodeBlockLine)Lines[curIndex + 1]).Token);
                    falseI = 0;
                    RunLine(RCaronRunner.GetLine(callToken.Arguments[2], ref falseI, Raw));
                }

                curIndex++;
                break;
            }
            case LineType.QuickForLoop when line.Tokens[0] is CallLikePosToken callToken:
            {
                var falseI = 0;
                RunLine(RCaronRunner.GetLine(callToken.Arguments[0], ref falseI, Raw));
                var scope = new StackThing(true, false, null);
                while (SimpleEvaluateBool(callToken.Arguments[1]))
                {
                    BlockStack.Push(scope);
                    RunCodeBlock(((CodeBlockLine)Lines[curIndex + 1]).Token);
                    falseI = 0;
                    RunLine(RCaronRunner.GetLine(callToken.Arguments[2], ref falseI, Raw));
                }

                curIndex++;
                break;
            }
            case LineType.Function:
            {
                string name;
                FunctionArgument[]? arguments = null;
                if (line.Tokens[1] is CallLikePosToken callToken)
                {
                    name = callToken.GetName(Raw).ToLowerInvariant();
                    arguments = new FunctionArgument[callToken.Arguments.Length];
                    for (int i = 0; i < callToken.Arguments.Length; i++)
                    {
                        var cur = callToken.Arguments[i];
                        var argName = Raw[(cur[0].Position.Start + 1)..cur[0].Position.End];
                        arguments[i] = new FunctionArgument(argName);
                        if (cur.Length > 1 && cur[1].EqualsString(Raw, "="))
                        {
                            arguments[i].DefaultValue = SimpleEvaluateExpressionHigh(cur[2..]);
                        }
                    }
                }
                else
                    name = line.Tokens[1].ToString(Raw);

                Functions[name] = new Function(((CodeBlockLine)Lines[curIndex + 1]).Token, arguments);
                curIndex++;
                break;
            }
            case LineType.KeywordCall when line.Tokens[0] is CallLikePosToken callToken:
            {
                MethodCall(callToken.GetName(Raw), callToken: callToken, instance: null
                    // instanceTokens: MemoryMarshal.CreateSpan(ref callToken.OriginalToken, 1)
                    );
                break;
            }
            case LineType.DotGroupCall:
            {
                EvaluateDotThings(((DotGroupPosToken)line.Tokens[0]).Tokens);
                break;
            }
            case LineType.KeywordPlainCall:
            {
                var keyword = line.Tokens[0];
                var keywordString = keyword.ToString(Raw);
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
                        ReturnValue = RCaronInsideEnum.Breaked;
                        return RunLineResult.Exit;
                    }
                    case "return":
                    {
                        ReturnValue = SimpleEvaluateExpressionHigh(ArgsArray());
                        var g = BlockStack.Pop();
                        while (!g.IsReturnWorthy)
                            g = BlockStack.Pop();
                        return RunLineResult.Exit;
                    }
                }

                if (Options.EnableDebugging)
                    switch (keywordString)
                    {
                        case "dbg_println":
                            Console.Debug(SimpleEvaluateExpressionHigh(ArgsArray()));
                            return RunLineResult.Nothing;
                        case "dbg_assert_is_one":
                            GlobalScope.SetVariable("$$assertResult",
                                SimpleEvaluateExpressionSingle(args[0]).Expect<long>() == 1);
                            return RunLineResult.Nothing;
                        case "dbg_sum_three":
                            GlobalScope.SetVariable("$$assertResult", Horrors.Sum(
                                Horrors.Sum(SimpleEvaluateExpressionSingle(args[0]).NotNull(),
                                    SimpleEvaluateExpressionSingle(args[1]).NotNull()),
                                SimpleEvaluateExpressionSingle(args[2]).NotNull()));
                            return RunLineResult.Nothing;
                    }

                if (Options.EnableDumb)
                    switch (keywordString)
                    {
                        case "goto_line":
                            curIndex = (int)SimpleEvaluateExpressionSingle(args[0]).Expect<long>();
                            return RunLineResult.Nothing;
                    }

                if (Functions.TryGetValue(keywordString, out var func))
                {
                    FunctionPlainCall(func, line.Tokens.Segment(1..));
                    return RunLineResult.Nothing;
                }

                MethodCall(keywordString, line.Tokens.AsSpan()[1..]);
                break;
            }
        }

        return RunLineResult.Nothing;
    }

    public object? RunCodeBlock(CodeBlockToken codeBlock)
    {
        var prevIndex = curIndex;
        var prevLines = Lines;
        curIndex = 0;
        Lines = codeBlock.Lines;
        ReturnValue = RCaronInsideEnum.NoReturnValue;
        Run();
        curIndex = prevIndex;
        Lines = prevLines;
        return ReturnValue;
    }

    public object? MethodCall(string name, Span<PosToken> argumentTokens = default, CallLikePosToken? callToken = null
        // , Span<PosToken> instanceTokens = default
        , object? instance = null
        )
    {
        // lowercase the string if not all characters are lowercase
        for (var i = 0; i < name.Length; i++)
        {
            if (!char.IsLower(name[i]))
            {
                name = name.ToLowerInvariant();
                break;
            }
        }

        object? At(in Span<PosToken> tokens, int index)
        {
            if (callToken != null)
                return SimpleEvaluateExpressionHigh(callToken.Arguments[index]);
            return SimpleEvaluateExpressionSingle(tokens[index]);
        }

        object?[] All(in Span<PosToken> tokens)
        {
            if (tokens.Length == 0 && (callToken?.ArgumentsEmpty() ?? false))
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

        switch (name.ToLowerInvariant())
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
                var val = GlobalScope.GetVariable(At(argumentTokens, 0).Expect<string>());
                if (val.Equals(RCaronInsideEnum.VariableNotFound))
                    throw RCaronException.VariableNotFound(name);
                return val;
            }
            case "globalset":
            {
                GlobalScope.SetVariable(At(argumentTokens, 0).Expect<string>(), At(argumentTokens, 1));
                return RCaronInsideEnum.NoReturnValue;
            }
            case "printfunny":
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
        }

        if (Functions.TryGetValue(name, out var func))
        {
            return FunctionPlainCall(func, argumentTokens);
        }

        if (name[0] == '#' || instance != null)
        {
            var args = All(in argumentTokens);
            Type? type = null;
            string methodName;
            object? target = null;
            object? variable = null;
            if (instance != null)
            {
                // Span<PosToken> given = instanceTokens;
                // if (instanceTokens[0] is DotGroupPosToken dotGroupPosToken)
                // {
                //     given = dotGroupPosToken.Tokens.AsSpan()[..^2];
                //     instanceTokens = dotGroupPosToken.Tokens;
                // }

                // var obj = EvaluateDotThings(given);
                var obj = instance;
                variable = obj;
                if (obj is RCaronType rCaronType)
                    type = rCaronType.Type;
                else
                {
                    target = obj;
                    type = obj.GetType();
                }

                // var i = name.Length - 1;
                // while (name[i] != '.')
                //     i--;
                // i++;
                // todo(perf): just steal it from name var? -- fix name var first lol maybe
                // methodName = instanceTokens[^1].ToString(Raw);
                goto resolveMethod;
            }

            // var d = name[1..(name.LastIndexOf('.'))];
            // type = TypeResolver.FindType(d, FileScope);
            //
            // if (type == null)
            //     throw new RCaronException($"cannot find type '{d}' for external method call",
            //         RCaronExceptionCode.ExternalTypeNotFound);

            resolveMethod: ;
            var methods = (MethodBase[])type.GetMethods()
                .Where(m => m.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase)).ToArray();
            // constructors
            if (name.Equals("new", StringComparison.InvariantCultureIgnoreCase))
            {
                var foundMethods = methods.ToList();
                foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                {
                    foundMethods.Add(constructor);
                }

                methods = foundMethods.ToArray();
            }

            if (methods.Length == 0)
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
                            if (method.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) &&
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

            Span<uint> scores = stackalloc uint[methods.Length];
            for (var i = 0; i < methods.Length; i++)
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
                    else if (parameters[j].ParameterType.IsGenericType &&
                             ListEx.IsAssignableToGenericType(args[j].GetType(),
                                 parameters[j].ParameterType.GetGenericTypeDefinition()))
                    {
                        score += 10;
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

            // mismatch count arguments -> equate it out with default values
            // is equate even a word?
            var paramss = methods[bestIndex].GetParameters();
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

            // generic method handling aaa
            if (methods[bestIndex].IsGenericMethod)
            {
                // todo(perf): probably not the fastest
                var t = methods[bestIndex].DeclaringType!;
                // static class
                if (t.IsSealed && t.IsAbstract)
                {
                    var staticContext = InvokeContext.CreateStatic;
                    return Dynamic.InvokeMember(staticContext(t), methods[bestIndex].Name, args);
                }
                else
                {
                    return Dynamic.InvokeMember(target, methods[bestIndex].Name, args);
                }
            }

            if (methods[bestIndex] is ConstructorInfo constructorInfo)
            {
                return constructorInfo.Invoke(args);
            }

            return methods[bestIndex].Invoke(target, args);
        }

        foreach (var module in Modules)
        {
            var v = module.RCaronModuleRun(name, this, argumentTokens);
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
        // else
        // {
        //     val = null;
        //     var name = tokens[0].ToSpan(Raw);
        //     var d = name[1..(name.LastIndexOf('.'))].ToString();
        //     type = TypeResolver.FindType(d, FileScope)!;
        //     if (type == null)
        //         throw RCaronException.TypeNotFound(d);
        // }

        if (val is RCaronType rCaronType)
            type = rCaronType.Type;

        var last = tokens[^1];
        if (last.Type == TokenType.Keyword || (tokens.Length == 1 && tokens[0].Type == TokenType.ExternThing))
        {
            string str;
            if (last.Type == TokenType.Keyword)
            {
                str = last.ToString(Raw);
            }
            else if (tokens[0].Type == TokenType.ExternThing)
            {
                var name = tokens[0].ToSpan(Raw);
                str = name[(name.LastIndexOf('.') + 1)..].ToString();
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

        if (val is IList iList && last is ArrayAccessorToken arrayAccessorToken)
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
        Type type;
        if (val is RCaronType rCaronType)
        {
            type = rCaronType.Type;
        }
        else
        {
            type = val.GetType();
        }

        for (int i = 1; i < instanceTokens.Length; i++)
        {
            if (instanceTokens[i].Type == TokenType.Dot || instanceTokens[i].Type == TokenType.Colon)
                continue;
            if (i != instanceTokens.Length - 1 && val == null)
                throw RCaronException.NullInTokens(instanceTokens, Raw, i);
            if (instanceTokens[i] is ArrayAccessorToken arrayAccessorToken)
            {
                if (val is IDictionary dict)
                {
                    var args = type.GetGenericArguments();
                    var keyType = args[0];
                    val = dict[Convert.ChangeType(SimpleEvaluateExpressionHigh(arrayAccessorToken.Tokens), keyType)!];
                    type = val?.GetType();
                    continue;
                }

                var asInt = Convert.ChangeType(SimpleEvaluateExpressionHigh(arrayAccessorToken.Tokens), typeof(int));
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

                continue;
            }

            if (instanceTokens[i] is CallLikePosToken callLikePosToken)
            {
                var d = MethodCall(callLikePosToken.GetName(Raw), callToken: callLikePosToken, instance: val);
                val = d;
                type = val?.GetType();
                continue;
            }

            var str = instanceTokens[i].ToString(Raw);
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

            if (int.TryParse(str, out var partIntValue))
            {
                if (val is Array array)
                {
                    val = array.GetValue(partIntValue);
                    type = val?.GetType();
                    continue;
                }

                if (val is IList list)
                {
                    val = list[partIntValue];
                    type = val?.GetType();
                    continue;
                }
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
        }

        var val = GetVar(name);
        if (val?.Equals(RCaronInsideEnum.VariableNotFound) ?? false)
            throw RCaronException.VariableNotFound(name);
        return val;
    }

    public object? SimpleEvaluateExpressionSingle(PosToken token)
    {
        switch (token.Type)
        {
            case TokenType.VariableIdentifier:
            {
                var name = token.ToString(Raw)[1..];
                return EvaluateVariable(name);
            }
            case TokenType.Number:
                return Int64.Parse(token.ToSpan(Raw));
            case TokenType.DecimalNumber:
                return Decimal.Parse(token.ToSpan(Raw));
            case TokenType.String:
            {
                var s = token.ToSpan(Raw)[1..^1];
                Span<char> g = stackalloc char[s.Length];
                var str = new SpanStringBuilder(ref g);
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
            }
            case TokenType.DumbShit when token is ValueGroupPosToken valueGroupPosToken:
                return SimpleEvaluateExpressionValue(valueGroupPosToken.ValueTokens);
            case TokenType.KeywordCall when token is CallLikePosToken callToken:
                return MethodCall(callToken.GetName(Raw), callToken: callToken);
            case TokenType.CodeBlock when token is CodeBlockToken codeBlockToken:
                BlockStack.Push(new(false, true, null));
                return RunCodeBlock(codeBlockToken);
            case TokenType.Keyword:
                return token.ToString(Raw);
            case TokenType.DotGroup:
                return EvaluateDotThings(MemoryMarshal.CreateSpan(ref token, 1));
            case TokenType.ExternThing:
            {
                var d = token.ToSpan(Raw)[1..].ToString();
                var type = TypeResolver.FindType(d, FileScope)!;
                ;
                if (type == null)
                    throw RCaronException.TypeNotFound(d);
                return new RCaronType(type);
            }
        }

        throw new Exception($"invalid tokentype to evaluate: {token.Type}");
    }

    [CollectionAccess(CollectionAccessType.Read)]
    public object SimpleEvaluateExpressionValue(ArraySegment<PosToken> tokens)
    {
        // repeat action something math
        var index = 0;
        object value = SimpleEvaluateExpressionSingle(tokens[0])!;
        if (tokens[1] is ArrayAccessorToken)
        {
        }

        while (index < tokens.Count - 1)
        {
            var op = tokens[index + 1].ToString(Raw);
            var second = SimpleEvaluateExpressionSingle(tokens[index + 2])!;
            switch (op)
            {
                case Operations.SumOp:
                    Horrors.AddTo(ref value, second);
                    break;
                case Operations.SubtractOp:
                    value = Horrors.Subtract(value, second);
                    break;
                case Operations.MultiplyOp:
                    value = Horrors.Multiply(value, second);
                    break;
                case Operations.DivideOp:
                    value = Horrors.Divide(value, second);
                    break;
                case Operations.ModuloOp:
                    value = Horrors.Modulo(value, second);
                    break;
                case Operations.RangeOp:
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
            }

            index += 2;
        }

        return value;
    }

    public object? SimpleEvaluateExpressionHigh(ArraySegment<PosToken> tokens)
        => tokens.Count switch
        {
            > 0 when tokens[0].Type == TokenType.Keyword => MethodCall(tokens[0].ToString(Raw),
                argumentTokens: tokens.AsSpan()[1..]),
            1 => SimpleEvaluateExpressionSingle(tokens[0]),
            > 2 => SimpleEvaluateExpressionValue(tokens),
            _ => throw new Exception("what he fuck")
        };

    public object? FunctionPlainCall(Function function, ReadOnlySpan<PosToken> argumentTokens)
    {
        LocalScope scope = null;
        if (function.Arguments != null)
        {
            scope ??= new();
            object?[] argumentValues = new object?[function.Arguments.Length];
            for (var i = 0; i < function.Arguments.Length; i++)
            {
                if (function.Arguments[i].DefaultValue?.Equals(RCaronInsideEnum.NoDefaultValue) ?? false)
                    argumentValues[i] = RCaronInsideEnum.NotAssigned;
                else
                    argumentValues[i] = function.Arguments[i].DefaultValue;
            }

            var argPos = 0;
            for (var i = 0; i < argumentTokens.Length; i++)
            {
                if (argumentTokens[i].Type == TokenType.Operator && argumentTokens[i].EqualsString(Raw, "-") &&
                    argumentTokens[i + 1].Type == TokenType.Keyword)
                {
                    var argName = argumentTokens[i + 1].ToSpan(Raw);
                    var j = 0;
                    for (; j < function.Arguments.Length; j++)
                    {
                        if (argName.SequenceEqual(function.Arguments[j].Name))
                            break;
                        else if (j == function.Arguments.Length - 1)
                            throw RCaronException.NamedArgumentNotFound(argName);
                    }

                    argumentValues[j] = SimpleEvaluateExpressionSingle(argumentTokens[i + 2]);
                    i += 2;
                    argPos++;
                }
                else
                {
                    for (var j = 0; j < function.Arguments.Length; j++)
                    {
                        if (argumentValues[j].Equals(RCaronInsideEnum.NotAssigned))
                        {
                            argumentValues[j] = SimpleEvaluateExpressionSingle(argumentTokens[i]);
                            argPos++;
                            break;
                        }
                        else if (j == function.Arguments.Length - 1)
                            throw RCaronException.LeftOverPositionalArgument();
                    }

                    argPos++;
                }
            }

            if (argumentValues.Contains(RCaronInsideEnum.NotAssigned))
                throw RCaronException.ArgumentsLeftUnassigned();

            scope.Variables ??= new();
            for (var i = 0; i < function.Arguments.Length; i++)
            {
                scope.Variables[function.Arguments[i].Name] = argumentValues[i];
            }
        }

        BlockStack.Push(new StackThing(false, true, scope));
        RunCodeBlock(function.CodeBlock);
        return ReturnValue;
    }

    public bool SimpleEvaluateBool(PosToken[] tokens)
    {
        var val1 = SimpleEvaluateExpressionSingle(tokens[0])!;
        if (tokens.Length == 1)
        {
            if (val1 is bool b)
                return b;
        }

        // todo: cant switch case with a Span yet -- rider doesnt support
        var op = tokens[1].ToString(Raw);
        var val2 = SimpleEvaluateExpressionSingle(tokens[2])!;
        switch (op)
        {
            case Operations.IsEqualOp:
                return val1.Equals(val2);
            case Operations.IsNotEqualOp:
                return !val1.Equals(val2);
            case Operations.IsGreaterOp:
                return Horrors.IsGreater(val1, val2);
            case Operations.IsGreaterOrEqualOp:
                return val1.Equals(val2) || Horrors.IsGreater(val1, val2);
            case Operations.IsLessOp:
                return !val1.Equals(val2) && Horrors.IsLess(val1, val2);
            case Operations.IsLessOrEqualOp:
                return val1.Equals(val2) || Horrors.IsLess(val1, val2);
            default:
                throw new RCaronException($"unknown operator: {op}", ExceptionCode.UnknownOperator);
        }
    }

    public object? GetVar(string name)
    {
        for (var i = 0; i < BlockStack.Count; i++)
        {
            var el = BlockStack.ElementAt(^(i + 1));
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
            var el = BlockStack.ElementAt(^(i + 1));
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
            var el = BlockStack.ElementAt(^(i + 1));
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