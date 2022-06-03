namespace RCaron;

public static class TypeResolver
{
    public static Type? FindType(string name, IList<string>? usedNamespaces = null)
    {
        // name with no dot and no used namespaces
        if (!name.Contains('.') && usedNamespaces == null)
            return null;
        var res = Type.GetType(name, false, true);
        if (res != null)
            return res;
        // todo(perf): if no dot in name just search used namespaces
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        // Type? endingMatch =  null;
        foreach (var ass in assemblies)
        {
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
                    (usedNamespaces?.Contains(type.Namespace) ?? false))
                {
                    return type;
                }
            }
        }

        return null;
    }
}