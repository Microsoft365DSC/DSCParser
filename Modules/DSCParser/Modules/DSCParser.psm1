# DSCParser.CSharp PowerShell Module
# Loads the C# assembly using Assembly Load Context for isolation

$Script:ModuleRoot = $PSScriptRoot

# Check if running in PowerShell Core or Windows PowerShell
$Script:IsPowerShellCore = $PSVersionTable.PSEdition -eq 'Core'
$Script:AssemblyPath = Join-Path $Script:ModuleRoot "bin\DSCParser.CSharp.dll"
$Script:AssemblyLoaded = $false

# Function to load the C# assembly using Assembly Load Context
function Initialize-DscParserAssembly
{
    [CmdletBinding()]
    [OutputType([System.Boolean])]
    param()

    if ($Script:AssemblyLoaded)
    {
        Write-Verbose "DSCParser.CSharp assembly is already initialized."
        return $true
    }

    try
    {
        # Check if assembly file exists
        if (-not (Test-Path -Path $Script:AssemblyPath))
        {
            throw "DSCParser.CSharp assembly not found at: $Script:AssemblyPath. Please build the C# project first."
        }

        Add-Type -Path $Script:AssemblyPath -ErrorAction Stop

        Write-Verbose -Message "Successfully loaded DSCParser.CSharp assembly (PowerShell $($PSVersionTable.PSEdition))"
        return $true
    }
    catch
    {
        Write-Error "Failed to load DSCParser.CSharp assembly: $_"
        return $false
    }
}

# Initialize the assembly on module import
$Script:AssemblyLoaded = Initialize-DscParserAssembly

<#
.SYNOPSIS
    Converts a DSC configuration file or content to DSC objects.

.DESCRIPTION
    This function parses a DSC configuration file or string content and converts it
    into an array of hashtables representing each DSC resource instance.
    Uses the C# implementation with Assembly Load Context isolation.

.PARAMETER Path
    The path to the DSC configuration file to parse.

.PARAMETER Content
    The DSC configuration content as a string.

.PARAMETER IncludeComments
    Include comment metadata in the parsed output.

.PARAMETER Schema
    Optional schema definition for parsing.

.PARAMETER IncludeCIMInstanceInfo
    Include CIM instance information in the output. Default is $true.

.PARAMETER DscResources
    An array of DscResourceInfo objects to assist in parsing.

.EXAMPLE
    ConvertTo-DSCObject -Path "C:\DSCConfigs\MyConfig.ps1"

.EXAMPLE
    $content = Get-Content "MyConfig.ps1" -Raw
    ConvertTo-DSCObject -Content $content -IncludeComments $true
#>
function ConvertTo-DSCObject
{
    [CmdletBinding(DefaultParameterSetName = 'Path')]
    [OutputType([Array])]
    param
    (
        [Parameter(Mandatory = $true, ParameterSetName = 'Path')]
        [ValidateScript({
            if (-not ($_ | Test-Path)) {
                throw "File or folder does not exist"
            }
            if (-not ($_ | Test-Path -PathType Leaf)) {
                throw "The Path argument must be a file. Folder paths are not allowed."
            }
            return $true
        })]
        [System.String]
        $Path,

        [Parameter(Mandatory = $true, ParameterSetName = 'Content')]
        [System.String]
        $Content,

        [Parameter(ParameterSetName = 'Path')]
        [Parameter(ParameterSetName = 'Content')]
        [System.Boolean]
        $IncludeComments = $false,

        [Parameter(ParameterSetName = 'Path')]
        [Parameter(ParameterSetName = 'Content')]
        [System.String]
        $Schema,

        [Parameter(ParameterSetName = 'Path')]
        [Parameter(ParameterSetName = 'Content')]
        [System.Boolean]
        $IncludeCIMInstanceInfo = $true,

        [Parameter(ParameterSetName = 'Path')]
        [Parameter(ParameterSetName = 'Content')]
        [Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo[]]
        $DscResourceInfo
    )

    if (-not $Script:AssemblyLoaded)
    {
        throw "DSCParser.CSharp assembly is not loaded. Module initialization failed."
    }

    try
    {
        if ($null -eq $Script:DscResourceCache -and -not $PSBoundParameters.ContainsKey('DscResourceInfo'))
        {
            $Script:DscResourceCache = Get-DscResourceV2
        }
        elseif ($PSBoundParameters.ContainsKey('DscResourceInfo'))
        {
            $Script:DscResourceCache = $DscResourceInfo
        }

        $options = [DSCParser.CSharp.DscParseOptions]::new()

        # Set options
        $options.IncludeComments = $IncludeComments
        $options.IncludeCIMInstanceInfo = $IncludeCIMInstanceInfo
        if (-not [string]::IsNullOrEmpty($Schema))
        {
            $options.Schema = $Schema
        }

        # Call ConvertToDscObject
        if ($PSCmdlet.ParameterSetName -eq 'Path')
        {
            $result = [DSCParser.CSharp.DscParser]::ConvertToDscObject($Path, $null, $options, $Script:DscResourceCache)
        }
        else
        {
            $result = [DSCParser.CSharp.DscParser]::ConvertToDscObject($null, $Content, $options, $Script:DscResourceCache)
        }

        # Convert result to array of hashtables
        $output = @()
        foreach ($item in $result)
        {
            $hashtable = $item.ToHashtable()
            $output += $hashtable
        }

        return $output
    }
    catch
    {
        Write-Error "Error parsing DSC configuration: $_"
        throw
    }
}

<#
.SYNOPSIS
    Converts DSC objects back to DSC configuration text.

.DESCRIPTION
    This function takes an array of hashtables representing DSC resources
    and converts them back into DSC configuration text format.
    Uses the C# implementation with Assembly Load Context isolation.

.PARAMETER DSCResources
    An array of hashtables representing DSC resource instances.

.PARAMETER ChildLevel
    The indentation level for nested resources. Default is 0.

.EXAMPLE
    $resources = ConvertTo-DSCObject -Path "MyConfig.ps1"
    $dscText = ConvertFrom-DSCObject -DSCResources $resources
#>
function ConvertFrom-DSCObject
{
    [CmdletBinding()]
    [OutputType([System.String])]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [System.Collections.Hashtable[]]
        $DSCResources,

        [Parameter(Mandatory = $false)]
        [System.Int32]
        $ChildLevel = 0
    )

    process
    {
        if (-not $Script:AssemblyLoaded)
        {
            throw "DSCParser.CSharp assembly is not loaded. Module initialization failed."
        }

        try
        {
            $result = [DSCParser.CSharp.DscParser]::ConvertFromDscObject($DSCResources, $ChildLevel)
            return $result
        }
        catch
        {
            Write-Error "Error converting DSC objects: $_"
            throw
        }
    }
}
