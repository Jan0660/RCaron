using System;

namespace RCaron.LibrarySourceGenerator;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class MethodAttribute : Attribute
{
    public MethodAttribute(string name)
    {
        Name = name;
    }
    public string Name { get; set; }
}