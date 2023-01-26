using System.Buffers;

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
                object?[]? argsFinal = null;
                if (func.Value.OriginalFunction.Arguments is not null)
                {
                    var l = func.Value.OriginalFunction.Arguments.Length;
                    Span<bool> assigned = ArrayPool<bool>.Shared.Rent(l).AsSpan()[..l];
                    // Span<bool> assigned = stackalloc bool[func.Value.OriginalFunction.Arguments.Length];
                    argsFinal = new object?[l];

                    for (int i = 0; i < args.Positional.Length; i++)
                    {
                        argsFinal[i] = args.Positional[i];
                        assigned[i] = true;
                    }

                    for (var i = 0; i < args.NamedNames.Length; i++)
                    {
                        var index = 0;
                        for (; index < func.Value.OriginalFunction.Arguments.Length; index++)
                        {
                            if (func.Value.OriginalFunction.Arguments[index].Name.SequenceEqual(args.NamedNames[i]))
                            {
                                argsFinal[index] = args.NamedValues[i];
                                assigned[index] = true;
                            }
                            else if (index == func.Value.OriginalFunction.Arguments.Length - 1)
                                throw RCaronException.NamedArgumentNotFound(args.NamedNames[i]);
                        }
                    }

                    for (var i = 0; i < argsFinal.Length; i++)
                    {
                        if(assigned[i] == false)
                        {
                            if (!func.Value.OriginalFunction.Arguments[i].DefaultValue?.Equals(RCaronInsideEnum.NoDefaultValue) ?? true)
                                argsFinal[i] = func.Value.OriginalFunction.Arguments[i].DefaultValue;
                            else
                                throw RCaronException.ArgumentsLeftUnassigned();
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