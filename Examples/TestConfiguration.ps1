# Example DSC Configuration for Testing DSCParser.CSharp

Configuration TestConfiguration
{
    Node localhost
    {
        File TestFile1
        {
            DestinationPath = "C:\Temp\TestFile.txt"
            Ensure = "Present"
            Contents = "Hello World from DSCParser.CSharp"
            Type = "File"
            Force = $true
        }

        File TestDirectory
        {
            DestinationPath = "C:\Temp\TestDirectory"
            Ensure = "Present"
            Type = "Directory"
        }

        Registry TestRegistry
        {
            Key = "HKEY_LOCAL_MACHINE\SOFTWARE\TestKey"
            Ensure = "Present"
            ValueName = "TestValue"
            ValueData = "TestData"
            ValueType = "String"
        }

        Script TestScript
        {
            GetScript = {
                return @{ Result = "Success" }
            }
            SetScript = {
                Write-Verbose "Setting configuration"
            }
            TestScript = {
                return $true
            }
        }

        Environment TestEnvironmentVariable
        {
            Name = "TestVar"
            Ensure = "Present"
            Value = "TestValue"
        }

        WindowsFeature TestFeature
        {
            Name = "Web-Server"
            Ensure = "Present"
            IncludeAllSubFeature = $true
        }

        Service TestService
        {
            Name = "wuauserv"
            State = "Running"
            StartupType = "Automatic"
        }

        User TestUser
        {
            UserName = "TestUser"
            Ensure = "Present"
            Description = "Test user for DSCParser.CSharp"
            Password = $null
            Disabled = $false
        }

        Group TestGroup
        {
            GroupName = "TestGroup"
            Ensure = "Present"
            Description = "Test group for DSCParser.CSharp"
            Members = @("TestUser")
        }

        Log TestLog
        {
            Message = "DSCParser.CSharp test configuration applied"
        }

        Package TestPackage
        {
            Name = "TestPackage"
            Path = "C:\Temp\TestPackage.msi"
            ProductId = "{12345678-1234-1234-1234-123456789012}"
            Ensure = "Present"
        }

        Archive TestArchive
        {
            Path = "C:\Temp\TestArchive.zip"
            Destination = "C:\Temp\ExtractedArchive"
            Ensure = "Present"
        }
    }
}
