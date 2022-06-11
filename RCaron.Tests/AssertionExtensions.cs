namespace RCaron.Tests;

public static class AssertionExtensions
{
    public static void AssertVariableEquals<T>(this Motor motor, string variableName, T expected)
    {
        Assert.Equal(expected, motor.GlobalScope.GetVariable(variableName));
    }

    public static void AssertVariableIsType<T>(this Motor motor, string variableName)
    {
        Assert.IsType<T>(motor.GlobalScope.GetVariable(variableName));
    }
}