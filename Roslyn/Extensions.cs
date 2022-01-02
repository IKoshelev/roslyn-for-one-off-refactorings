using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Roslyn
{
    public static class Extensions
    {
        public static bool IsKindAny(this SyntaxNode node, params SyntaxKind[] syntaxKinds)
        {
            return syntaxKinds.Any(x => node.IsKind(x));
        }

        public static IEnumerable<TTarget> SelectNotNull<TSource, TTarget>(this IEnumerable<TSource> source, Func<TSource, TTarget> func)
        {
            return source.Select(x => func(x)).Where(x => x != null);
        }
    }
}
