using Microsoft.Management.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;

namespace DSCParser.PSDSC
{
    /// <summary>
    /// Static entry point for DSC resource discovery that can be called from C# code.
    /// Provides programmatic access to DSC resources without requiring PowerShell cmdlet invocation.
    /// </summary>
    public static class DscResourceService
    {
        // Parameters to ignore for composite resources
        private static readonly string[] IgnoreResourceParameters =
        [
            "InstanceName", "OutputPath", "ConfigurationData", "Verbose", "Debug",
            "ErrorAction", "WarningAction", "InformationAction", "ErrorVariable",
            "WarningVariable", "InformationVariable", "OutVariable", "OutBuffer",
            "PipelineVariable", "WhatIf", "Confirm"
        ];

        /// <summary>
        /// Gets DSC resources on the machine with optional filtering.
        /// </summary>
        /// <param name="resourceNames">Optional array of resource names to filter on (supports wildcards)</param>
        /// <param name="moduleName">Optional module name to filter on</param>
        /// <param name="includeCompositeResources">Whether to include composite (configuration-based) resources</param>
        /// <returns>List of discovered DSC resources</returns>
        public static List<DscResourceInfo> GetDscResources(
            string[]? resourceNames = null,
            string? moduleName = null,
            bool includeCompositeResources = true)
        {
            var resources = new List<DscResourceInfo>();
            resourceNames ??= [];

            try
            {
                // Load default CIM keywords
                LoadDefaultCimKeywords();

                // Get module list
                var modules = GetModuleList(moduleName);

                // Import resources from modules
                if (modules is not null && modules.Length > 0)
                {
                    ImportResourcesFromModules(modules);
                }

                // Get resources from CIM cache
                var keywords = GetCachedKeywords(moduleName);
                var dscResourceNames = keywords.Select(k => k.Keyword).ToArray();

                // Process CIM resources
                foreach (var keyword in keywords)
                {
                    var resource = ResourceProcessor.GetResourceFromKeyword(
                        keyword,
                        resourceNames,
                        modules ?? [],
                        dscResourceNames);

                    if (resource is not null)
                    {
                        resources.Add(resource);
                    }
                }

                // Get composite resources (configurations) if requested
                if (includeCompositeResources)
                {
                    var configurations = GetConfigurations();

                    foreach (var config in configurations)
                    {
                        var resource = ResourceProcessor.GetCompositeResource(
                            resourceNames,
                            config,
                            IgnoreResourceParameters,
                            modules ?? []);

                        if (resource is not null &&
                            (string.IsNullOrEmpty(moduleName) ||
                             (resource.ModuleName is not null &&
                              resource.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))) &&
                            !string.IsNullOrEmpty(resource.Path) &&
                            Path.GetFileName(resource.Path).Equals($"{resource.Name}.schema.psm1", StringComparison.OrdinalIgnoreCase))
                        {
                            resources.Add(resource);
                        }
                    }
                }

                // Sort resources by Module and Name
                var sortedResources = resources
                    .OrderBy(r => r.ModuleName ?? string.Empty)
                    .ThenBy(r => r.Name)
                    .ToList();

                // Remove duplicates
                var uniqueResources = new List<DscResourceInfo>();
                var seen = new HashSet<string>();

                foreach (var resource in sortedResources)
                {
                    var key = $"{resource.ModuleName}_{resource.Name}";
                    if (seen.Add(key))
                    {
                        uniqueResources.Add(resource);
                    }
                }

                return uniqueResources;
            }
            finally
            {
                // Cleanup
                ResetDynamicKeywords();
                ClearDscClassCache();
            }
        }

        /// <summary>
        /// Gets the syntax string for a DSC resource.
        /// </summary>
        /// <param name="resource">The DSC resource to get syntax for</param>
        /// <returns>Formatted syntax string</returns>
        public static string GetResourceSyntax(DscResourceInfo resource)
        {
            return DscResourceHelpers.GetSyntax(resource);
        }

