namespace RCaron.Tests;

public static class AssertionExtensions
{
    public static void VariableEquals<T>(this Motor motor, string variableName, T expected)
    {
        Assert.Equal(expected, motor.Variables[variableName]);
    }
}