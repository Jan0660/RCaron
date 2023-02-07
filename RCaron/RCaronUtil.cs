using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.CSharp.RuntimeBinder;

namespace RCaron;

public static class RCaronUtil
{
    public static CallSiteBinder GetBinaryOperationBinder(OperationEnum operation)
    {
        return Binder.BinaryOperation(CSharpBinderFlags.None, operation switch
        {
            OperationEnum.Sum => ExpressionType.Add,
            OperationEnum.Subtract => ExpressionType.Subtract,
            OperationEnum.Multiply => ExpressionType.Multiply,
            OperationEnum.Divide => ExpressionType.Divide,
            OperationEnum.Modulo => ExpressionType.Modulo,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Invalid operation.")
        }, null, new[]
        {
            CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
            CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
        });
    }
    public static CallSite<Func<CallSite, object, object, object>> GetBinaryOperationCallSite(OperationEnum operation)
    {
        var b = GetBinaryOperationBinder(operation);
        var callsite = CallSite<Func<CallSite, object, object, object>>.Create(b);
        return callsite;
    }
}