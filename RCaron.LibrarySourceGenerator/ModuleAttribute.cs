using System;

namespace RCaron.LibrarySourceGenerator;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ModuleAttribute : Attribute
{
    public ModuleAttribute(string name)
    {
        Name = name;
    }
    public string Name { get; set; }
}