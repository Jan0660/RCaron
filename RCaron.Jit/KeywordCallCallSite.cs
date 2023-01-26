namespace RCaron.Jit;

public class KeywordCallCallSite
{
    public string Keyword { get; }
    public CompiledContext CompiledContext { get; }
    public KeywordCallCallSite(string keyword, CompiledContext compiledContext)
    {
        Keyword = keyword;
        CompiledContext = compiledContext;
    }

    public object Run(Arguments args)
    {
        foreach (var func in CompiledContext.Functions)
        {
            // todo: call precompile in the Hook
            if (func.Key.Equals(Keyword, StringComparison.InvariantCultureIgnoreCase))
            {
                object[]? argsFinal = null;
                if (func.Value.OriginalFunction.Arguments is not null)
                {
                    argsFinal = new object[func.Value.OriginalFunction.Arguments.Length];
                
                    for (int i = 0; i < argsFinal.Length; i++)
                    {
                        argsFinal[i] = args.Positional[i];
                    }
                    for (var i = 0; i < args.NamedNames.Length; i++)
                    {
                        var index = 0;
                        for (; index < func.Value.OriginalFunction.Arguments.Length; index++)
                        {
                            if (func.Value.OriginalFunction.Arguments[index].Name.SequenceEqual(args.NamedNames[i]))
                                break;
                            else if (index == func.Value.OriginalFunction.Arguments.Length - 1)
                                throw RCaronException.NamedArgumentNotFound(args.NamedNames[i]);
                        }
                    }
                }

                return func.Value.Invoke(argsFinal);
            }
        }

        throw new RCaronException($"Method of name '{Keyword}' not found", RCaronExceptionCode.MethodNotFound);
    }
    
    public record Arguments(object[] Positional, string[] NamedNames, object[] NamedValues);
}