using Microsoft.PowerShell.DesiredStateConfiguration.V2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;

namespace DSCParser.PSDSC
{
    /// <summary>
    /// Helper methods for DSC resource operations
    /// </summary>
    internal static partial class DscResourceHelpers
    {
        private static readonly Regex SchemaMofRegex = new(@"\.schema\.mof$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Hidden resources that should not be returned to users
        private static readonly HashSet<string> HiddenResources = new(StringComparer.OrdinalIgnoreCase)
        {
            "OMI_BaseResource",
            "MSFT_KeyValuePair",
            "MSFT_BaseConfigurationProviderRegistration",
            "MSFT_CimConfigurationProviderRegistration",
            "MSFT_PSConfigurationProviderRegistration",
            "OMI_ConfigurationDocument",
            "MSFT_Credential",
            "MSFT_DSCMetaConfiguration",
            "OMI_ConfigurationDownloadManager",
            "OMI_ResourceModuleManager",
            "OMI_ReportManager",
            "MSFT_FileDownloadManager",
            "MSFT_WebDownloadManager",
            "MSFT_FileResourceManager",
            "MSFT_WebResourceManager",
            "MSFT_WebReportManager",
            "OMI_MetaConfigurationResource",
            "MSFT_PartialConfiguration",
            "MSFT_DSCMetaConfigurationV2"
        };

        // Type conversion map for MOF types to PowerShell types
        private static readonly Dictionary<string, string> ConvertTypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "MSFT_Credential", "[PSCredential]" },
            { "MSFT_KeyValuePair", "[HashTable]" },
            { "MSFT_KeyValuePair[]", "[HashTable]" }
        };

        /// <summary>
        /// Checks whether a resource is hidden and should not be shown to users
        /// </summary>
        public static bool IsHiddenResource(string resourceName) => HiddenResources.Contains(resourceName);

