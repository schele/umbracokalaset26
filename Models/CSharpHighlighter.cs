using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UmbracoKalaset26.Models;

/// <summary>
/// C# for the code block (Models/CodeHighlighter.cs), coloured like Visual Studio 2022's default Dark and
/// Light themes. Roslyn parses the snippet; with nothing to compile it against, a name is classified by
/// where it stands: a type position, a method call, a parameter or local declared in the snippet. A
/// capitalised name before a dot that the snippet doesn't declare (<c>Console.WriteLine</c>) is taken to
/// be a type, and a type named I + capital (<c>IEmailSender</c>) an interface.
/// </summary>
public static class CSharpHighlighter
{
    private enum Kind { Plain, Keyword, Control, Class, Struct, Interface, Enum, TypeParameter, Method, Local, String, Number, Comment, Preprocessor }

    // Visual Studio 2022 (Tools → Options → Fonts and Colors), Dark and Light; null is the plain text colour.
    private static readonly Dictionary<Kind, (string Dark, string? Light)> Colors = new()
    {
        [Kind.Keyword] = ("#569CD6", "#0000FF"),
        [Kind.Control] = ("#D8A0DF", "#8F08C4"),
        [Kind.Class] = ("#4EC9B0", "#2B91AF"),
        [Kind.Struct] = ("#86C691", "#2B91AF"),
        [Kind.Interface] = ("#B8D7A3", "#2B91AF"),
        [Kind.Enum] = ("#B8D7A3", "#2B91AF"),
        [Kind.TypeParameter] = ("#B8D7A3", "#2B91AF"),
        [Kind.Method] = ("#DCDCAA", "#74531F"),
        [Kind.Local] = ("#9CDCFE", "#1F377F"),
        [Kind.String] = ("#D69D85", "#A31515"),
        [Kind.Number] = ("#B5CEA8", null),
        [Kind.Comment] = ("#57A64A", "#008000"),
        [Kind.Preprocessor] = ("#9B9B9B", "#808080"),
    };

    // "Keyword - Control" in Visual Studio. default, in and when only count inside a switch, foreach or catch.
    private static readonly HashSet<SyntaxKind> ControlKeywords =
    [
        SyntaxKind.IfKeyword, SyntaxKind.ElseKeyword, SyntaxKind.WhileKeyword, SyntaxKind.DoKeyword,
        SyntaxKind.ForKeyword, SyntaxKind.ForEachKeyword, SyntaxKind.SwitchKeyword, SyntaxKind.CaseKeyword,
        SyntaxKind.TryKeyword, SyntaxKind.CatchKeyword, SyntaxKind.FinallyKeyword, SyntaxKind.GotoKeyword,
        SyntaxKind.BreakKeyword, SyntaxKind.ContinueKeyword, SyntaxKind.ReturnKeyword, SyntaxKind.ThrowKeyword,
        SyntaxKind.YieldKeyword, SyntaxKind.DefaultKeyword, SyntaxKind.InKeyword, SyntaxKind.WhenKeyword,
    ];

    /// <summary>The code, HTML-encoded, with a coloured <c>&lt;span&gt;</c> per token or comment.</summary>
    public static string ToHtml(string code, bool dark)
    {
        SyntaxNode root = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
        var snippet = new Snippet(root);
        var html = new StringBuilder();

        void Append(string text, Kind kind)
        {
            // A comment or directive ends with its line break: keep the break outside the colour.
            string body = text.TrimEnd('\r', '\n');
            string? color = kind == Kind.Plain ? null : dark ? Colors[kind].Dark : Colors[kind].Light;
            if (body.Length > 0)
            {
                html.Append(color is null ? Encode(body) : $"<span style=\"color:{color};\">{Encode(body)}</span>");
            }

            html.Append(text, body.Length, text.Length - body.Length);
        }

        void AppendTrivia(SyntaxTriviaList trivia)
        {
            foreach (SyntaxTrivia item in trivia)
            {
                Append(item.ToFullString(), item.Kind() switch
                {
                    SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
                        or SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia => Kind.Comment,
                    SyntaxKind.DisabledTextTrivia => Kind.Preprocessor,
                    _ when item.IsDirective => Kind.Preprocessor,
                    _ => Kind.Plain,
                });
            }
        }

        foreach (SyntaxToken token in root.DescendantTokens())
        {
            AppendTrivia(token.LeadingTrivia);
            Append(token.Text, snippet.Classify(token));
            AppendTrivia(token.TrailingTrivia);
        }

        return html.ToString();
    }

