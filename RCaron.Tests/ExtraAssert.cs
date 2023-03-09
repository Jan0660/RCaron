using System.Reflection;
using JetBrains.Annotations;
using RCaron.Parsing;

namespace RCaron.Tests;

public static class ExtraAssert
{
    public static void ThrowsParsingCode(Action action, ExceptionCode exceptionCode)
    {
        var exc = Throws<ParsingException>(action);
        Assert.Equal(exceptionCode, exc.Code);
    }

    public static void ThrowsCode(Action action, ExceptionCode exceptionCode)
    {
        var exc = Throws<RCaronException>(action);
        Assert.Equal(exceptionCode, exc.Code);
    }

    public static T Throws<T>([InstantHandle] Action action) where T : Exception
    {
        var exc = ThrowsAnyException(action);
        Assert.IsType<T>(exc);
        return (T)exc;
    }

    public static Exception ThrowsAnyException([InstantHandle] Action action)
    {
        var exception = Assert.ThrowsAny<Exception>(action);
        while (exception is TargetInvocationException { InnerException: not null } targetInvocationException)
        {
            exception = targetInvocationException.InnerException;
        }

        // if (exception is TargetInvocationException { InnerException: not null } targetInvocationException)
        //     return targetInvocationException.InnerException;
        return exception;
    }
}