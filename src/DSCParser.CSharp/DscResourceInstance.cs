using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DSCParser.CSharp
{
    /// <summary>
    /// Represents a parsed DSC resource instance
    /// </summary>
    public class DscResourceInstance
    {
        public string ResourceName { get; set; } = string.Empty;
        public string ResourceInstanceName { get; set; } = string.Empty;
        public Dictionary<string, object?> Properties { get; set; } = [];

        /// <summary>
        /// Adds or updates a property value
        /// </summary>
        public void AddProperty(string key, object? value) => Properties[key] = value;

        /// <summary>
        /// Gets a property value
        /// </summary>
        public object? GetProperty(string key) => Properties.ContainsKey(key) ? Properties[key] : null;

        /// <summary>
        /// Converts to Hashtable for PowerShell compatibility
        /// </summary>
        public Hashtable ToHashtable()
        {
            Hashtable result = new(StringComparer.OrdinalIgnoreCase)
            {
                ["ResourceName"] = ResourceName,
                ["ResourceInstanceName"] = ResourceInstanceName
            };

            foreach (KeyValuePair<string, object?> kvp in Properties)
            {
                result[kvp.Key] = ConvertToHashtableRecursive(kvp.Value);
            }

            return result;
        }

        private static object? ConvertToHashtableRecursive(object? value)
        {
            if (value == null) return null;

            if (value is DscResourceInstance dscInstance)
            {
                return dscInstance.ToHashtable();
            }

            if (value is Dictionary<string, object?> dict)
            {
                Hashtable ht = new(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, object?> kvp in dict)
                {
                    ht[kvp.Key] = ConvertToHashtableRecursive(kvp.Value);
                }
                return ht;
            }

            return value is IEnumerable<object> enumerable && value is not string
                ? enumerable.Select(ConvertToHashtableRecursive).ToArray()
                : value;
        }
    }

    /// <summary>
    /// Options for DSC parsing
    /// </summary>
    public class DscParseOptions
    {
        public bool IncludeComments { get; set; } = false;
        public bool IncludeCIMInstanceInfo { get; set; } = true;
        public string? Schema { get; set; }
    }
}
