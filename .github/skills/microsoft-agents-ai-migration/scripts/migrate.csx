// ⚠ AUTO-GENERATED MIGRATION SCRIPT — Review before running!
// Microsoft.Agents.AI Migration: 1.0.0-preview.251204.1 → 1.0.0-preview.260212.1
// Run with: dotnet script scripts/migrate.csx

#r "nuget: Microsoft.CodeAnalysis.CSharp, 4.8.0"

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  Microsoft.Agents.AI Migration Script                          ║");
Console.WriteLine("║  1.0.0-preview.251204.1 → 1.0.0-preview.260212.1               ║");
Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Configuration
var searchPath = Args.Count > 0 ? Args[0] : Directory.GetCurrentDirectory();
var dryRun = Args.Contains("--dry-run");

if (dryRun)
{
    Console.WriteLine("🔍 DRY RUN MODE - No files will be modified");
    Console.WriteLine();
}

// Type renames
var typeRenames = new Dictionary<string, string>
{
    // Session/Thread renames
    { "AgentThread", "AgentSession" },
    // Note: AgentThreadMetadata was REMOVED (PR #3067) - not renamed. Manual removal required.
    
    // Storage provider renames
    { "ChatMessageStore", "ChatHistoryProvider" },
    
    // Response class renames
    { "AgentRunResponse", "AgentResponse" },
    { "AgentRunResponseUpdate", "AgentResponseUpdate" },
    { "AgentRunResponseEvent", "AgentResponseEvent" },
    { "AgentRunUpdateEvent", "AgentUpdateEvent" },
    
    // GitHub casing fix
    { "GithubCopilotAgent", "GitHubCopilotAgent" },
    { "GithubCopilotAgentOptions", "GitHubCopilotAgentOptions" },
};

// Method renames (class.method -> new method name)
var methodRenames = new Dictionary<string, string>
{
    { "GetNewThread", "CreateSessionAsync" },
    { "GetNewSession", "CreateSession" },
    { "DeserializeThread", "DeserializeSessionAsync" },
    { "CreateAIAgent", "AsAIAgent" },
    { "GetAIAgent", "AsAIAgent" },
};

// Namespace renames
var namespaceRenames = new Dictionary<string, string>
{
    { "Microsoft.Agents.AI.Search", "Microsoft.Agents.AI" },
};

/// <summary>
/// Rewriter that handles type renames
/// </summary>
class TypeRenameRewriter : CSharpSyntaxRewriter
{
    private readonly Dictionary<string, string> _renames;
    public int ChangeCount { get; private set; }
    public List<string> Changes { get; } = new();

    public TypeRenameRewriter(Dictionary<string, string> renames)
    {
        _renames = renames;
    }

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        var identifier = node.Identifier.Text;
        if (_renames.TryGetValue(identifier, out var newName))
        {
            ChangeCount++;
            Changes.Add($"  Type: {identifier} → {newName}");
            return node.WithIdentifier(SyntaxFactory.Identifier(newName));
        }
        return base.VisitIdentifierName(node);
    }

    public override SyntaxNode VisitGenericName(GenericNameSyntax node)
    {
        var identifier = node.Identifier.Text;
        if (_renames.TryGetValue(identifier, out var newName))
        {
            ChangeCount++;
            Changes.Add($"  Generic Type: {identifier} → {newName}");
            var newNode = node.WithIdentifier(SyntaxFactory.Identifier(newName));
            return base.VisitGenericName(newNode);
        }
        return base.VisitGenericName(node);
    }
}

/// <summary>
/// Rewriter that handles method renames
/// </summary>
class MethodRenameRewriter : CSharpSyntaxRewriter
{
    private readonly Dictionary<string, string> _renames;
    public int ChangeCount { get; private set; }
    public List<string> Changes { get; } = new();

    public MethodRenameRewriter(Dictionary<string, string> renames)
    {
        _renames = renames;
    }

    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            if (_renames.TryGetValue(methodName, out var newName))
            {
                ChangeCount++;
                Changes.Add($"  Method: {methodName} → {newName}");
                
                var newMemberAccess = memberAccess.WithName(
                    SyntaxFactory.IdentifierName(newName));
                
                return node.WithExpression(newMemberAccess);
            }
        }
        return base.VisitInvocationExpression(node);
    }
}

