namespace RCaron.Classes;

public sealed class ClassDefinition
{
    public string Name { get; }
    public string[]? PropertyNames { get; }
    public PosToken[]?[]? PropertyInitializers { get; }
    public Dictionary<string, Function>? Functions { get; init; }
    // todo(feat): constructor

    public ClassDefinition(string name, string[]? propertyNames, PosToken[]?[]? propertyInitializers)
    {
        (Name, PropertyNames, PropertyInitializers) = (name, propertyNames, propertyInitializers);
    }
}