        #region Private Helper Methods

        private static void LoadDefaultCimKeywords()
        {
            try
            {
                var dscClassCacheType = Type.GetType(
                    "Microsoft.PowerShell.DesiredStateConfiguration.Internal.DscClassCache, " +
                    "System.Management.Automation",
                    throwOnError: false);

                if (dscClassCacheType is not null)
                {
                    var method = dscClassCacheType.GetMethod(
                        "LoadDefaultCimKeywords",
                        [typeof(Collection<Exception>), typeof(bool)]);

                    if (method is not null)
                    {
                        var errors = new Collection<Exception>();
                        _ = method.Invoke(null, [errors, true]);
                    }
                }
            }
            catch
            {
                // Silently ignore errors
            }
        }

        private static PSModuleInfo[]? GetModuleList(string? moduleName)
        {
            try
            {
                using var ps = System.Management.Automation.PowerShell.Create();
                if (!string.IsNullOrEmpty(moduleName))
                {
                    _ = ps.AddCommand("Get-Module")
                      .AddParameter("ListAvailable", true)
                      .AddParameter("Name", moduleName);
                }
                else
                {
                    var dscModules = DscResourceHelpers.GetDscResourceModules();
                    if (dscModules.Count > 0)
                    {
                        _ = ps.AddCommand("Get-Module")
                          .AddParameter("ListAvailable", true)
                          .AddParameter("Name", dscModules.ToArray());
                    }
                    else
                    {
                        return null;
                    }
                }

                var results = ps.Invoke();
                return results.Select(r => r.BaseObject as PSModuleInfo)
                             .Where(m => m is not null)
                             .ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static void ImportResourcesFromModules(PSModuleInfo[] modules)
        {
            foreach (var module in modules)
            {
                if (module.ExportedDscResources.Count > 0)
                {
                    ImportClassResourcesFromModule(module);
                }

                var dscResourcesPath = Path.Combine(module.ModuleBase, "DscResources");
                if (Directory.Exists(dscResourcesPath))
                {
                    foreach (var resourceDir in Directory.GetDirectories(dscResourcesPath))
                    {
                        var resourceName = Path.GetFileName(resourceDir);
                        ImportCimAndScriptKeywordsFromModule(module, resourceName);
                    }
                }
            }
        }

        private static void ImportClassResourcesFromModule(PSModuleInfo module)
        {
            var dscClassCacheType = Type.GetType(
                "Microsoft.PowerShell.DesiredStateConfiguration.Internal.DscClassCache, " +
                "System.Management.Automation",
                throwOnError: false);

            if (dscClassCacheType is not null)
            {
                var method = dscClassCacheType.GetMethod(
                    "ImportClassResourcesFromModule",
                    BindingFlags.Public | BindingFlags.Static);

                if (method is not null)
                {
                    var resources = new List<string> { "*" };
                    var functionsToDefine = new Dictionary<string, ScriptBlock>(
                        StringComparer.OrdinalIgnoreCase);

                    _ = method.Invoke(null, [module, resources, functionsToDefine]);
                }
            }
        }

        private static void ImportCimAndScriptKeywordsFromModule(PSModuleInfo module, string resourceName)
        {
            var dscClassCacheType = Type.GetType(
                "Microsoft.PowerShell.DesiredStateConfiguration.Internal.DscClassCache, " +
                "System.Management.Automation",
                throwOnError: false);

            if (dscClassCacheType is not null)
            {
                var method = dscClassCacheType.GetMethod(
                    "ImportCimKeywordsFromModule",
                    [typeof(PSModuleInfo), typeof(string), typeof(string).MakeByRefType(), typeof(Dictionary<string, ScriptBlock>), typeof(Collection<Exception>)]);

                if (method is not null)
                {
                    string? schemaFilePath = null;
                    var functionsToDefine = new Dictionary<string, ScriptBlock>(
                        StringComparer.OrdinalIgnoreCase);
                    var keywordErrors = new Collection<Exception>();

                    _ = method.Invoke(null, [module, resourceName, schemaFilePath, functionsToDefine, keywordErrors]);
                }

                method = dscClassCacheType.GetMethod(
                    "ImportScriptKeywordsFromModule",
                    [typeof(PSModuleInfo), typeof(string), typeof(string).MakeByRefType(), typeof(Dictionary<string, ScriptBlock>)]);

                if (method is not null)
                {
                    string? schemaFilePath = null;
                    var functionsToDefine = new Dictionary<string, ScriptBlock>(
                        StringComparer.OrdinalIgnoreCase);

                    _ = method.Invoke(null, [module, resourceName, schemaFilePath, functionsToDefine]);
                }
            }
        }

        internal static List<CimClass> GetCachedClassByFileName(string fileName)
        {
            var dscClassCacheType = Type.GetType(
                "Microsoft.PowerShell.DesiredStateConfiguration.Internal.DscClassCache, " +
                "System.Management.Automation",
                throwOnError: false);
            if (dscClassCacheType is not null)
            {
                var method = dscClassCacheType.GetMethod(
                    "GetCachedClassByFileName",
                    BindingFlags.Public | BindingFlags.Static);

                if (method is not null)
                {
                    var result = method.Invoke(null, [fileName]);
                    return result as List<CimClass> ?? [];
                }
            }
            return [];
        }

        private static DynamicKeyword[] GetCachedKeywords(string? moduleName)
        {
            var dscClassCacheType = Type.GetType(
                "Microsoft.PowerShell.DesiredStateConfiguration.Internal.DscClassCache, " +
                "System.Management.Automation",
                throwOnError: false);

            if (dscClassCacheType is not null)
            {
                var method = dscClassCacheType.GetMethod(
                    "GetCachedKeywords",
                    BindingFlags.Public | BindingFlags.Static);

                if (method is not null)
                {
                    var result = method.Invoke(null, null);
                    if (result is IEnumerable<DynamicKeyword> keywords)
                    {
                        return keywords.Where(k =>
                            !k.IsReservedKeyword &&
                            !string.IsNullOrEmpty(k.ResourceName) &&
                            !DscResourceHelpers.IsHiddenResource(k.ResourceName) &&
                            (string.IsNullOrEmpty(moduleName) ||
                                k.ImplementingModule.Equals(moduleName, StringComparison.OrdinalIgnoreCase)))
                            .ToArray();
                    }
                }
            }

            return [];
        }

        private static ConfigurationInfo[] GetConfigurations()
        {
            try
            {
                using var ps = System.Management.Automation.PowerShell.Create();
                _ = ps.AddCommand("Get-Command")
                  .AddParameter("CommandType", "Configuration");

                var results = ps.Invoke();
                return results.Select(r => r.BaseObject as ConfigurationInfo)
                             .Where(c => c is not null)
                             .ToArray()!;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get commands by command type 'Configuration'. Error message: {ex.Message}");
                return [];
            }
        }

        private static void ResetDynamicKeywords()
        {
            var dynamicKeywordType = typeof(DynamicKeyword);
            var method = dynamicKeywordType.GetMethod(
                "Reset",
                BindingFlags.Public | BindingFlags.Static);

            _ = (method?.Invoke(null, null));
        }

        private static void ClearDscClassCache()
        {
            try
            {
                var dscClassCacheType = Type.GetType(
                    "Microsoft.PowerShell.DesiredStateConfiguration.Internal.DscClassCache, " +
                    "System.Management.Automation",
                    throwOnError: false);

                if (dscClassCacheType is not null)
                {
                    var method = dscClassCacheType.GetMethod(
                        "ClearCache",
                        BindingFlags.Public | BindingFlags.Static);

                    _ = (method?.Invoke(null, null));
                }
            }
            catch
            {
                // Ignore errors
            }
        }
        #endregion
    }
}
