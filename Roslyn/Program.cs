using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Build.Locator;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace Roslyn
{
    class Program
    {
        async static Task Main(string[] args)
        {
            // make sure Microsoft.CodeAnalysis.CSharp.Workspaces is loaded
            var _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);
            MSBuildLocator.RegisterDefaults();

            Regex whitespaceRemover = new Regex(@"\s\s+");

            using var workspace = MSBuildWorkspace.Create();
            var project = await workspace.OpenProjectAsync(@"..\..\..\..\TestSubjst\TestSubjst.csproj");

            IEnumerable<(Document document, SyntaxNode node)> allNodes =
                    await GetAllNodeOfAllDocuments(project.Documents.ToArray());

            var compilation = await project.GetCompilationAsync();

            var contextsOfInterest = await GetContextsOfInterest(allNodes, async (x) =>
            {
                var nodesOfInterest = new[]{    SyntaxKind.LogicalOrExpression,
                                                SyntaxKind.LogicalAndExpression,
                                                SyntaxKind.LogicalNotExpression,
                                                SyntaxKind.ParenthesizedExpression};

                var (document, node) = x;

                var isOfInterest = node.IsKindAny(nodesOfInterest);
                if (false == isOfInterest) { return null; }

                // Check if node is nested within a bigger logial expression.
                // We are only interesed in 'tips', NOT in nested nodes
                var isNestedWithinAnotherNodeOfInterest = node.Ancestors()
                    .TakeWhile(x => x.IsKindAny(nodesOfInterest))
                    .Any();

                if (isNestedWithinAnotherNodeOfInterest) { return null; }

                // We are only interested in complex expressions,
                // which contain at-least 3 logical operators
                var containedLogicalOperatorsCount = node.DescendantNodesAndSelf()
                    .Count(x => x.IsKindAny(SyntaxKind.LogicalOrExpression,
                                            SyntaxKind.LogicalAndExpression));

                if (containedLogicalOperatorsCount < 2) { return null; }

                var declaringMethodName = node.Ancestors()
                                            .SelectNotNull(x => x as MethodDeclarationSyntax)
                                            .FirstOrDefault()
                                            ?.Identifier.Text;

                var declaringTypeName = node.Ancestors()
                                            .SelectNotNull(x => x as TypeDeclarationSyntax)
                                            .FirstOrDefault()
                                            ?.Identifier.Text;

                var model = compilation.GetSemanticModel(
                                await document.GetSyntaxTreeAsync());

                // We are interested in logical expressinos which 
                // invlove the same members of the same type

                // Here we prepare lists of invlovled SimpleMemberAccessExpression
                // that will be used in similarity analysis down the road.
                // we are looking for expressions like x.y,
                // SimpleMemberAccessExpression on an IdentifierName
                // bound to an instance of some type.
                // We will get the 'x.y' part of expressions like 
                // x.y
                // x.y.z.c
                // x.y.Contains(...)
                (string typeName, string memberName)[] memberAccesExpressions
                    = node
                        .DescendantNodesAndSelf()
                        .Where(x => x.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                        .SelectNotNull(x => x as MemberAccessExpressionSyntax)
                        .Where(x => x.Expression.IsKind(SyntaxKind.IdentifierName))
                        .Select(x =>
                        (
                            typeName: model.GetTypeInfo(x.Expression).Type?.Name,
                            memberName: x.Name.Identifier.Text
                        ))
                        .ToArray();

                var fullText = node.GetText().ToString();
                var condensedText = whitespaceRemover.Replace(fullText, "");
                return new LoicalExpressionContext(
                    node,
                    memberAccesExpressions,
                    declaringMethodName,
                    declaringTypeName,
                    fullText,
                    condensedText);
            });

            // You will get some duplication: when context A has similar context B -
            // then context B will also be recorder to have similar context A.
            // This can be removed, but it may be a good idea to leave it there during exploraiton phase.
            var similarContexts = contextsOfInterest.Select(context =>
            {
                (Context<LoicalExpressionContext> context, int sameMembersAccessCount)[]
                    contextsWithSimilarMemberAccessed =
                        contextsOfInterest
                            .Where(x => x != context)
                            .Select(x => (
                                context: x,
                                sameMembersAccessCount: x.meta.memberAccessesInvolved
                                    .Intersect(context.meta.memberAccessesInvolved)
                                    .Count()))
                            .Where(x => x.sameMembersAccessCount >= 3) // at least 3 of the same members accessed
                            .OrderByDescending(x => x.sameMembersAccessCount)
                            .ToArray();

                return (context, similarContexts: contextsWithSimilarMemberAccessed);
            })
            .Where(x => x.similarContexts.Count() > 0)
            .OrderByDescending(x => x.similarContexts.FirstOrDefault().sameMembersAccessCount)
            .ToArray();

            var report = PrepareReport(similarContexts);
        }

        private static string PrepareReport(
            (Context<LoicalExpressionContext> context, (Context<LoicalExpressionContext> context, int sameMembersAccessCount)[] similarContexts)[] similarContexts)
        {
            var reportsPerContext = similarContexts
                                        .Select(x =>
@$"Original: {x.context.meta.declaringTypeName}.{x.context.meta.declaringMethodName}

{x.context.meta.fullText}

Similar: 

{string.Join("\r\n", x.similarContexts.Select(y =>
$@"{y.context.meta.declaringTypeName}.{y.context.meta.declaringMethodName}

{y.context.meta.fullText}

"))}
");

            var report =
@$"
{String.Join("\r\n--------------------------------------------------------------------------------------\r\n",
reportsPerContext)}
";

            return report;
        }

        record LoicalExpressionContext(
            SyntaxNode node,
            (string typeName, string memberName)[] memberAccessesInvolved,
            string declaringMethodName,
            string declaringTypeName,
            string fullText, 
            string condensedText);

        private static async Task<IEnumerable<(Document document, SyntaxNode node)>> GetAllNodeOfAllDocuments(Document[] documents)
        {
            return (await Task.WhenAll(
                    documents.Select(async (document) =>
                    {
                        var root = await document.GetSyntaxRootAsync();
                        return root.DescendantNodes()
                                .Select(node => (
                                    document,
                                    node
                                ));
                    })))
                .SelectMany(x => x);
        }

        record Context<T>(Document document, T meta);
        static async Task<Context<T>[]> GetContextsOfInterest<T>(
            IEnumerable<(Document document, SyntaxNode node)> contexts,
            Func<(Document document, SyntaxNode node), Task<T>> produceContextMaybe)
        where T : class
        {
            return (await Task.WhenAll(contexts
                    .Select(async x => new Context<T>(
                        x.document,
                        await produceContextMaybe(x)
                        )
                    )))
                    .Where(x => x.meta != null)
                    .ToArray();
        }

        
        
    }
}
