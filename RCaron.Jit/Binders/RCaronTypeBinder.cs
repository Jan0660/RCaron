// using System.Collections.ObjectModel;
// using System.Dynamic;
// using System.Linq.Expressions;
// using System.Runtime.CompilerServices;
//
// namespace RCaron.Jit.Binders;
//
// public class RCaronTypeBinder : GetMemberBinder
// {
//     public override Expression Bind(object[] args, ReadOnlyCollection<ParameterExpression> parameters,
//         LabelTarget returnLabel)
//     {
//         return Expression.Constant(true);
//         return Expression.And(Expression.Equal(Expression.Constant(args[0]), parameters[0]),
//             Expression.Equal(Expression.Constant(args[1]), parameters[1]));
//         var typeName = (string)args[0];
//         var fileScope = (FileScope)args[1];
//         var type = TypeResolver.FindType(typeName, fileScope);
//         return Expression.Constant(type);
//     }
// }