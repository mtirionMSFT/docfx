using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

#nullable enable

namespace Microsoft.DocAsCode.Dotnet;

internal static partial class SymbolUrlResolver
{
    public static string? GetSymbolUrl(ISymbol symbol, Compilation compilation, HashSet<IAssemblySymbol> allAssemblies)
    {
        return GetDocfxUrl(symbol, allAssemblies)
            ?? GetMicrosoftLearnUrl(symbol)
            ?? GetPdbSourceLinkUrl(compilation, symbol);
    }

    internal static string? GetDocfxUrl(ISymbol symbol, HashSet<IAssemblySymbol> allAssemblies)
    {
        if (symbol.ContainingAssembly is null || !allAssemblies.Contains(symbol.ContainingAssembly))
            return null;

        var commentId = symbol.GetDocumentationCommentId();
        if (commentId is null)
            return null;

        var parts = commentId.Split(':');
        var type = parts[0];
        var uid = parts[1].ToLowerInvariant();

        return type switch
        {
            "N" or "T" => $"{uid.Replace('`', '-')}.html",
            "M" or "F" or "P" or "E" => $"{VisitorHelper.GetId(symbol.ContainingType).Replace('`', '-')}.html#{Regex.Replace(uid, @"/\W/", "_")}",
            _ => throw new NotSupportedException($"Unknown comment ID format '{type}"),
        };
    }
}
