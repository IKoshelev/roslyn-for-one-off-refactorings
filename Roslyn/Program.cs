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
using Microsoft.CodeAnalysis.Formatting;

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

            await RunQueryExample(allNodes, compilation);
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
                             (x) => x.node.NameColon?.Name.ToString()// named argument
                                     ?? parameters[x.index].Name,    // positional argument
                             (x) => x.node.Expression);

                    return result;
                }
            });

            Solution solution = null; 

            // Now we replace all found calls with equivalent calls to new method
            foreach (var x in callsToReplace.GroupBy(x => x.document))
            {
                solution = solution ?? x.Key.Project.Solution;
                // 'Solution' is the root node of our entire project graph,
                // solution  ==(1..x)==> projects ==(1..x)==> documents.
                // Since entire graph is immutable,
                // any change in it effctively produces a new solution graph,
                // so we must always start by getting the 'current' version
                // of the document we want to edit.
                var currentDocument = solution.GetDocument(x.Key.Id);
                var editor = await DocumentEditor.CreateAsync(currentDocument);

                foreach (var context in x)
                {
                    editor.ReplaceNode(
                        context.meta.node,
                        PrepareNewMethodCall(
                                            context.meta.node,
                                            context.meta.arguments));
                }
                var newDocument = editor.GetChangedDocument();
                solution = newDocument.Project.Solution;
            }

            workspace.TryApplyChanges(solution);

            static SyntaxNode PrepareNewMethodCall(
                InvocationExpressionSyntax oldCall, 
                Dictionary<string, ExpressionSyntax> oldArgumentExpressions)
            {
                // When we prepare a new SyntaxTree, we start with the 'leaves',
                // nodes which have no descendants or very few of them.
                // This is because, ideally, we don't want to construct
                // graphs of more than 3 nodes at a time.
                // When we have the "leaves" - we compose them into branches,
                // then branches into more branches and so on recursively,
                // each time dealing with composition of just a few nodes.
                // There is nothing preventing us from constructing entire sub-tree
                // in one go, but that aproach is usually harder to read and reason about
                // (you'll see why below).

                // First we prepare new arguments one by one
                var newReportNameArgument = Argument(oldArgumentExpressions["reportName"]);

                // Luckly for us, these don't change between old and new method,
                // else we would have to map them.
                var userIdParamNames = new string[]
                {
                    "userIdInAccountingSystem",
                    "userIdInHrSystemSystem",
                    "userIdInSalesSystem",
                    "userIdInSupplySystem"
                };

                var userIdExpressions = oldArgumentExpressions
                                            .Where(x => userIdParamNames.Contains(x.Key)
                                                        && x.Value.Kind() != SyntaxKind.NullLiteralExpression)
                                            .ToArray();

                SyntaxNode newUserIdsArgument = null;
                if (userIdExpressions.Any()) 
                {
                    // array of 'useIdInSystemX: expression'
                    var userIdsAsNamedArguments = userIdExpressions
                        .Select(x =>
                                Argument(x.Value)
                                    .WithNameColon(
                                        NameColon(IdentifierName(x.Key))));

                    // new UserIdsAcrossSystems(
                    //      useIdInSystemX: expression,
                    //      useIdInSystemY: expression)
                    newUserIdsArgument = Argument(
                                            ObjectCreationExpression(
                                              IdentifierName("UserIdsAcrossSystems"))
                                                .WithArgumentList(
                                                  ArgumentList(
                                                    SeparatedList(userIdsAsNamedArguments))));
                }

                ;
                ArgumentSyntax newScheduleWithPriorityArgument = null;
                if (oldArgumentExpressions.TryGetValue("priority", out var scheduleWithPriorityExpression)
                    && scheduleWithPriorityExpression.Kind() != SyntaxKind.NullLiteralExpression)
                {
                    newScheduleWithPriorityArgument = Argument(scheduleWithPriorityExpression);
                }

                // Now we put new arguments in a list, if they are needed (not null).
                // To keep things beautifull, we switch to named arguments
                // once any positional argument is skipped.

                var newArgumentNodesList = new List<SyntaxNodeOrToken>();
                newArgumentNodesList.Add(newReportNameArgument);

                var switchedToNamedArguemntMode = false;
                if(newUserIdsArgument != null)
                {
                    newArgumentNodesList.Add(Token(SyntaxKind.CommaToken));
                    newArgumentNodesList.Add(newUserIdsArgument);
                } 
                else
                {
                    switchedToNamedArguemntMode = true;
                }

                if(newScheduleWithPriorityArgument != null)
                {
                    if(switchedToNamedArguemntMode)
                    {
                        newScheduleWithPriorityArgument = 
                            newScheduleWithPriorityArgument
                                .WithNameColon(
                                    NameColon(
                                        IdentifierName("scheduleWithPriority")));
                    }
                    newArgumentNodesList.Add(Token(SyntaxKind.CommaToken));
                    newArgumentNodesList.Add(newScheduleWithPriorityArgument);
                }

                var newCall = InvocationExpression(
                                  MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    (oldCall.Expression as MemberAccessExpressionSyntax).Expression,
                                    IdentifierName("ScheduleReport")))
                                .WithArgumentList(
                                     ArgumentList(
                                        SeparatedList<ArgumentSyntax>(newArgumentNodesList)));

                // Since our new method returns an object intead of just id,
                // we must now add id extraction after call.
                var getIdFromNewCallResult = MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    newCall,
                                    IdentifierName("id"));

                // We must remember to call "NormalizeWhitespace",
                // without it there will be no spaces at all in code,
                // and it will not compile.
                getIdFromNewCallResult = getIdFromNewCallResult
                                            .NormalizeWhitespace(elasticTrivia: true);

                return getIdFromNewCallResult;
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
