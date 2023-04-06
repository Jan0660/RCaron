using System.Buffers;
using System.Reflection;

namespace RCaron;

public static class MethodResolver
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
                RCaronExceptionCode.MethodNoSuitableMatch);

        var bestMethod = methods[bestIndex];

        // attention: do not use arr or methods after this point
        ArrayPool<MethodBase>.Shared.Return(arr, true);
        return (bestMethod, needsNumericConversions[bestIndex], isExtensionMethods);
    }
}