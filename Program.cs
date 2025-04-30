using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MonoStubGenerator;

class Program
{
    static async Task Main(string[] args)
    {
        // Input and output directories
        string inputDirectory = args[0];
        string outputDirectory = args[1];
        Console.WriteLine($"Generate stubs from directory: {inputDirectory} into {outputDirectory}");

        // Ensure output directory exists
        Directory.CreateDirectory(outputDirectory);

        // Process each .cs file in the input directory
        foreach (var file in Directory.EnumerateFiles(inputDirectory, "*.cs", SearchOption.AllDirectories))
        {
            Console.WriteLine($"Processing file: {file}");
            string code = File.ReadAllText(file);
            string stubCode = await GenerateStubCode(code);

            // Write stub code to output directory
            string relativePath = Path.GetRelativePath(inputDirectory, file);
            string outputPath = Path.Combine(outputDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, stubCode);
        }

    }

    static async Task<string> GenerateStubCode(string code)
    {
        // Parse the code into a syntax tree
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = await syntaxTree.GetRootAsync();

        // Create a rewriter to strip method bodies and preserve structures
        var rewriter = new StubRewriter();
        var newRoot = rewriter.Visit(root);

        // Format the output code
        return newRoot.NormalizeWhitespace().ToFullString();
    }
}

class StubRewriter : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node) => null;
    public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node) => null;
    public override SyntaxNode? VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node) => null;
    public override SyntaxNode? VisitDestructorDeclaration(DestructorDeclarationSyntax node) => null;

    private bool IsAbstract(SyntaxTokenList modifiers) => modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword));
    private bool IsInterfaceImplementation(SyntaxNode node)
    {
        bool hasExplicitInterface = false;
        if (node is MethodDeclarationSyntax method)
            hasExplicitInterface = method.ExplicitInterfaceSpecifier != null;
        else if (node is PropertyDeclarationSyntax property)
            hasExplicitInterface = property.ExplicitInterfaceSpecifier != null;

        return hasExplicitInterface ||
               (node.Parent is ClassDeclarationSyntax classDecl &&
                classDecl.BaseList?.Types.Any(t => t.Type.ToString().StartsWith("I")) == true);
    }

    private BlockSyntax CreateNotImplementedBlock()
    {
        return SyntaxFactory.Block(
            SyntaxFactory.ThrowStatement(
                SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.ParseTypeName("System.NotImplementedException"))
                .WithArgumentList(SyntaxFactory.ArgumentList())));
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (!IsAbstract(node.Modifiers) && IsInterfaceImplementation(node))
            return base.VisitMethodDeclaration(node
                .WithExpressionBody(null)
                .WithBody(CreateNotImplementedBlock())
                .WithSemicolonToken(default));
        else
            return null;
    }

    public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        if (!IsAbstract(node.Modifiers) && IsInterfaceImplementation(node))
        {
            var accessorList = SyntaxFactory.AccessorList(
                SyntaxFactory.List(new[]
                {
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithExpressionBody(null)
                        .WithBody(CreateNotImplementedBlock()),
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithExpressionBody(null)
                        .WithBody(CreateNotImplementedBlock())
                }));
            return base.VisitPropertyDeclaration(node.WithAccessorList(accessorList)
                       .WithInitializer(null)
                       .WithExpressionBody(null)
                       .WithSemicolonToken(default));
        }
        else
            return null;
    }

    private static readonly string[] allowedAttributes = ["HideInInspector", "Tooltip", "SerializeField", "Serializable", "Obsolete", "RequireComponent", "Range", "NonSerialized", "DisallowMultipleComponent"];
    public override SyntaxNode? VisitAttributeList(AttributeListSyntax node)
    {
        if (node.Target != null) return null; // zap all assembly attributes

        var xs = node.Attributes.Where(node => allowedAttributes.Contains(node.Name.ToString()));
        if (xs.Count() == 0) return null; // empty

        return node.WithAttributes(SyntaxFactory.SeparatedList(xs));
    }

    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        if (node.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword) || m.IsKind(SyntaxKind.ReadOnlyKeyword)))
            return node; // assume safe

        var xs = node.Declaration.Variables.Select(node => node.Initializer?.Value is LiteralExpressionSyntax ? node : node.WithInitializer(null));
        return base.VisitFieldDeclaration(node.WithDeclaration(node.Declaration.WithVariables(
            SyntaxFactory.SeparatedList(xs))));
    }
}
