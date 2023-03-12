namespace RCaron.LibrarySourceGenerator;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModuleAttribute : Attribute
{
    public ModuleAttribute(string name)
    {
        Name = name;
    }
    public string Name { get; set; }
    public bool ImplementModuleRun { get; set; } = true;
}