        /// <summary>
        /// Checks whether an input name matches one of the patterns
        /// </summary>
        public static bool IsPatternMatched(string[] patterns, string name)
        {
            if (patterns is null || patterns.Length == 0)
            {
                return true;
            }

            foreach (var pattern in patterns)
            {
                if (Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets implementing module path from schema file path
        /// </summary>
        public static string? GetImplementingModulePath(string schemaFileName)
        {
            if (string.IsNullOrEmpty(schemaFileName))
            {
                return null;
            }

            // Try .psd1 first
            var moduleFileName = SchemaMofRegex.Replace(schemaFileName, "") + ".psd1";
            if (File.Exists(moduleFileName))
            {
                return moduleFileName;
            }

            // Try .psm1
            moduleFileName = SchemaMofRegex.Replace(schemaFileName, "") + ".psm1";
            return File.Exists(moduleFileName)
                ? moduleFileName
                : null;
        }

        /// <summary>
        /// Gets module for a DSC resource from schema file
        /// </summary>
        public static PSModuleInfo? GetModule(PSModuleInfo[] modules, string? schemaFileName)
        {
            if (string.IsNullOrEmpty(schemaFileName) || modules is null || modules.Length == 0)
            {
                return null;
            }

            string? schemaFileExt = null;
            if (schemaFileName!.Contains(".schema.mof", StringComparison.OrdinalIgnoreCase))
            {
                schemaFileExt = ".schema.mof";
            }
            else if (schemaFileName!.Contains(".schema.psm1", StringComparison.OrdinalIgnoreCase))
            {
                schemaFileExt = ".schema.psm1";
            }

            if (schemaFileExt is null)
            {
                return null;
            }

            // Get module from parent directory
            // Desired structure is: <Module-directory>/DscResources/<schema file directory>/schema.File
            try
            {
                var schemaDirectory = Path.GetDirectoryName(schemaFileName);
                if (string.IsNullOrEmpty(schemaDirectory))
                {
                    return null;
                }

                var subDirectory = Directory.GetParent(schemaDirectory);
                if (subDirectory is null ||
                    !subDirectory.Name.Equals("DscResources", StringComparison.OrdinalIgnoreCase) ||
                    subDirectory.Parent is null)
                {
                    return null;
                }

                var moduleBase = subDirectory.Parent.FullName;
                var result = modules.FirstOrDefault(m =>
                    m.ModuleBase is not null &&
                    m.ModuleBase.Equals(moduleBase, StringComparison.OrdinalIgnoreCase));

                if (result is not null)
                {
                    // Validate it's a proper resource module
                    var validResource = ValidateResourceModule(schemaFileName!, schemaFileExt);
                    if (validResource)
                    {
                        return result;
                    }
                }
            }
            catch
            {
                // Return null on any error
            }

            return null;
        }

        /// <summary>
        /// Validates that a schema file corresponds to a proper DSC resource module
        /// </summary>
        private static bool ValidateResourceModule(string schemaFileName, string schemaFileExt)
        {
            // Log Resource is internally handled - special case
            if (schemaFileName.Contains("MSFT_LogResource", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check for proper resource module files
            var extensions = new[] { ".psd1", ".psm1", ".dll", ".cdxml" };
            foreach (var ext in extensions)
            {
                var resModuleFileName = Regex.Replace(schemaFileName, schemaFileExt + "$", "", RegexOptions.IgnoreCase) + ext;
                if (File.Exists(resModuleFileName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets DSC resource modules from PSModulePath
        /// </summary>
        public static HashSet<string> GetDscResourceModules()
        {
            var dscModuleFolderList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var psModulePath = Environment.GetEnvironmentVariable("PSModulePath");

            if (string.IsNullOrEmpty(psModulePath))
            {
                return dscModuleFolderList;
            }

            var listPSModuleFolders = psModulePath.Split([Path.PathSeparator], StringSplitOptions.RemoveEmptyEntries);

            foreach (var folder in listPSModuleFolders)
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                try
                {
                    foreach (var moduleFolder in Directory.GetDirectories(folder))
                    {
                        var addModule = false;
                        var moduleName = Path.GetFileName(moduleFolder);

                        // Check for DscResources folder
                        var dscResourcesPath = Path.Combine(moduleFolder, "DscResources");
                        if (Directory.Exists(dscResourcesPath))
                        {
                            addModule = true;
                        }
                        else
                        {
                            // Check for nested DscResources folders (one level deep)
                            foreach (var subFolder in Directory.GetDirectories(moduleFolder))
                            {
                                var nestedDscPath = Path.Combine(subFolder, "DscResources");
                                if (Directory.Exists(nestedDscPath))
                                {
                                    addModule = true;
                                    break;
                                }
                            }
                        }

                        // Check .psd1 files for DscResourcesToExport
                        if (!addModule)
                        {
                            var psd1Pattern = $"{moduleName}.psd1";
                            var psd1Files = Directory.GetFiles(moduleFolder, psd1Pattern, SearchOption.TopDirectoryOnly);

                            if (psd1Files.Length == 0)
                            {
                                // Check one level deep
                                foreach (var subFolder in Directory.GetDirectories(moduleFolder))
                                {
                                    psd1Files = Directory.GetFiles(subFolder, psd1Pattern, SearchOption.TopDirectoryOnly);
                                    if (psd1Files.Length > 0) break;
                                }
                            }

                            foreach (var psd1File in psd1Files)
                            {
                                try
                                {
                                    var content = File.ReadAllText(psd1File);
                                    if (Regex.IsMatch(content, @"^\s*DscResourcesToExport\s*=", RegexOptions.Multiline))
                                    {
                                        addModule = true;
                                        break;
                                    }
                                }
                                catch
                                {
                                    // Ignore file read errors
                                }
                            }
                        }

                        if (addModule)
                        {
                            dscModuleFolderList.Add(moduleName);
                        }
                    }
                }
                catch
                {
                    // Ignore directory access errors
                }
            }

            return dscModuleFolderList;
        }

        /// <summary>
        /// Converts MOF type constraint to PowerShell type name
        /// </summary>
        public static string ConvertTypeConstraintToTypeName(string typeConstraint, string[] dscResourceNames)
        {
            if (ConvertTypeMap.TryGetValue(typeConstraint, out var mappedType))
            {
                return mappedType;
            }

            // Try to convert using PowerShell type conversion
            var type = ConvertCimTypeNameToPSTypeName(typeConstraint);

            if (!string.IsNullOrEmpty(type))
            {
                return type;
            }

            // Check if it's a DSC resource type
            foreach (var resourceName in dscResourceNames)
            {
                if (typeConstraint.Equals(resourceName, StringComparison.OrdinalIgnoreCase) ||
                    typeConstraint.Equals(resourceName + "[]", StringComparison.OrdinalIgnoreCase))
                {
                    return $"[{typeConstraint}]";
                }
            }

            return $"[{typeConstraint}]";
        }

        /// <summary>
        /// Converts CIM type name to PowerShell type name
        /// </summary>
        private static string ConvertCimTypeNameToPSTypeName(string cimTypeName)
        {
            Dictionary<string, string> convertTypeMap = new()
            {
                ["MSFT_Credential"] = "[PSCredential]",
                ["MSFT_KeyValuePair"] = "[HashTable]",
                ["MSFT_KeyValuePair[]"] = "[HashTable]"
            };

            return convertTypeMap.TryGetValue(cimTypeName, out var mappedType)
                ? mappedType
                : LanguagePrimitives.ConvertTypeNameToPSTypeName(cimTypeName);
        }

        /// <summary>
        /// Generates syntax string for a DSC resource
        /// </summary>
        public static string GetSyntax(DscResourceInfo resource)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{resource.Name} [String] #ResourceName");
            sb.AppendLine("{");

            foreach (var property in resource.PropertiesAsResourceInfo)
            {
                sb.Append("    ");

                if (!property.IsMandatory)
                {
                    sb.Append('[');
                }

                sb.Append(property.Name);
                sb.Append(" = ");
                sb.Append(property.PropertyType);

                // Add possible values
                if (property.Values.Count > 0)
                {
                    sb.Append("{ ");
                    sb.Append(string.Join(" | ", property.Values));
                    sb.Append(" }");
                }

                if (!property.IsMandatory)
                {
                    sb.Append(']');
                }

                sb.AppendLine();
            }

            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