    // Element content only needs these three.
    private static string Encode(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>What the snippet declares, to tell its types, members and locals apart.</summary>
    private sealed class Snippet
    {
        private readonly SyntaxNode _root;
        private readonly Dictionary<string, Kind> _types = [];
        private readonly HashSet<string> _typeParameters = [];
        private readonly HashSet<string> _members = [];
        private readonly Dictionary<SyntaxNode, HashSet<string>> _locals = [];

        public Snippet(SyntaxNode root)
        {
            _root = root;
            foreach (SyntaxNode node in root.DescendantNodes())
            {
                switch (node)
                {
                    case TypeParameterSyntax p:
                        _typeParameters.Add(p.Identifier.ValueText);
                        break;
                    case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax:
                        SyntaxToken name = node is BaseTypeDeclarationSyntax t ? t.Identifier : ((DelegateDeclarationSyntax)node).Identifier;
                        _types[name.ValueText] = Declaration(node);
                        break;
                    default:
                        if (DeclaredName(node) is { } member) _members.Add(member);
                        break;
                }
            }
        }

        public Kind Classify(SyntaxToken token)
        {
            SyntaxKind kind = token.Kind();
            if (kind == SyntaxKind.NumericLiteralToken) return Kind.Number;
            if (kind == SyntaxKind.CharacterLiteralToken
                || (kind != SyntaxKind.IdentifierToken && !SyntaxFacts.IsKeywordKind(kind) && kind.ToString().Contains("String", StringComparison.Ordinal)))
            {
                return Kind.String;
            }

            if (SyntaxFacts.IsKeywordKind(kind)) return IsControl(token) ? Kind.Control : Kind.Keyword;
            if (kind != SyntaxKind.IdentifierToken) return Kind.Plain;

            return token.Parent switch
            {
                BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or ConstructorDeclarationSyntax or DestructorDeclarationSyntax
                    => token.Parent is ConstructorDeclarationSyntax or DestructorDeclarationSyntax ? Kind.Class : Declaration(token.Parent),
                TypeParameterSyntax => Kind.TypeParameter,
                MethodDeclarationSyntax or LocalFunctionStatementSyntax => Kind.Method,
                ParameterSyntax or ForEachStatementSyntax or CatchDeclarationSyntax or SingleVariableDesignationSyntax => Kind.Local,
                VariableDeclaratorSyntax { Parent.Parent: not BaseFieldDeclarationSyntax } => Kind.Local,
                SimpleNameSyntax name => Reference(name),
                _ => Kind.Plain,
            };
        }

        private static bool IsControl(SyntaxToken token) =>
            ControlKeywords.Contains(token.Kind()) && token.Kind() switch
            {
                SyntaxKind.DefaultKeyword => token.Parent is DefaultSwitchLabelSyntax,
                SyntaxKind.InKeyword => token.Parent is CommonForEachStatementSyntax,
                SyntaxKind.WhenKeyword => token.Parent is WhenClauseSyntax or CatchFilterClauseSyntax,
                _ => true,
            };

        private static Kind Declaration(SyntaxNode node) => node switch
        {
            StructDeclarationSyntax => Kind.Struct,
            RecordDeclarationSyntax r => r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? Kind.Struct : Kind.Class,
            InterfaceDeclarationSyntax => Kind.Interface,
            EnumDeclarationSyntax => Kind.Enum,
            _ => Kind.Class,
        };

        private static string? DeclaredName(SyntaxNode node) => node switch
        {
            PropertyDeclarationSyntax p => p.Identifier.ValueText,
            EventDeclarationSyntax e => e.Identifier.ValueText,
            MethodDeclarationSyntax m => m.Identifier.ValueText,
            LocalFunctionStatementSyntax f => f.Identifier.ValueText,
            EnumMemberDeclarationSyntax e => e.Identifier.ValueText,
            VariableDeclaratorSyntax v => v.Identifier.ValueText,
            _ => LocalName(node),
        };

        private static string? LocalName(SyntaxNode node) => node switch
        {
            ParameterSyntax p => p.Identifier.ValueText,
            VariableDeclaratorSyntax { Parent.Parent: not BaseFieldDeclarationSyntax } v => v.Identifier.ValueText,
            ForEachStatementSyntax f => f.Identifier.ValueText,
            CatchDeclarationSyntax c when !c.Identifier.IsMissing => c.Identifier.ValueText,
            SingleVariableDesignationSyntax s => s.Identifier.ValueText,
            _ => null,
        };

        // A name that isn't a declaration: a type, a method call, a local, or a member.
        private Kind Reference(SimpleNameSyntax name)
        {
            string text = name.Identifier.ValueText;

            // System.Text.StringBuilder: only the last part is the type.
            SyntaxNode standalone = name;
            while (standalone.Parent is QualifiedNameSyntax q && q.Right == standalone
                || standalone.Parent is AliasQualifiedNameSyntax a && a.Name == standalone)
            {
                standalone = standalone.Parent;
            }

            if (standalone.Parent is AttributeSyntax) return Kind.Class;
            if (SyntaxFacts.IsInTypeOnlyContext((ExpressionSyntax)standalone))
            {
                return name.IsVar || name.IsNint || name.IsNuint || text == "dynamic" ? Kind.Keyword : TypeKind(text);
            }

            bool isMemberName = name.Parent is MemberAccessExpressionSyntax m && m.Name == name || name.Parent is MemberBindingExpressionSyntax;
            SyntaxNode callee = isMemberName ? name.Parent! : name;
            if (callee.Parent is InvocationExpressionSyntax call && call.Expression == callee)
            {
                return text == "nameof" ? Kind.Keyword : Kind.Method;
            }

            if (isMemberName) return Kind.Plain;
            if (LocalsIn(Scope(name)).Contains(text)) return Kind.Local;

            // Console.WriteLine: a capitalised receiver the snippet doesn't declare is a type.
            return name.Parent is MemberAccessExpressionSyntax { Expression: var receiver } && receiver == name
                && char.IsUpper(text[0]) && !_members.Contains(text)
                ? TypeKind(text)
                : Kind.Plain;
        }

        private Kind TypeKind(string name) =>
            _typeParameters.Contains(name) ? Kind.TypeParameter
            : _types.TryGetValue(name, out Kind declared) ? declared
            : name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]) ? Kind.Interface
            : Kind.Class;

        // Locals and parameters are looked up in the member the name is used in; top-level statements share one scope.
        private SyntaxNode Scope(SyntaxNode node) =>
            node.Ancestors().FirstOrDefault(a => a is BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax
                or BaseFieldDeclarationSyntax or GlobalStatementSyntax) is { } member and not GlobalStatementSyntax
                ? member
                : _root;

        private HashSet<string> LocalsIn(SyntaxNode scope)
        {
            if (!_locals.TryGetValue(scope, out HashSet<string>? names))
            {
                IEnumerable<SyntaxNode> parts = scope is CompilationUnitSyntax unit ? unit.Members.OfType<GlobalStatementSyntax>() : [scope];
                names = parts.SelectMany(part => part.DescendantNodesAndSelf())
                    .Select(LocalName)
                    .OfType<string>()
                    .ToHashSet();
                _locals[scope] = names;
            }

            return names;
        }
    }
}
