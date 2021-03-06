﻿using System;
using System.Linq;
using ICSharpCode.CodeConverter.Util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using SyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using TypeSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax;
using VisualBasicExtensions = Microsoft.CodeAnalysis.VisualBasicExtensions;

namespace ICSharpCode.CodeConverter.CSharp
{
    internal static class SemanticModelExtensions
    {
        /// <summary>
        /// This check is entirely to avoid some unnecessary default initializations so the code looks less cluttered and more like the VB did.
        /// The caller should default to outputting an initializer which is always safe for equivalence/correctness.
        /// </summary>
        public static bool IsDefinitelyAssignedBeforeRead(this SemanticModel semanticModel, VariableDeclaratorSyntax localDeclarator, ModifiedIdentifierSyntax name)
        {
            Func<string, bool> equalsId = s => s.Equals(name.Identifier.ValueText, StringComparison.OrdinalIgnoreCase);

            // Find the first and second statements in the method (property, constructor, etc.) which contain the identifier
            // This may overshoot where there are multiple identifiers with the same name - this is ok, it just means we could output an initializer where one is not needed
            var statements = localDeclarator.GetAncestor<MethodBlockBaseSyntax>().Statements.Where(s =>
                s.DescendantTokens().Any(id => ((SyntaxToken) id).IsKind(SyntaxKind.IdentifierToken) && equalsId(id.ValueText))
            ).Take(2).ToList();
            var first = statements.First();
            var second = statements.Last();

            // Analyze the data flow in this block to see if initialization is required
            // If the second statement where the identifier is used is an if block, we look at the condition rather than the whole statement. This is an easy special
            // case which catches eg. the if (TryParse()) pattern. This could happen for any node which allows multiple statements.
            var dataFlow = second is MultiLineIfBlockSyntax ifBlock
                ? semanticModel.AnalyzeDataFlow(ifBlock.IfStatement.Condition)
                : semanticModel.AnalyzeDataFlow(first, second);

            bool alwaysAssigned = dataFlow.AlwaysAssigned.Any(s => equalsId(s.Name));
            bool readInside = dataFlow.ReadInside.Any(s => equalsId(s.Name));
            bool writtenInside = dataFlow.WrittenInside.Any(s => equalsId(s.Name));
            return alwaysAssigned && !writtenInside || !readInside;
        }

        public static TypeSyntax GetCsTypeSyntax(this SemanticModel vbSemanticModel, ITypeSymbol typeSymbol, VisualBasicSyntaxNode contextNode)
        {
            if (typeSymbol.IsNullable()) return SyntaxFactory.NullableType(GetCsTypeSyntax(vbSemanticModel, typeSymbol.GetNullableUnderlyingType(), contextNode));
            var predefined = typeSymbol.SpecialType.GetPredefinedKeywordKind();
            if (predefined != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None)
            {
                return SyntaxFactory.PredefinedType(SyntaxFactory.Token(predefined));
            }

            var typeName = typeSymbol.ToMinimalCSharpDisplayString(vbSemanticModel, contextNode.SpanStart);
            return SyntaxFactory.ParseTypeName(typeName);
        }
    }
}