/// <summary>
/// Rewriter that handles namespace renames in using directives
/// </summary>
class NamespaceRenameRewriter : CSharpSyntaxRewriter
{
    private readonly Dictionary<string, string> _renames;
    public int ChangeCount { get; private set; }
    public List<string> Changes { get; } = new();

    public NamespaceRenameRewriter(Dictionary<string, string> renames)
    {
        _renames = renames;
    }

    public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node)
    {
        var namespaceName = node.Name?.ToString();
        if (namespaceName != null && _renames.TryGetValue(namespaceName, out var newNamespace))
        {
            ChangeCount++;
            Changes.Add($"  Namespace: {namespaceName} → {newNamespace}");
            
            return node.WithName(SyntaxFactory.ParseName(newNamespace));
        }
        return base.VisitUsingDirective(node);
    }
}

// Find all C# files
var csFiles = Directory.GetFiles(searchPath, "*.cs", SearchOption.AllDirectories)
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
    .ToList();

Console.WriteLine($"📁 Searching in: {searchPath}");
Console.WriteLine($"📄 Found {csFiles.Count} C# files to process");
Console.WriteLine();

var totalChanges = 0;
var filesModified = 0;

foreach (var filePath in csFiles)
{
    var originalCode = File.ReadAllText(filePath);
    var tree = CSharpSyntaxTree.ParseText(originalCode);
    var root = tree.GetRoot();

    var fileChanges = new List<string>();

    // Apply type renames
    var typeRewriter = new TypeRenameRewriter(typeRenames);
    root = typeRewriter.Visit(root);
    fileChanges.AddRange(typeRewriter.Changes);

    // Apply method renames
    var methodRewriter = new MethodRenameRewriter(methodRenames);
    root = methodRewriter.Visit(root);
    fileChanges.AddRange(methodRewriter.Changes);

    // Apply namespace renames
    var namespaceRewriter = new NamespaceRenameRewriter(namespaceRenames);
    root = namespaceRewriter.Visit(root);
    fileChanges.AddRange(namespaceRewriter.Changes);

    var changeCount = typeRewriter.ChangeCount + methodRewriter.ChangeCount + namespaceRewriter.ChangeCount;

    if (changeCount > 0)
    {
        var relativePath = Path.GetRelativePath(searchPath, filePath);
        Console.WriteLine($"📝 {relativePath} ({changeCount} changes)");
        
        foreach (var change in fileChanges)
        {
            Console.WriteLine(change);
        }
        Console.WriteLine();

        if (!dryRun)
        {
            var newCode = root.ToFullString();
            File.WriteAllText(filePath, newCode);
        }

        totalChanges += changeCount;
        filesModified++;
    }
}

Console.WriteLine("════════════════════════════════════════════════════════════════");
Console.WriteLine($"✅ Migration complete!");
Console.WriteLine($"   Files modified: {filesModified}");
Console.WriteLine($"   Total changes: {totalChanges}");

if (dryRun)
{
    Console.WriteLine();
    Console.WriteLine("ℹ️  This was a dry run. Run without --dry-run to apply changes.");
}

Console.WriteLine();
Console.WriteLine("⚠️  IMPORTANT: Manual review required for:");
Console.WriteLine("   • Adding 'await' to async method calls (GetNewThread → CreateSessionAsync)");
Console.WriteLine("   • Updating method signatures to async where needed");
Console.WriteLine("   • Updating ChatHistoryProvider method signatures (new agent/session params)");
Console.WriteLine("   • Replacing AgentSession.Serialize() with AIAgent.SerializeSession()");
Console.WriteLine("   • Removing usages of deleted APIs:");
Console.WriteLine("     - AgentThreadMetadata (removed in v260108.1, PR #3067)");
Console.WriteLine("     - NotifyThreadOfNewMessagesAsync (removed in v251204.1, PR #2450)");
Console.WriteLine("     - UserInputRequests property (removed in v260205.1, PR #3682)");
Console.WriteLine("     - Display name property (removed in v251219.1, PR #2758)");
Console.WriteLine("   • Replacing sync extension methods with async equivalents (PR #3291)");
Console.WriteLine();
