namespace RCaron.Tests;

public static class ExtraAssert
{
    public static void ThrowsCode(Action action, ExceptionCode exceptionCode)
    {
        var exc = Assert.Throws<RCaronException>(action);
        Assert.Equal(exceptionCode, exc.Code);
    }
}