using System.Collections;

namespace DSCParser.Tests;

/// <summary>
/// One keyword entry of the shape ConvertTo-DscKeywordSchemaObject writes into a schema cache.
/// </summary>
internal static class SchemaCacheEntry
{
    /// <param name="properties">The type constraint first, then the allowed values.</param>
    internal static Hashtable Build(
        string keyword, string nameMode, Dictionary<string, string[]> properties, params string[] mandatory)
    {
        Hashtable propertyMap = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string name, string[] typeAndValues) in properties)
        {
            propertyMap[name] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = name,
                ["typeConstraint"] = typeAndValues[0],
                ["mandatory"] = mandatory.Contains(name, StringComparer.OrdinalIgnoreCase),
                ["isKey"] = false,
                ["attributes"] = Array.Empty<object>(),
                ["values"] = typeAndValues.Skip(1).Cast<object>().ToArray(),
                ["valueMap"] = typeAndValues.Skip(1)
                    .Select(value => new Hashtable(StringComparer.OrdinalIgnoreCase) { ["key"] = value, ["value"] = value })
                    .Cast<object>()
                    .ToArray(),
            };
        }

        return new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["keyword"] = keyword,
            ["resourceName"] = keyword,
            ["implementingModule"] = "Contoso",
            ["implementingModuleVersion"] = "2.0.0",
            ["nameMode"] = nameMode,
            ["bodyMode"] = "Hashtable",
            ["directCall"] = false,
            ["metaStatement"] = false,
            ["properties"] = propertyMap,
        };
    }
}
