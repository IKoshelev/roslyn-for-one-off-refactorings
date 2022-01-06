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
using Microsoft.CodeAnalysis.Editing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Roslyn
{
    class Program
    {
        public static Regex whitespaceRemover = new Regex(@"\s\s+");

        async static Task Main(string[] args)
        {
            // make sure Microsoft.CodeAnalysis.CSharp.Workspaces is loaded
            var _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);
            MSBuildLocator.RegisterDefaults();

            using var workspace = MSBuildWorkspace.Create();
            var project = await workspace.OpenProjectAsync(@"..\..\..\..\TestSubject\TestSubject.csproj");

            IEnumerable<(Document document, SyntaxNode node)> allNodes =
                    await GetAllNodeOfAllDocuments(project.Documents.ToArray());

            var compilation = new Lazy<Task<Compilation>>(() => project.GetCompilationAsync());

            //await RunQueryExample(allNodes, compilation);
            await RunCodeChangeExample(allNodes, compilation, workspace);
        }

        record LogicalExpressionContext(
            SyntaxNode node,
            (string typeName, string memberName)[] memberAccessesInvolved,
            string declaringMethodName,
            string declaringTypeName,
            string fullText,
            string condensedText);

        private static async Task RunQueryExample(
            IEnumerable<(Document document, SyntaxNode node)> allNodes, 
            Lazy<Task<Compilation>> compilation)
        {
            var contextsOfInterest = await GetContextsOfInterest(allNodes, async (x) =>
            {
                var partsOfLogicalExpression = new[]{
                                                SyntaxKind.LogicalOrExpression,
                                                SyntaxKind.LogicalAndExpression,
                                                SyntaxKind.LogicalNotExpression,
                                                SyntaxKind.ParenthesizedExpression};

                var (document, node) = x;

                if (false == node.IsKindAny(partsOfLogicalExpression)) { return null; }

                // Check if node is nested within a bigger logcial expression.
                // We are only interested in 'tips', NOT in nested nodes
                var isNestedWithinAnotherNodeOfInterest = node.Ancestors()
                    .TakeWhile(x => x.IsKindAny(partsOfLogicalExpression))
                    .Any();

                if (isNestedWithinAnotherNodeOfInterest) { return null; }

                // We are looking for complex expressions with multiple
                // || or && operators
                var containedLogicalOperatorsCount = node.DescendantNodesAndSelf()
                    .Count(x => x.IsKindAny(SyntaxKind.LogicalOrExpression,
                                            SyntaxKind.LogicalAndExpression));

                if (containedLogicalOperatorsCount < 2) { return null; }

                // By this point we know, that current examined node is of interest
                // to us, and all that is left is to prepare relevant 
                // metadata for future analysis.

                var declaringMethodName = node.Ancestors()
                                            .OfType<MethodDeclarationSyntax>()
                                            .FirstOrDefault()
                                            ?.Identifier.Text;

                var declaringTypeName = node.Ancestors()
                                            .OfType<TypeDeclarationSyntax>()
                                            .FirstOrDefault()
                                            ?.Identifier.Text;

                var model = (await compilation.Value).GetSemanticModel(
                                await document.GetSyntaxTreeAsync());

                // We prepare list of SimpleMemberAccessExpression nodes contained
                // within the syntax sub-tree rooted at current node (it's descendants),
                // the list will be used in similarity analysis down the road.
                // We are looking for expressions like 'x.y',
                // this code will be represented by a SimpleMemberAccessExpression
                // having Expression property of IdentifierName.
                // We will get the 'x.y' part of code like 
                // x.y
                // x.y.z.c
                // x.y.Contains(...)
                (string typeName, string memberName)[] memberAccessExpressions
                    = node
                        .DescendantNodesAndSelf()
                        .Where(x => x.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                        .OfType<MemberAccessExpressionSyntax>()
                        .Where(x => x.Expression.IsKind(SyntaxKind.IdentifierName))

                        // Note, that before we were working just with syntax.
                        // Here we are using semantic model of our code to get
                        // information about actual type contained in a given variable.
                        .Select(x =>
                        (
                            // you may want to include namespace
                            typeName: model.GetTypeInfo(x.Expression).Type?.Name,
                            memberName: x.Name.Identifier.Text
                        ))
                        .ToArray();

                var fullText = node.GetText().ToString();
                var condensedText = whitespaceRemover.Replace(fullText, "");
                return new LogicalExpressionContext(
                    node,
                    memberAccessExpressions,
                    declaringMethodName,
                    declaringTypeName,
                    fullText,
                    condensedText);
            });

            // You will get some duplication: when context A has similar context B -
            // then context B will also be recorded to have similar context A.
            // This can be removed, but it may be a good idea to leave it there during exploraiton phase.
            var similarContexts = contextsOfInterest.Select(context =>
            {
                (Context<LogicalExpressionContext> context, int sameMembersAccessCount)[]
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

            return;

            static string PrepareReport(
                (Context<LogicalExpressionContext> context, 
                (Context<LogicalExpressionContext> context, int sameMembersAccessCount)[] similarContexts)[] contexts)
            {
                var reportsPerContext = contexts
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
        }

        record MethodReplacementContext(
            InvocationExpressionSyntax node,
            Dictionary<string, ExpressionSyntax> arguments);

        private static async Task RunCodeChangeExample(
             IEnumerable<(Document document, SyntaxNode node)> allNodes,
             Lazy<Task<Compilation>> compilation,
             MSBuildWorkspace workspace)
        {

            var callsToReplace = await GetContextsOfInterest(allNodes, async (x) =>
            {
                var node = x.node as InvocationExpressionSyntax;
                if (node == null) { return null; }

                var methodMemberAccess = node.Expression as MemberAccessExpressionSyntax;
                if (methodMemberAccess == null) { return null; }

                var methodName = methodMemberAccess.Name;
                var methodNameAsText = methodName?.GetText().ToString();
                if (methodNameAsText != "ScheduleReport") { return null; }

                var model = (await compilation.Value).GetSemanticModel(
                                await x.document.GetSyntaxTreeAsync());

                var methodSymbolInfo = model.GetSymbolInfo(methodName).Symbol;

                // Make sure we've got exactly the method we are looking for,
                // and not one of its overloads.
                var displayStringWeAreLookingFor =
                    "TestSubject.CodeReplacementTestbed.ReportSchedulingSystem"
                    + ".ScheduleReport(string, bool?, int?, int?, int?, int?)";

                if ( methodSymbolInfo?.ToDisplayString() != displayStringWeAreLookingFor
                    || false == methodSymbolInfo?.GetAttributes()
                                    .Any(x => x.AttributeClass?.Name == "ObsoleteAttribute"))
                {
                    return null;
                }

                // Ok, we've got exactly the method we were looking for.
                // Now lets get information about call arguments
                var parsedArguments =  ParseArguments(
                                            node.ArgumentList, 
                                            (IMethodSymbol)methodSymbolInfo);

                return new MethodReplacementContext(
                    node,
                    parsedArguments);

                static Dictionary<string, ExpressionSyntax> ParseArguments(
                                                        ArgumentListSyntax argumentsList,
                                                        IMethodSymbol methodSymbol)
                {
                    var parameters = methodSymbol.Parameters.ToArray();

                    var result = argumentsList.Arguments
                         .Select((node, index) => (node, index))
                         .ToDictionary(
                             (x) => x.node.NameColon?.Name.ToString()    // named argument
                                     ?? parameters[x.index].Name,        // positional argument
                             (x) => x.node.NameColon?.Expression
                                     ?? x.node.Expression);

                    return result;
                }
            });

            // Now we replace all found calls with equivalent calls to new method

            foreach (var x in callsToReplace.GroupBy(x => x.document))
            {
                var editor = await DocumentEditor.CreateAsync(x.Key);

                foreach (var context in x)
                {
                    editor.ReplaceNode(context.meta.node,
                                            LiteralExpression(
                                              SyntaxKind.NullLiteralExpression));
                }
                var newDocument = editor.GetChangedDocument();
                workspace.TryApplyChanges(newDocument.Project.Solution);
            }
        }

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
