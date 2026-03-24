@{
    # Script module or binary module file associated with this manifest.
    RootModule = 'DSCParser.PSDSC.dll'

    # Version number of this module.
    ModuleVersion = '1.0.0'

    # Supported PSEditions
    CompatiblePSEditions = @('Desktop', 'Core')

    # ID used to uniquely identify this module
    GUID = '18b13a3f-1771-4481-9acd-b0372245b533'

    # Author of this module
    Author = 'Microsoft Corporation'

    # Company or vendor of this module
    CompanyName = 'Microsoft Corporation'

    # Copyright statement for this module
    Copyright = '(c) Microsoft Corporation. All rights reserved.'

    # Description of the functionality provided by this module
    Description = 'High-performance compiled version of Get-DscResource cmdlet for PowerShell Desired State Configuration. This module provides optimized DSC resource discovery with significant performance improvements over the script-based implementation.'

    # Minimum version of the PowerShell engine required by this module
    PowerShellVersion = '5.1'

    # Minimum version of the common language runtime (CLR) required by this module
    DotNetFrameworkVersion = '4.7.2'

    # Minimum version of Microsoft .NET Framework required by this module
    CLRVersion = '4.0'

    # Functions to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no functions to export.
    FunctionsToExport = @()

    # Cmdlets to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no cmdlets to export.
    CmdletsToExport = @('Get-DscResourceV2')

    # Variables to export from this module
    VariablesToExport = @()

    # Aliases to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no aliases to export.
    AliasesToExport = @()

    # Private data to pass to the module specified in RootModule/ModuleToProcess. This may also contain a PSData hashtable with additional module metadata used by PowerShell.
    PrivateData = @{
        PSData = @{
            # Tags applied to this module. These help with module discovery in online galleries.
            Tags = @('DSC', 'DesiredStateConfiguration', 'Configuration', 'Resource', 'Performance', 'Compiled', 'Binary')

            # A URL to the license for this module.
            LicenseUri = 'https://github.com/microsoft/DSCParser/blob/master/LICENSE'

            # A URL to the main website for this project.
            ProjectUri = 'https://github.com/microsoft/DSCParser'

            # ReleaseNotes of this module
            #ReleaseNotes = ""
        }
    }

    # HelpInfo URI of this module
    HelpInfoURI = 'https://aka.ms/ps-dsc-help'
}
