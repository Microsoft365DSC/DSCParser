# DSCParser.CSharp

A high-performance C# implementation of the DSCParser PowerShell module with multi-targeting support for both Windows PowerShell 5.1 and PowerShell 7+ using .

## Overview

DSCParser.CSharp provides the same functionality as the original PowerShell DSCParser module but implemented in C# for better performance and maintainability.

## Features

- **Parse DSC Configurations**: Convert DSC configuration files (.ps1) to structured objects (hashtables)
- **Generate DSC Configurations**: Convert structured objects back to DSC configuration text
- **Multi-Targeting**: Supports both Windows PowerShell 5.1 (.NET Framework 4.8) and PowerShell 7+ (.NET 10)
- **PowerShell AST Parsing**: Leverages PowerShell's Abstract Syntax Tree for accurate parsing
- **CIM Instance Support**: Full support for CIM instances and complex nested structures
- **Comment Preservation**: Optional metadata extraction from comments in DSC files

## Architecture

```text
DSCParser.CSharp/
├── src/
│   ├── DscParser.cs                 # Main parser class
│   ├── DSCParser.CSharp.csproj      # C# project file (multi-targeting)
│   ├── DscResourceInfoMapper.cs     # Resource mapper
│   ├── DscResourceInfo.cs           # Resource info representation
│   └── DscResourceInstance.cs       # Resource representation
Modules/DSCParser/
├── DSCParser.psd1        # PowerShell module manifest
└── Modules/
    ├── DSCParser.CSharp.psm1        # PowerShell wrapper module
```

## Building the Project

### Prerequisites

- .NET 10 SDK or later
- .NET Framework 4.7.1 Developer Pack
- PowerShell 5.1 or PowerShell 7+

### Build Instructions

1. Navigate to the root directory:

   ```powershell
   cd DSCParser.CSharp
   ```

2. Build the project using the provided script:

   ```powershell
   .\Build.ps1 -Configuration Release
   ```

   This will build the netstandard2.0 version and copy it to the `.\DSCParser.CSharp\PowerShellModule` module directory.

## Installation

After building, import the module in either Windows PowerShell 5.1 or PowerShell 7+:

```powershell
# Works in both Windows PowerShell 5.1 and PowerShell 7+
Import-Module .\DSCParser.CSharp\PowerShellModule\DSCParser.CSharp.psd1
```

The module automatically detects which PowerShell version you're using and loads the appropriate assembly:

## Usage

### ConvertTo-DSCObject

Parse a DSC configuration file into structured objects:

```powershell
# Parse from file
$resources = ConvertTo-DSCObject -Path "C:\DSCConfigs\MyConfig.ps1"

# Parse from string content
$content = Get-Content "MyConfig.ps1" -Raw
$resources = ConvertTo-DSCObject -Content $content

# Include comments as metadata
$resources = ConvertTo-DSCObject -Path "MyConfig.ps1" -IncludeComments $true
```

### ConvertFrom-DSCObject

Convert structured objects back to DSC configuration text:

```powershell
# Parse and convert back
$resources = ConvertTo-DSCObject -Path "MyConfig.ps1"
$dscText = ConvertFrom-DSCObject -DSCResources $resources

# Output the generated DSC text
Write-Output $dscText
```

## Key Differences from PowerShell Version

| Feature | PowerShell Version | C# Version |
| --------- | ------------------- | ------------ |
| Performance | Slower (interpreted) | Faster (compiled) |
| Type Safety | Dynamic | Strong typing |
| Maintainability | Script-based | Object-oriented |
| Debugging | VS Code | VS Code + IDE Support |
| Dependencies | Script scope | Isolated context (PS7+) |
| Windows PS 5.1 | Supported | Supported |
| PowerShell 7+ | Supported | Supported |

## API Reference

### ConvertTo-DSCObject

**Parameters:**

- `Path` (String): Path to DSC configuration file
- `Content` (String): DSC configuration content as string
- `IncludeComments` (Boolean): Include comment metadata (default: $false)
- `Schema` (String): Optional schema definition
- `IncludeCIMInstanceInfo` (Boolean): Include CIM instance info (default: $true)

**Returns:** Array of hashtables representing DSC resources

### ConvertFrom-DSCObject

**Parameters:**

- `DSCResources` (Hashtable[]): Array of DSC resource hashtables
- `ChildLevel` (Int32): Indentation level for nested resources (default: 0)

**Returns:** String containing DSC configuration text

## Performance Considerations

The C# implementation offers significant performance improvements:

- **Parsing**: Excluding loading of the DSC resources, up to 9x faster than PowerShell version
- **Memory**: Lower memory footprint due to compiled code
- **Large Files**: Better handling of large DSC configurations
- **Caching**: Built-in caching for CIM classes and resource properties

## Troubleshooting

### Assembly Not Found Error

If you get an error about the assembly not being found:

```powershell
# Ensure the assembly is built and copied to the correct location
$assemblyPath = "DSCParser.CSharp\PowerShellModule\bin\DSCParser.CSharp.dll"
Test-Path $assemblyPath
```

## Related Links

- [PowerShell DSC Documentation](https://docs.microsoft.com/powershell/dsc/)
- [PowerShell AST](https://docs.microsoft.com/powershell/module/microsoft.powershell.core/about/about_abstract_syntax_tree)
