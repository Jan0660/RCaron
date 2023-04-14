using System.Collections.Immutable;

namespace RCaron;

public static class TypeResolver
{
    public static readonly ImmutableDictionary<string, Type> CSharpTypes = new Dictionary<string, Type>
    {
        { "bool", typeof(bool) },
        { "byte", typeof(byte) },
        { "sbyte", typeof(sbyte) },
        { "char", typeof(char) },
        { "decimal", typeof(decimal) },
        { "double", typeof(double) },
        { "float", typeof(float) },
        { "int", typeof(int) },
        { "uint", typeof(uint) },
        { "long", typeof(long) },
        { "ulong", typeof(ulong) },
        { "object", typeof(object) },
        { "short", typeof(short) },
        { "ushort", typeof(ushort) },
        { "string", typeof(string) },
    }.ToImmutableDictionary(StringComparer.InvariantCultureIgnoreCase);

    public static Type? FindType(string name, FileScope? fileScope = null)
    {
        // CSharp type names
        if (CSharpTypes.TryGetValue(name, out var cst))
            return cst;

        var usedNamespaces = fileScope?.UsedNamespaces;
        // name with no dot and no used namespaces
        if (!name.Contains('.') && usedNamespaces == null)
            return null;
        if (fileScope?.TypeCache != null && fileScope.TypeCache.TryGetValue(name, out var t))
            return t;
        var res = Type.GetType(name, false, true);
        if (res != null)
            return res;
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        // Type? endingMatch =  null;
        foreach (var ass in assemblies)
        {
            if (ass.IsDynamic)
                continue;
            foreach (var type in ass.ExportedTypes)
            {
                // if (type.FullName?.EndsWith(name, StringComparison.InvariantCultureIgnoreCase) ?? false)
                // {
                //     endingMatch = type;
                // }
                // exact match
                if (type.FullName?.Equals(name, StringComparison.InvariantCultureIgnoreCase) ?? false)
                {
                    return type;
                }

                if (type.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) &&
                    type.Namespace != null && (usedNamespaces?.Contains(type.Namespace) ?? false))
                {
                    if (fileScope != null)
                    {
                        fileScope.TypeCache ??= new();
                        fileScope.TypeCache.Add(name, type);
                    }

                    return type;
                }
            }
        }

        return null;
    }
}