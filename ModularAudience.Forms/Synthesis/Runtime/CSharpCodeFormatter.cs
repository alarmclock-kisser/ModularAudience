using System;
using System.Text;

namespace ModularAudience.Forms.Synthesis.Runtime;

/// <summary>
/// Tolerant, display-only formatter for LLM-generated C# source.
/// Some GGUF models emit the correct spaces/tabs for indentation but collapse the line
/// breaks after semicolons, so the editor shows one long "flowing text" line. This helper
/// inserts exactly those missing newlines and keeps every whitespace character the model
/// emitted (no re-indentation). Strings, chars, comments and for-loop headers are left
/// untouched; already formatted code is returned unchanged.
/// </summary>
internal static class CSharpCodeFormatter
{
    public static string SmartFormat(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;

        string text = code.Replace("\r\n", "\n").Replace('\r', '\n');
        if (!NeedsReflow(text)) return text;

        return InsertSemicolonNewlines(text);
    }

    /// <summary>Only reflow when semicolons clearly outnumber existing line breaks.</summary>
    private static bool NeedsReflow(string text)
    {
        int newlines = 0;
        int semicolons = 0;
        foreach (char c in text)
        {
            if (c == '\n') newlines++;
            else if (c == ';') semicolons++;
        }

        return semicolons >= 6 && newlines * 2 < semicolons;
    }

    private enum ScanState { Code, String, VerbatimString, Char, LineComment, BlockComment }

    /// <summary>
    /// True when a newline follows after optional horizontal whitespace. Used to avoid
    /// doubling line breaks the model already provided.
    /// </summary>
    private static bool HasLineBreakSoon(string text, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n' || c == '\r') return true;
            if (c != ' ' && c != '\t') return false;
        }
        return false;
    }

    private static string InsertSemicolonNewlines(string text)
    {
        var result = new StringBuilder(text.Length + 64);
        ScanState state = ScanState.Code;
        int parenDepth = 0;
        int bracketDepth = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';

            switch (state)
            {
                case ScanState.Code:
                    if (c == '"')
                    {
                        state = i > 0 && text[i - 1] == '@' ? ScanState.VerbatimString : ScanState.String;
                        result.Append(c);
                    }
                    else if (c == '\'')
                    {
                        state = ScanState.Char;
                        result.Append(c);
                    }
                    else if (c == '/' && next == '/')
                    {
                        result.Append(c).Append(text[++i]);
                        state = ScanState.LineComment;
                    }
                    else if (c == '/' && next == '*')
                    {
                        result.Append(c).Append(text[++i]);
                        state = ScanState.BlockComment;
                    }
                    else if (c == '(')
                    {
                        parenDepth++;
                        result.Append(c);
                    }
                    else if (c == ')')
                    {
                        parenDepth = Math.Max(0, parenDepth - 1);
                        result.Append(c);
                    }
                    else if (c == '[')
                    {
                        bracketDepth++;
                        result.Append(c);
                    }
                    else if (c == ']')
                    {
                        bracketDepth = Math.Max(0, bracketDepth - 1);
                        result.Append(c);
                    }
                    else if (c == ';' && parenDepth == 0 && bracketDepth == 0)
                    {
                        // Insert only a genuinely missing line break. Any spaces/tabs the
                        // model already emitted stay untouched and become the next line's
                        // indentation.
                        result.Append(';');
                        if (!HasLineBreakSoon(text, i + 1)) result.Append('\n');
                    }
                    else
                    {
                        result.Append(c);
                    }
                    break;

                case ScanState.String:
                    result.Append(c);
                    if (c == '\\' && i + 1 < text.Length) result.Append(text[++i]);
                    else if (c == '"') state = ScanState.Code;
                    break;

                case ScanState.VerbatimString:
                    result.Append(c);
                    if (c == '"')
                    {
                        if (next == '"') result.Append(text[++i]);
                        else state = ScanState.Code;
                    }
                    break;

                case ScanState.Char:
                    result.Append(c);
                    if (c == '\\' && i + 1 < text.Length) result.Append(text[++i]);
                    else if (c == '\'' || c == '\n') state = ScanState.Code;
                    break;

                case ScanState.LineComment:
                    result.Append(c);
                    if (c == '\n') state = ScanState.Code;
                    break;

                case ScanState.BlockComment:
                    result.Append(c);
                    if (c == '*' && next == '/')
                    {
                        result.Append(text[++i]);
                        state = ScanState.Code;
                    }
                    break;
            }
        }

        return result.ToString();
    }
}
