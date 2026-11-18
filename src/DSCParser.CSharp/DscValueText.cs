using System;
using System.Text.RegularExpressions;

namespace DSCParser.CSharp
{
    internal static class DscValueText
    {
        private const string NewObject = "New-Object";

        private static readonly char[] LineBreaks = ['\r', '\n'];

        private static readonly char[] QuoteCharacters = ['"', '`', '\u201C', '\u201D', '\u201E'];

        private static readonly char[] SmartDoubleQuotes = ['\u201C', '\u201D', '\u201E'];

        private static readonly Regex ExportVariable = new(
            @"^\$(?:true|false|null|OrganizationName|Creds[A-Za-z0-9_]+|ConfigurationData\.NonNodeData\.[A-Za-z_][A-Za-z0-9_]*)\z",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Subexpression = new(
            @"\G\$\(\s*\$[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*(?:\.Split\('[^'\u2018-\u201E""`$\r\n]+'\)\[[0-9]+\])?\s*\)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ExportReference = new(
            @"\G\$(?:OrganizationName(?![\p{L}\p{Nd}_?:])|\(\$OrganizationName(?:\.Split\('\.'\)\[[0-9]+\])?\)|\(\$ConfigurationData\.NonNodeData\.[A-Za-z_][A-Za-z0-9_]*\))",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Identifier = new(
            @"^[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private enum Interpolation
        {
            Live,
            Inert,
            Executable
        }

        internal static bool IsIdentifier(string text) => Identifier.IsMatch(text);

        internal static bool HasLineBreak(string text) => text.IndexOfAny(LineBreaks) >= 0;

        internal static bool IsLiteralName(string name) =>
            !HasLineBreak(name) && (name.IndexOfAny(QuoteCharacters) >= 0 || IsLiteralText(name));

        internal static bool IsLiteralValue(string value) => IsLiteralText(value) && !ExportVariable.IsMatch(value);

        // A plain variable, $OrganizationName and a Microsoft365DSC subexpression stay live inside double quotes.
        private static bool IsLiteralText(string text) => !IsInertText(text) || !HasOnlyExportReferences(text);

        private static bool IsInertText(string value) =>
            HasOnlyInertInterpolation(value)
            && value.IndexOfAny(SmartDoubleQuotes) < 0
            && !value.StartsWith(NewObject, StringComparison.OrdinalIgnoreCase);

        private static bool HasOnlyInertInterpolation(string value)
        {
            for (int at = value.IndexOf('$'); at >= 0;)
            {
                if (ReadInterpolation(value, at, out int length) == Interpolation.Executable)
                {
                    return false;
                }

                at = value.IndexOf('$', at + length);
            }

            return true;
        }

        private static bool HasOnlyExportReferences(string value)
        {
            for (int at = value.IndexOf('$'); at >= 0;)
            {
                Match reference = ExportReference.Match(value, at);
                if (!reference.Success)
                {
                    return false;
                }

                at = value.IndexOf('$', at + reference.Length);
            }

            return true;
        }

        private static Interpolation ReadInterpolation(string value, int at, out int length)
        {
            length = 1;

            if (at + 1 == value.Length)
            {
                return Interpolation.Inert;
            }

            char first = value[at + 1];

            if (first == '(')
            {
                Match subexpression = Subexpression.Match(value, at);
                length = subexpression.Success ? subexpression.Length : 2;

                return subexpression.Success ? Interpolation.Live : Interpolation.Executable;
            }

            if (first is '{' or ':')
            {
                return Interpolation.Executable;
            }

            if (first is '$' or '?' or '^')
            {
                length = 2;
                return Interpolation.Inert;
            }

            if (!IsNameCharacter(first))
            {
                return Interpolation.Inert;
            }

            int end = at + 2;
            while (end < value.Length && IsNameCharacter(value[end]))
            {
                end++;
            }

            length = end - at;

            if (end < value.Length && value[end] == ':' && (end + 1 == value.Length || value[end + 1] != ':'))
            {
                return Interpolation.Executable;
            }

            return char.IsLetter(first) || first == '_' ? Interpolation.Live : Interpolation.Inert;
        }

        private static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character is '_' or '?';
    }
}
