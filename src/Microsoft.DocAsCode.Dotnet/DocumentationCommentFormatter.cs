// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Linq;
using Microsoft.DocAsCode.Common;

#nullable enable

namespace Microsoft.DocAsCode.Dotnet;

internal static class DocumentationCommentFormatter
{
    private static readonly Regex RegionRegex = new(@"^\s*#region\s*(.*)$");
    private static readonly Regex XmlRegionRegex = new(@"^\s*<!--\s*<([^/\s].*)>\s*-->$");
    private static readonly Regex EndRegionRegex = new(@"^\s*#endregion\s*.*$");
    private static readonly Regex XmlEndRegionRegex = new(@"^\s*<!--\s*</(.*)>\s*-->$");

    public delegate string? ResolveCrefDelegate(string cref, bool seealso);

    public static string Format(string rawXml, ResolveCrefDelegate? resolveCref = null, string? sourcePath = null, string? codeSourceBasePath = null)
    {
        // Recommended XML tags for C# documentation comments:
        // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/recommended-tags#code
        //
        // Sandcastle XML Comments Guide:
        // https://ewsoftware.github.io/XMLCommentsGuide/html/4268757F-CE8D-4E6D-8502-4F7F2E22DDA3.htm
        var result = new StringBuilder();
        FormatNode(XElement.Parse($"<tag>{rawXml}</tag>", LoadOptions.PreserveWhitespace));
        return result.ToString();

        StringBuilder FormatNode(XNode node)
        {
            return node switch
            {
                XText text => result.Append(HttpUtility.HtmlEncode(text.Value)),
                XElement element => FormatElement(element),
                _ => result,
            };

            StringBuilder FormatElement(XElement e)
            {
                return e.Name.LocalName switch
                {
                    "para" => FormatChildNodes("<p>", "</p>"),
                    "code" => FormatCode(),
                    "term" => FormatChildNodes("<span class=\"term\">", "</span>"),
                    "description" => FormatChildNodes(),
                    "list" => e.Attribute("type")?.Value switch
                    {
                        "table" => FormatTable(),
                        "number" => FormatList("<ol>", "</ol>"),
                        _ => FormatList("<ul>", "</ul>"),
                    },
                    "typeparamref" or "paramref" => FormatChildNodes("<c>", "</c>"),
                    "see" or "seealso" => FormatSee(),
                    "note" => FormatNote(),
                    _ => FormatChildNodes($"<{e.Name.LocalName}>", $"</{e.Name.LocalName}>"),
                };

                StringBuilder FormatChildNodes(string? open = null, string? close = null)
                {
                    if (open != null)
                        result.Append(open);

                    foreach (var child in e.Nodes())
                        FormatNode(child);

                    if (close != null)
                        result.Append(close);

                    return result;
                }

                StringBuilder FormatTable()
                {
                    result.Append("<table>");
                    if (e.Elements().FirstOrDefault(e => e.Name.LocalName is "listheader") is { } listheader)
                    {
                        result.Append("<thead><tr>");
                        foreach (var child in listheader.Nodes())
                        {
                            result.Append("<td>");
                            FormatNode(child);
                            result.Append("</td>");
                        }
                        result.Append("</tr></thead>");
                    }

                    result.Append("<tbody>");
                    foreach (var child in e.Elements())
                    {
                        if (child.Name.LocalName is "item")
                        {
                            result.Append("<td>");
                            FormatNode(child);
                            result.Append("</td>");
                        }
                    }
                    return result.Append("</tbody></table>");
                }

                StringBuilder FormatList(string open, string close)
                {
                    result.Append(open);
                    foreach (var child in e.Elements())
                    {
                        if (child.Name.LocalName is "item")
                        {
                            result.Append("<li>");
                            FormatNode(child);
                            result.Append("</li>");
                        }
                    }
                    return result.Append(close);
                }

                StringBuilder FormatSee()
                {
                    var href = e.Attribute("href")?.Value;

                    if (resolveCref != null && e.Attribute("cref")?.Value is { } cref)
                        href = resolveCref(cref, e.Name.LocalName is "seealso");

                    if (e.Name.LocalName is "see" && e.Attribute("langword")?.Value is { } langword)
                        href = SymbolUrlResolver.GetLangwordUrl(langword);

                    return string.IsNullOrEmpty(href)
                        ? FormatChildNodes("<c class=\"xref\">", "</c>")
                        : FormatChildNodes("<a class=\"xref\" href=\"{HttpUtility.HtmlAttributeEncode(href)}\">", "</a>");
                }

                StringBuilder FormatNote()
                {
                    var type = e.Attribute("type")?.Value ?? "note";
                    return FormatChildNodes($"<div class=\"{type}\"><h5>{HttpUtility.HtmlEncode(type)}</h5>", "</div>");
                }

                StringBuilder FormatCode()
                {
                    if (e.Attribute("source")?.Value is { } source)
                    {
                        var lang = Path.GetExtension(source).TrimStart('.');
                        var code = ResolveCodeSource(source, e.Attribute("region")?.Value, sourcePath, codeSourceBasePath);
                        return result.Append($"<pre><code class=\"lang-{HttpUtility.HtmlAttributeEncode(lang)}\">{HttpUtility.HtmlEncode(code)}</code></pre>");
                    }
                    return FormatChildNodes("<pre><code class=\"lang-csharp\">", "</code></pre>");
                }
            }
        }
    }

    private static string? ResolveCodeSource(string source, string? region, string? sourcePath, string? codeSourceBasePath)
    {
        var sourceDirectory = sourcePath is null ? null : Path.GetDirectoryName(sourcePath);
        var path = Path.GetFullPath(Path.Combine(codeSourceBasePath ?? sourceDirectory ?? ".", source));
        if (!File.Exists(path))
        {
            Logger.LogWarning($"Source file '{path}' not found.");
            return null;
        }

        if (string.IsNullOrEmpty(region))
        {
            return File.ReadAllText(path);
        }

        var (regionRegex, endRegionRegex) = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xml" or ".xaml" or ".html" or ".cshtml" or ".vbhtml" => (XmlRegionRegex, XmlEndRegionRegex),
            _ => (RegionRegex, EndRegionRegex),
        };

        var builder = new StringBuilder();
        var regionCount = 0;
        foreach (var line in File.ReadLines(path))
        {
            var match = regionRegex.Match(line);
            if (match.Success)
            {
                var name = match.Groups[1].Value.Trim();
                if (name == region)
                {
                    ++regionCount;
                    continue;
                }
                else if (regionCount > 0)
                {
                    ++regionCount;
                }
            }
            else if (regionCount > 0 && endRegionRegex.IsMatch(line))
            {
                --regionCount;
                if (regionCount == 0)
                {
                    break;
                }
            }

            if (regionCount > 0)
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString();
    }
}
