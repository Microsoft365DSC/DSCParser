using Microsoft.PowerShell.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;

namespace DSCParser.PSDSC
{
    /// <summary>
    /// Gets DSC resources on the machine. Allows filtering on a particular resource.
    /// High-performance compiled version of Get-DscResource cmdlet.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "DscResourceV2", DefaultParameterSetName = "Default")]
    [OutputType(typeof(DscResourceInfo), typeof(string))]
    public sealed class GetDscResourceCommand : PSCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets the resource name(s) to filter on
        /// </summary>
        [Parameter(ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "Default")]
        [ValidateNotNullOrEmpty]
        public string[]? Name { get; set; }

        /// <summary>
        /// Gets or sets the module to filter on
        /// </summary>
        [Parameter(ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "Default")]
        [ValidateNotNullOrEmpty]
        public object? Module { get; set; }

        /// <summary>
        /// Gets or sets whether to return syntax instead of resource objects
        /// </summary>
        [Parameter(ParameterSetName = "Default")]
        public SwitchParameter Syntax { get; set; }

        #endregion

        #region Private Fields

        private string? _moduleString;

        #endregion

        #region Cmdlet Overrides

        /// <summary>
        /// BeginProcessing - Parse module parameter
        /// </summary>
        protected override void BeginProcessing()
        {
            try
            {
                // Parse module parameter to extract module name string
                if (Module is not null)
                {
                    if (Module is string moduleName)
                    {
                        _moduleString = moduleName;
                    }
                    else if (Module is ModuleSpecification ms)
                    {
                        _moduleString = ms.Name;
                    }
                    else if (Module is System.Collections.Hashtable ht)
                    {
                        if (ht.ContainsKey("ModuleName"))
                        {
                            _moduleString = ht["ModuleName"]?.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(
                    ex,
                    "FailedToParseModule",
                    ErrorCategory.InvalidArgument,
                    Module));
            }
        }

        /// <summary>
        /// ProcessRecord - Discover and output DSC resources using the static service
        /// </summary>
        protected override void ProcessRecord()
        {
            try
            {
                WriteVerbose("Discovering DSC resources...");

                if (Name is not null && Name.Length > 0)
                {
                    WriteVerbose($"Filtering resources by names: {string.Join(", ", Name)}");
                }

                if (Module is not null)
                {
                    WriteVerbose($"Filtering resources by module: {_moduleString ?? Module.ToString()}");
                }

                // Use the static service to get resources
                var resources = DscResourceService.GetDscResources(
                    resourceNames: Name,
                    moduleName: _moduleString,
                    includeCompositeResources: true);

                WriteVerbose($"Found {resources.Count} resources");

                // Check that all requested resources were found
                CheckResourcesFound(Name, resources);

                // Output results
                foreach (var resource in resources)
                {
                    if (Syntax.IsPresent)
                    {
                        WriteObject(DscResourceService.GetResourceSyntax(resource));
                    }
                    else
                    {
                        WriteObject(resource);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(
                    ex,
                    "FailedToGetResources",
                    ErrorCategory.InvalidOperation,
                    null));
            }
        }

        #endregion

        /// <summary>
        /// Checks that all requested resources were found
        /// </summary>
        private void CheckResourcesFound(string[]? names, List<DscResourceInfo> resources)
        {
            if (names is null || names.Length == 0)
            {
                return;
            }

            var namesWithoutWildcards = names.Where(n => !WildcardPattern.ContainsWildcardCharacters(n)).ToArray();

            foreach (var name in namesWithoutWildcards)
            {
                var found = resources.Any(r =>
                    r.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    r.ResourceType.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (!found)
                {
                    WriteError(new ErrorRecord(
                        new ItemNotFoundException($"Resource '{name}' not found."),
                        "ResourceNotFound",
                        ErrorCategory.ObjectNotFound,
                        name));
                }
            }
        }
    }
}
