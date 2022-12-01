using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace RCaron.Jit.Binders;

public class RCaronTypeBinder : CallSiteBinder
{
    public override Expression Bind(object[] args, ReadOnlyCollection<ParameterExpression> parameters, LabelTarget returnLabel)
    {
        var typeName = (string)args[0];
        var fileScope = (FileScope)args[1];
        var type = TypeResolver.FindType(typeName, fileScope);
        return Expression.Constant(type);
    }
}