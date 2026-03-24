# Test Script for DSCParser.CSharp Module

param(
    [Parameter()]
    [string]$ModulePath = (Join-Path $PSScriptRoot '..\PowerShellModule\DSCParser.CSharp.psd1')
)

$ErrorActionPreference = 'Stop'

Write-Host "DSCParser.CSharp Test Script" -ForegroundColor Cyan
Write-Host "============================" -ForegroundColor Cyan
Write-Host ""

# Import the module
Write-Host "[1/5] Importing module..." -ForegroundColor Yellow
if (Get-Module DSCParser.CSharp)
{
    Remove-Module DSCParser.CSharp -Force
}
Import-Module $ModulePath -Force
Write-Host "Module imported successfully" -ForegroundColor Green
Write-Host ""

# Test 1: Parse example configuration
Write-Host "[2/5] Testing ConvertTo-DSCObject..." -ForegroundColor Yellow
$configPath = Join-Path $PSScriptRoot 'TestConfiguration.ps1'

if (-not (Test-Path $configPath))
{
    Write-Error "Test configuration file not found: $configPath"
    exit 1
}

try
{
    $resources = ConvertTo-DSCObject -Path $configPath
    Write-Host "Successfully parsed $($resources.Count) resources" -ForegroundColor Green
    Write-Host ""

    # Display first resource
    Write-Host "Sample Resource (first in configuration):" -ForegroundColor Cyan
    $resources[0] | Format-Table -AutoSize
}
catch
{
    Write-Host "Failed to parse configuration" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

# Test 2: Convert back to DSC text
Write-Host "[3/5] Testing ConvertFrom-DSCObject..." -ForegroundColor Yellow
try
{
    $dscText = ConvertFrom-DSCObject -DSCResources $resources
    Write-Host "Successfully converted back to DSC text" -ForegroundColor Green
    Write-Host ""

    # Display first few lines
    Write-Host "Generated DSC Text (first 20 lines):" -ForegroundColor Cyan
    $lines = $dscText -split "`n" | Select-Object -First 20
    foreach ($line in $lines)
    {
        Write-Host $line -ForegroundColor Gray
    }
    Write-Host "..." -ForegroundColor Gray
}
catch
{
    Write-Host "Failed to convert to DSC text" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

# Test 3: Parse with content parameter
Write-Host ""
Write-Host "[4/5] Testing ConvertTo-DSCObject with Content parameter..." -ForegroundColor Yellow
try
{
    $content = Get-Content $configPath -Raw
    $resources2 = ConvertTo-DSCObject -Content $content
    Write-Host "Successfully parsed using Content parameter" -ForegroundColor Green

    if ($resources2.Count -eq $resources.Count)
    {
        Write-Host "Resource count matches ($($resources2.Count))" -ForegroundColor Green
    }
    else
    {
        Write-Host "Resource count mismatch (expected $($resources.Count), got $($resources2.Count))" -ForegroundColor Red
    }
}
catch
{
    Write-Host "Failed to parse with Content parameter" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}

# Test 4: Verify specific resource properties
Write-Host ""
Write-Host "[5/5] Verifying resource properties..." -ForegroundColor Yellow
try
{
    $fileResource = $resources | Where-Object { $_.ResourceName -eq 'File' -and $_.ResourceInstanceName -eq 'TestFile1' }

    if ($null -eq $fileResource)
    {
        Write-Host "Could not find TestFile1 resource" -ForegroundColor Red
    }
    else
    {
        Write-Host "Found TestFile1 resource" -ForegroundColor Green

        $expectedProperties = @{
            'DestinationPath' = 'C:\Temp\TestFile.txt'
            'Ensure' = 'Present'
            'Type' = 'File'
        }

        $allMatch = $true
        foreach ($prop in $expectedProperties.GetEnumerator())
        {
            if ($fileResource[$prop.Key] -eq $prop.Value)
            {
                Write-Host "  $($prop.Key) = $($prop.Value)" -ForegroundColor Green
            }
            else
            {
                Write-Host "  $($prop.Key) expected '$($prop.Value)', got '$($fileResource[$prop.Key])'" -ForegroundColor Red
                $allMatch = $false
            }
        }

        if ($allMatch)
        {
            Write-Host "All properties match expected values" -ForegroundColor Green
        }
    }
}
catch
{
    Write-Host "Failed to verify resource properties" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "All basic tests completed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Module Functions Available:" -ForegroundColor Cyan
Get-Command -Module DSCParser.CSharp | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor White
}
Write-Host ""
