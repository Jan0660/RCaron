using System.Reflection;
using JetBrains.Annotations;

namespace RCaron.Tests;

public static class ExtraAssert
{
    public static void ThrowsCode(Action action, ExceptionCode exceptionCode)
    {
        var exc = Throws<RCaronException>(action);
        Assert.Equal(exceptionCode, exc.Code);
    }
    
    public static T Throws<T>([InstantHandle] Action action) where T : Exception
    {
        var exc = ThrowsAnyException(action);
        Assert.IsType<T>(exc);
        return (T) exc;
    }

    public static Exception ThrowsAnyException([InstantHandle] Action action)
    {
        var exception = Assert.ThrowsAny<Exception>(action);
        if (exception is TargetInvocationException { InnerException: not null } targetInvocationException)
            return targetInvocationException.InnerException;
        return exception;
    }
}