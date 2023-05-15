using RCaron.Classes;
using RCaron.LibrarySourceGenerator;

namespace RCaron.BaseLibrary;

[Module("Reflection")]
public partial class ReflectionModule : IRCaronModule
{
    /// <summary>
    /// Returned by <see cref="GetProperty"/> and <see cref="GetStaticProperty"/> when the property doesn't exist.
    /// </summary>
    public static readonly object NotFound = new();

    [Method("Is-ClassInstance", Description = "Returns true if the object is a ClassInstance")]
    public static bool IsClassInstance(Motor _, object? obj)
        => obj is ClassInstance;

    [Method("Is-ClassDefinition", Description = "Returns true if the object is a ClassDefinition")]
    public static bool IsClassDefinition(Motor _, object? obj)
        => obj is ClassDefinition;

    [Method("Get-ClassDefinition", Description = "Returns the ClassDefinition of the given ClassInstance")]
    public static ClassDefinition? GetClassDefinition(Motor _, ClassInstance instance)
        => instance.Definition;

    [Method("Get-Function", Description = "Returns the function with the given name or null if it doesn't exist")]
    public static Function? GetFunction(Motor _, ClassDefinition definition, string name)
        => definition.Functions?.GetValueOrDefault(name);

    [Method("Get-StaticFunction",
        Description = "Returns the static function with the given name or null if it doesn't exist")]
    public static Function? GetStaticFunction(Motor _, ClassDefinition definition, string name)
        => definition.StaticFunctions?.GetValueOrDefault(name);

    [Method("Get-Property",
        Description = "Returns the property with the given name or ReflectionModule.NotFound if it doesn't exist")]
    public static object? GetProperty(Motor _, ClassInstance instance, string name)
    {
        var index = instance.Definition.GetPropertyIndex(name);
        if (index == -1) return NotFound;
        return instance.PropertyValues![index];
    }

    [Method("Get-StaticProperty",
        Description =
            "Returns the value of the static property with the given name or ReflectionModule.NotFound if it doesn't exist")]
    public static object? GetStaticProperty(Motor _, ClassDefinition definition, string name)
    {
        var index = definition.GetStaticPropertyIndex(name);
        if (index == -1) return NotFound;
        return definition.StaticPropertyValues![index];
    }
}