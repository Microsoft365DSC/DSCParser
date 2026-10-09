using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace DSCParser.PSDSC
{
    /// <summary>
    /// Builds <see cref="DynamicKeyword"/> definitions from the entries of a serialized DSC schema
    /// cache, so a host that has neither the module on disk nor a usable runspace can still register
    /// a module's resources.
    /// </summary>
    internal static class DscSchemaCacheKeywords
    {
        public const string MissingMandatoryPropertyErrorId = "MissingValueForMandatoryProperty";

        public static DynamicKeyword Build(object entry)
        {
            if (entry is null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            string? keywordName = GetString(entry, "keyword");
            if (string.IsNullOrEmpty(keywordName))
            {
                throw new InvalidOperationException("A schema cache entry has no keyword name.");
            }

            DynamicKeyword keyword = new()
            {
                Keyword = keywordName,
                ResourceName = GetString(entry, "resourceName"),
                ImplementingModule = GetString(entry, "implementingModule"),
                NameMode = ParseEnum(GetString(entry, "nameMode"), DynamicKeywordNameMode.NoName),
                BodyMode = ParseEnum(GetString(entry, "bodyMode"), DynamicKeywordBodyMode.Hashtable),
                DirectCall = GetBool(entry, "directCall"),
                MetaStatement = GetBool(entry, "metaStatement")
            };

            string? version = GetString(entry, "implementingModuleVersion");
            if (!string.IsNullOrEmpty(version) && Version.TryParse(version, out Version parsedVersion))
            {
                keyword.ImplementingModuleVersion = parsedVersion;
            }

            foreach (KeyValuePair<string, object> property in GetMap(GetValue(entry, "properties")))
            {
                keyword.Properties.Add(property.Key, BuildProperty(property.Value));
            }

            List<string> mandatoryProperties = keyword.Properties
                .Where(property => property.Value.Mandatory)
                .Select(property => property.Key)
                .ToList();
            if (mandatoryProperties.Count > 0)
            {
                keyword.SemanticCheck = statement => CheckMandatoryProperties(statement, mandatoryProperties);
            }

            return keyword;
        }

        private static DynamicKeywordProperty BuildProperty(object source)
        {
            DynamicKeywordProperty property = new()
            {
                Name = GetString(source, "name"),
                TypeConstraint = GetString(source, "typeConstraint"),
                Mandatory = GetBool(source, "mandatory"),
                IsKey = GetBool(source, "isKey")
            };

            foreach (object attribute in GetSequence(GetValue(source, "attributes")))
            {
                if (attribute is not null)
                {
                    property.Attributes.Add(Stringify(attribute));
                }
            }

            foreach (object value in GetSequence(GetValue(source, "values")))
            {
                if (value is not null)
                {
                    property.Values.Add(Stringify(value));
                }
            }

            foreach (object pair in GetSequence(GetValue(source, "valueMap")))
            {
                string? key = GetString(pair, "key");
                if (key is not null)
                {
                    property.ValueMap[key] = GetString(pair, "value");
                }
            }

            return property;
        }

        private static ParseError[]? CheckMandatoryProperties(DynamicKeywordStatementAst statement, List<string> mandatoryProperties)
        {
            if (statement.CommandElements.OfType<HashtableAst>().FirstOrDefault() is not HashtableAst body)
            {
                return null;
            }

            HashSet<string> present = new(
                body.KeyValuePairs.Select(pair => pair.Item1).OfType<StringConstantExpressionAst>().Select(key => key.Value),
                StringComparer.OrdinalIgnoreCase);

            ParseError[] errors = mandatoryProperties
                .Where(name => !present.Contains(name))
                .Select(name => new ParseError(
                    statement.CommandElements[0].Extent,
                    MissingMandatoryPropertyErrorId,
                    $"Resource '{statement.CommandElements[0].Extent.Text}' requires a value for the mandatory property '{name}'."))
                .ToArray();

            return errors.Length == 0 ? null : errors;
        }

        private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
            where TEnum : struct
        {
            return Enum.TryParse(value, ignoreCase: true, out TEnum parsed) ? parsed : fallback;
        }

        private static object? GetValue(object? source, string name)
        {
            switch (Unwrap(source))
            {
                case IDictionary dictionary:
                    return dictionary.Contains(name) ? Unwrap(dictionary[name]) : null;

                case PSObject psObject:
                    return Unwrap(psObject.Properties[name]?.Value);

                default:
                    return null;
            }
        }

        private static string? GetString(object? source, string name)
        {
            return GetValue(source, name) is { } value ? Stringify(value) : null;
        }

        private static bool GetBool(object? source, string name)
        {
            object? value = GetValue(source, name);
            return value is not null && LanguagePrimitives.IsTrue(value);
        }

        private static IEnumerable<KeyValuePair<string, object>> GetMap(object? source)
        {
            switch (Unwrap(source))
            {
                case IDictionary dictionary:
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (Unwrap(entry.Value) is { } value)
                        {
                            yield return new KeyValuePair<string, object>(Stringify(entry.Key), value);
                        }
                    }

                    break;

                case PSObject psObject:
                    foreach (PSPropertyInfo property in psObject.Properties)
                    {
                        if (Unwrap(property.Value) is { } value)
                        {
                            yield return new KeyValuePair<string, object>(property.Name, value);
                        }
                    }

                    break;
            }
        }

        private static IEnumerable<object> GetSequence(object? source)
        {
            if (Unwrap(source) is IEnumerable sequence and not string)
            {
                foreach (object item in sequence)
                {
                    yield return Unwrap(item)!;
                }
            }
        }

        private static string Stringify(object value)
        {
            return value as string ?? LanguagePrimitives.ConvertTo<string>(value);
        }

        private static object? Unwrap(object? value)
        {
            return value is PSObject psObject && psObject.BaseObject is not PSCustomObject
                ? psObject.BaseObject
                : value;
        }
    }
}
