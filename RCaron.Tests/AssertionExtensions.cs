namespace RCaron.Tests;

public static class AssertionExtensions
{
    public static void AssertVariableEquals<T>(this Motor motor, string variableName, T? expected)
    {
        Assert.Equal(expected, motor.GlobalScope.GetVariable(variableName));
    }

    /// <returns>The variable</returns>
    public static T AssertVariableIsType<T>(this Motor motor, string variableName)
    {
        var variable = motor.GlobalScope.GetVariable(variableName);
        Assert.IsType<T>(variable);
        return (T)variable;
    }

    public static void RunWithCode(this Motor motor, string code)
    {
        motor.UseContext(RCaronRunner.Parse(code));
        motor.Run();
    }
}