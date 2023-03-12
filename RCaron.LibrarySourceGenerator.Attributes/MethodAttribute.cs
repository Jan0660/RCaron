namespace RCaron.LibrarySourceGenerator;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class MethodAttribute : Attribute
{
    public MethodAttribute(string name)
    {
        Name = name;
    }
    public string Name { get; set; }
}