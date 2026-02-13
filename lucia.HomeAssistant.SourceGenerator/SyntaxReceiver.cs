using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace lucia.HomeAssistant.SourceGenerator;

public class SyntaxReceiver : ISyntaxReceiver
{
    public List<ClassDeclarationSyntax> CandidateClasses { get; } = new();

    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode is ClassDeclarationSyntax classDeclaration &&
            classDeclaration.AttributeLists.Count > 0)
        {
            CandidateClasses.Add(classDeclaration);
        }
    }
}