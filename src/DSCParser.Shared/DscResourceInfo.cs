using System.Collections.Generic;
using System;
using System.Management.Automation;

namespace Microsoft.PowerShell.DesiredStateConfiguration
{
    /// <summary>
    /// Enumerated values for DSC resource implementation type
    /// </summary>
    public enum ImplementedAsType
    {
        /// <summary>
        /// DSC resource implementation type not known
        /// </summary>
        None = 0,

        /// <summary>
        /// DSC resource is implemented using PowerShell module
        /// </summary>
        PowerShell = 1,

        /// <summary>
        /// DSC resource is implemented using a CIM provider
        /// </summary>
        Binary = 2,

        /// <summary>
        /// DSC resource is a composite and implemented using configuration keyword
        /// </summary>
        Composite = 3
    }

    /// <summary>
    /// Contains a DSC resource information
    /// </summary>
    public sealed class DscResourceInfo
    {
        /// <summary>
        /// Initializes a new instance of the DscResourceInfo class
        /// </summary>
        public DscResourceInfo()
        {
            Properties = [];
        }

        /// <summary>
        /// Gets or sets resource type name
        /// </summary>
        public string ResourceType { get; set; }

        /// <summary>
        /// Gets or sets Name of the resource. This name is used to access the resource
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets friendly name defined for the resource
        /// </summary>
        public string FriendlyName { get; set; }

        /// <summary>
        /// Gets or sets module which implements the resource. This could point to parent module, if the DSC resource is implemented
        /// by one of nested modules.
        /// </summary>
        public PSModuleInfo Module { get; set; }

        /// <summary>
        /// Gets name of the module which implements the resource.
        /// </summary>
        public string? ModuleName
        {
            get
            {
                return Module?.Name;
            }
        }

        /// <summary>
        /// Gets version of the module which implements the resource.
        /// </summary>
        public Version? Version
        {
            get
            {
                return Module?.Version;
            }
        }

        /// <summary>
        /// Gets or sets of the file which implements the resource. For the reosurces which are defined using
        /// MOF file, this will be path to a module which resides in the same folder where schema.mof file is present.
        /// For composite resources, this will be the module which implements the resource
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets parent folder, where the resource is defined
        /// It is the folder containing either the implementing module(=Path) or folder containing ".schema.mof".
        /// For native providers, Path will be null and only ParentPath will be present.
        /// </summary>
        public string ParentPath { get; set; }

        /// <summary>
        /// Gets or sets a value which indicate how DSC resource is implemented
        /// </summary>
        public ImplementedAsType ImplementedAs { get; set; }

        /// <summary>
        /// Gets or sets company which owns this resource
        /// </summary>
        public string CompanyName { get; set; }

        /// <summary>
        /// Gets or sets properties of the resource
        /// </summary>
        public List<DscResourcePropertyInfo> PropertiesAsResourceInfo => Properties.ConvertAll(prop => (DscResourcePropertyInfo)prop);

        /// <summary>
        /// Gets or sets properties of the resource
        /// </summary>
        public List<object> Properties { get; private set; }

        /// <summary>
        /// Gets or sets implementation detail (e.g., "ScriptBased", "ClassBased")
        /// </summary>
        public string? ImplementationDetail { get; set; }

        /// <summary>
        /// Updates properties of the resource. Same as public variant, but accepts list of DscResourcePropertyInfo.
        /// Backwards compatibility for Windows PowerShell. It uses the DSCResourcePropertyInfo type from
        /// the Microsoft.Windows.DSC.CoreConfProviders.dll, which is incompatible with our own type.
        /// </summary>
        /// <param name="properties">Updated properties</param>
        public void UpdateProperties(List<DscResourcePropertyInfo> properties)
        {
            Properties = properties.ConvertAll(prop => (object)prop);
        }

        /// <summary>
        /// Updates properties of the resource.
        /// </summary>
        /// <param name="properties">Updated properties</param>
        public void UpdateProperties(List<object> properties)
        {
            Properties = properties;
        }
    }

    /// <summary>
    /// Contains a DSC resource property information
    /// </summary>
    public sealed class DscResourcePropertyInfo
    {
        /// <summary>
        /// Initializes a new instance of the DscResourcePropertyInfo class
        /// </summary>
        public DscResourcePropertyInfo()
        {
            Values = [];
        }

        /// <summary>
        /// Gets or sets name of the property
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets type of the property
        /// </summary>
        public string PropertyType { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the property is mandatory or not
        /// </summary>
        public bool IsMandatory { get; set; }

        /// <summary>
        /// Gets Values for a resource property
        /// </summary>
        public List<string> Values { get; set; }
    }
}
