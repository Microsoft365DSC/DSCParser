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
        /// <summary>
        /// Gets or sets the DSC resource type name
        /// </summary>
        public string ResourceName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name given to this instance in the configuration
        /// </summary>
        public string ResourceInstanceName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the parsed properties of this instance
        /// </summary>
        public Dictionary<string, object?> Properties { get; set; } = [];

        /// <summary>
        /// Null when the instance was not read from configuration text
        /// </summary>
        public DscResourceExtent? Extent { get; set; }

        /// <summary>
        /// Adds or updates a property value
        /// </summary>
        public void AddProperty(string key, object? value) => Properties[key] = value;

        /// <summary>
        /// Gets a property value
        /// </summary>
        public object? GetProperty(string key) => Properties.TryGetValue(key, out object? value) ? value : null;

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
        /// <summary>
        /// Gets or sets whether comments are captured as _metadata_ properties
        /// </summary>
        public bool IncludeComments { get; set; } = false;

        /// <summary>
        /// Gets or sets whether CIM instance type names are emitted as CIMInstance keys
        /// </summary>
        public bool IncludeCIMInstanceInfo { get; set; } = true;

        /// <summary>
        /// Gets or sets an optional schema definition
        /// </summary>
        public string? Schema { get; set; }

        /// <summary>
        /// Gets or sets whether to parse against keywords already registered with
        /// DscKeywordRegistry.RegisterFromSchemaCache instead of resolving the configuration's
        /// modules.
        /// </summary>
        /// <remarks>
        /// Set this in a host that has no PowerShell modules on disk and no usable runspace. The
        /// configuration's Import-DscResource statements are then ignored rather than honoured, so
        /// what the caller registered is the complete set of resources the parse can resolve.
        /// </remarks>
        public bool UseRegisteredKeywords { get; set; } = false;
    }
}
