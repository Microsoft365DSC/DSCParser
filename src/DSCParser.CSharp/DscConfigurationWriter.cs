using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation.Language;
using System.Text;
using System.Text.RegularExpressions;

namespace DSCParser.CSharp
{
    internal sealed class DscConfigurationWriter
    {
        private const string MetadataPrefix = "_metadata_";

        private const string ResourceNameKey = "ResourceName";

        private const string InstanceNameKey = "ResourceInstanceName";

        private const string CimTypeKey = "CIMInstance";

        // Matches a string that is only a variable reference, scoped or with member access
        // like $name, $scope:name, $config.Credential. Anything else that starts with '$'
        // (e.g. "$OrganizationName\Default") must be written quoted or the generated text
        // does not reparse.
        private static readonly Regex BareVariableReferenceRegex = new(
            @"^\$(?:\w+:)?\w+(?:\.\w+)*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly char[] QuoteEscapeCharacters = ['`', '"'];

        private readonly StringBuilder _text = new();

        private readonly string _newLine;

        private readonly bool _isLiteralOnly;

        private readonly bool _writesNestedResourceName;

        internal DscConfigurationWriter(string newLine, bool isLiteralOnly, bool writesNestedResourceName)
        {
            _newLine = newLine;
            _isLiteralOnly = isLiteralOnly;
            _writesNestedResourceName = writesNestedResourceName;
        }

        internal string Write(IEnumerable<IDictionary> resources, int childLevel)
        {
            AppendDscObjects(resources, childLevel);
            return _text.ToString();
        }

        internal static DscWriteError? FindLiteralError(IDictionary resource)
        {
            string? type = ReadText(resource, ResourceNameKey);
            string? instance = ReadText(resource, InstanceNameKey);

            if (type is null || !DscValueText.IsIdentifier(type) || instance is null || DscValueText.HasLineBreak(instance))
            {
                return new DscWriteError(type, instance, null);
            }

            // A setting named CIMInstance would be written as the block header.
            string? setting = resource.Contains(CimTypeKey)
                ? CimTypeKey
                : FindUnwritableSetting(resource, path: null, isCimInstance: false);

            return setting is null ? null : new DscWriteError(type, instance, setting);
        }

        private static string? FindUnwritableSetting(IDictionary instance, string? path, bool isCimInstance)
        {
            foreach (DictionaryEntry entry in instance)
            {
                string key = entry.Key as string ?? string.Empty;

                if (isCimInstance ? key == CimTypeKey : key is ResourceNameKey or InstanceNameKey)
                {
                    continue;
                }

                if (key.StartsWith(MetadataPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string setting = path is null ? key : $"{path}.{key}";

                if (!DscValueText.IsIdentifier(key))
                {
                    return setting;
                }

                if (FindUnwritableValue(entry.Value, setting) is { } unwritable)
                {
                    return unwritable;
                }
            }

            return null;
        }

        private static string? FindUnwritableValue(object? value, string setting)
        {
            switch (value)
            {
                case string:
                    return null;

                case IDictionary nested:
                    return ReadText(nested, CimTypeKey) is { } cimType && DscValueText.IsIdentifier(cimType)
                        ? FindUnwritableSetting(nested, setting, isCimInstance: true)
                        : setting;

                case IEnumerable items:
                    foreach (object? item in items)
                    {
                        if (FindUnwritableValue(item, setting) is { } unwritable)
                        {
                            return unwritable;
                        }
                    }

                    return null;

                default:
                    return value is null or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                        ? null
                        : setting;
            }
        }

        private static string? ReadText(IDictionary dictionary, string key) =>
            dictionary.Contains(key) ? dictionary[key] as string : null;

        /// <summary>
        /// Nested hashtables and arrays recurse into the same builder. No intermediate strings are
        /// produced per nesting level.
        /// </summary>
        private void AppendDscObjects(IEnumerable<IDictionary> dscResources, int childLevel)
        {
            string childSpacer = new(' ', childLevel * 4);

            foreach (IDictionary entry in dscResources)
            {
                List<string> sortedKeys = [];
                int longestParameter = 0;
                foreach (string key in entry.Keys)
                {
                    if (key.StartsWith(MetadataPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    sortedKeys.Add(key);
                    if (key.Length > longestParameter)
                    {
                        longestParameter = key.Length;
                    }
                }
                sortedKeys.Sort(StringComparer.Ordinal);

                if (entry.Contains(CimTypeKey))
                {
                    _ = _text.Append(childSpacer).Append(entry[CimTypeKey]).Append('{').Append(_newLine);
                }
                else if (entry.Contains(ResourceNameKey) && entry.Contains(InstanceNameKey))
                {
                    _ = _text.Append(childSpacer).Append(entry[ResourceNameKey]).Append(' ');
                    AppendInstanceName(entry[InstanceNameKey]);
                    _ = _text.Append(_newLine).Append(childSpacer).Append('{').Append(_newLine);
                }
                else
                {
                    _ = _text.Append(childSpacer).Append("@{").Append(_newLine);
                }

                foreach (string property in sortedKeys)
                {
                    if (property is InstanceNameKey or CimTypeKey ||
                        (property is ResourceNameKey && (childLevel == 0 || !_writesNestedResourceName)))
                    {
                        continue;
                    }

                    AppendProperty(property, entry[property], longestParameter - property.Length + 1, childSpacer, childLevel);
                }

                _ = _text.Append(childSpacer).Append('}').Append(_newLine);
            }
        }

        private void AppendInstanceName(object? name)
        {
            if (_isLiteralOnly && name is string text && DscValueText.IsLiteralName(text))
            {
                AppendSingleQuoted(text);
                return;
            }

            _ = _text.Append('"').Append(name).Append('"');
        }

        private void AppendProperty(string property, object? value, int additionalSpaces, string childSpacer, int childLevel)
        {
            switch (value)
            {
                case string strValue:
                    AppendPropertyPrefix(property, additionalSpaces, childSpacer);
                    if (_isLiteralOnly && DscValueText.IsLiteralValue(strValue))
                    {
                        AppendSingleQuoted(strValue);
                    }
                    // Only a string that is entirely a variable reference is written bare
                    else if (strValue.Length > 1 && strValue[0] == '$' && BareVariableReferenceRegex.IsMatch(strValue))
                    {
                        _ = _text.Append(strValue);
                    }
                    else if (strValue.StartsWith("New-Object", StringComparison.Ordinal))
                    {
                        _ = _text.Append(strValue.TrimStart('"').TrimEnd('"'));
                    }
                    else
                    {
                        AppendQuoted(strValue);
                    }
                    _ = _text.Append(_newLine);
                    break;

                case int intValue:
                    AppendPropertyPrefix(property, additionalSpaces, childSpacer);
                    _ = _text.Append(intValue).Append(_newLine);
                    break;

                case bool boolValue:
                    AppendPropertyPrefix(property, additionalSpaces, childSpacer);
                    _ = _text.Append('$').Append(boolValue).Append(_newLine);
                    break;

                // Covers Hashtable and the Dictionary instances the parser produces for CIM instances
                case IDictionary dictionary:
                    AppendPropertyPrefix(property, additionalSpaces, childSpacer);
                    int contentStart = _text.Length;
                    AppendDscObjects([dictionary], childLevel + 1);
                    StripIndentOfOpeningLine(contentStart);
                    break;

                case IEnumerable sequence:
                    AppendPropertyPrefix(property, additionalSpaces, childSpacer);
                    AppendArray(sequence, childSpacer, childLevel);
                    break;

                default:
                    if (value != null)
                    {
                        AppendPropertyPrefix(property, additionalSpaces, childSpacer);
                        _ = _text.Append(value).Append(_newLine);
                    }
                    break;
            }
        }

        private void AppendPropertyPrefix(string property, int additionalSpaces, string childSpacer)
        {
            _ = _text.Append(childSpacer).Append("    ").Append(property).Append(' ', additionalSpaces).Append("= ");
        }

        private void AppendSingleQuoted(string value)
        {
            _ = _text.Append('\'').Append(CodeGeneration.EscapeSingleQuotedStringContent(value)).Append('\'');
        }

        private void AppendQuoted(string value)
        {
            if (value.IndexOfAny(QuoteEscapeCharacters) < 0)
            {
                _ = _text.Append('"').Append(value).Append('"');
                return;
            }

            _ = _text.Append('"');
            foreach (char character in value)
            {
                if (character is '`' or '"')
                {
                    _ = _text.Append('`');
                }
                _ = _text.Append(character);
            }
            _ = _text.Append('"');
        }

        private void AppendArray(IEnumerable sequence, string childSpacer, int childLevel)
        {
            _ = _text.Append("@(");

            List<object?> items = [];
            bool isSimpleArray = true;
            foreach (object? item in sequence)
            {
                items.Add(item);
                if (item is IDictionary)
                {
                    isSimpleArray = false;
                }
            }

            if (items.Count == 0)
            {
                _ = _text.Append(')').Append(_newLine);
                return;
            }

            if (isSimpleArray && items.Count == 1)
            {
                AppendArrayItem(items[0]);
                _ = _text.Append(')').Append(_newLine);
                return;
            }

            _ = _text.Append(_newLine);
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    _ = _text.Append(_newLine);
                }

                if (items[i] is IDictionary nested)
                {
                    AppendDscObjects([nested], childLevel + 2);
                    _text.Length -= _newLine.Length;
                }
                else
                {
                    _ = _text.Append(' ', childSpacer.Length + 8);
                    AppendArrayItem(items[i]);
                }
            }
            _ = _text.Append(_newLine);

            _ = _text.Append(childSpacer).Append("    )").Append(_newLine);
        }

        private void AppendArrayItem(object? item)
        {
            if (item is not string text)
            {
                _ = _text.Append(item);
            }
            else if (_isLiteralOnly && DscValueText.IsLiteralValue(text))
            {
                AppendSingleQuoted(text);
            }
            else
            {
                AppendQuoted(text);
            }
        }

        /// <summary>
        /// Removes the leading indentation of the first rendered line when that line opens a block.
        /// Nested objects start directly after the "= " of their property assignment.
        /// </summary>
        private void StripIndentOfOpeningLine(int contentStart)
        {
            int lineEnd = contentStart;
            while (lineEnd < _text.Length && _text[lineEnd] is not '\r' and not '\n')
            {
                lineEnd++;
            }

            int indent = contentStart;
            while (indent < lineEnd && _text[indent] == ' ')
            {
                indent++;
            }

            if (indent > contentStart && lineEnd > contentStart && _text[lineEnd - 1] == '{')
            {
                _ = _text.Remove(contentStart, indent - contentStart);
            }
        }
    }
}
