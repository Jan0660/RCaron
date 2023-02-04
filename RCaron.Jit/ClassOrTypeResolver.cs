using RCaron.Classes;

namespace RCaron.Jit;

public static class ClassOrTypeResolver
{
    public static (ClassDefinition? classDefinition, Type? type) Resolve(string name, FileScope? fileScope = null)
    {
        if(fileScope != null && Motor.TryGetClassDefinition(name, fileScope, out var classDef))
            return (classDef, null);
        var t = TypeResolver.FindType(name, fileScope);
        return (null, t);
    }

    public static object ResolveForUse(string name, FileScope? fileScope = null)
    {
        var (classDef, type) = Resolve(name, fileScope);
        if (classDef != null)
            return classDef;
        if (type != null)
            return new RCaronType(type);
        throw RCaronException.TypeNotFound(name);
    }
}