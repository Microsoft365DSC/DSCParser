using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Language;
using DSCParser.CSharp;
using DSCParser.PSDSC;
using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using DscResourcePropertyInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourcePropertyInfo;

namespace DSCParser.Tests;

/// <summary>
/// Coverage of the parse path a host without installed modules uses: keywords come from a
/// serialized schema cache and the configuration's Import-DscResource statements are ignored.
/// </summary>
public class DscParserSchemaCacheKeywordTests : IDisposable
{
    private const string ResourceKeyword = "ContosoPolicy";

    private const string CimKeyword = "MSFT_ContosoAssignment";

    public void Dispose()
    {
        DscKeywordRegistry.ResetSchemaCache();
        DscKeywordRegistry.ClearKeywordTable();
        DscParser.ClearCaches();
        DscParser.WarningSink = null;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ConvertToDscObject_WithSchemaCacheKeywords_ShouldParseTypedProperties()
    {
        Register();

        const string configuration = """
            Configuration TenantConfig
            {
                Import-DscResource -ModuleName 'Contoso' -ModuleVersion '2.0.0'

                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                        Enabled     = $true
                        Threshold   = 3
                        Tags        = @('alpha', 'beta')
                        Ensure      = "Present"
                    }
                }
            }
            """;

        List<DscResourceInstance> result = Parse(configuration);

        DscResourceInstance policy = Assert.Single(result);
        Assert.Equal(ResourceKeyword, policy.ResourceName);
        Assert.Equal("Corp", policy.ResourceInstanceName);
        Assert.Equal("Corp policy", policy.Properties["DisplayName"]);
        Assert.Equal(true, policy.Properties["Enabled"]);
        Assert.Equal(3, policy.Properties["Threshold"]);
        Assert.Equal(new object[] { "alpha", "beta" }, policy.Properties["Tags"]);
    }

    [Fact]
    public void ConvertToDscObject_WithSchemaCacheKeywords_ShouldParseNestedCimInstances()
    {
        Register();

        const string configuration = """
            Configuration TenantConfig
            {
                Import-DscResource -ModuleName 'Contoso'

                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                        Assignments = @(
                            MSFT_ContosoAssignment
                            {
                                Target = 'AllUsers'
                            }
                        )
                    }
                }
            }
            """;

        List<DscResourceInstance> result = Parse(configuration);

        object[] assignments = Assert.IsType<object[]>(Assert.Single(result).ToHashtable()["Assignments"]);
        Hashtable assignment = Assert.IsType<Hashtable>(Assert.Single(assignments));
        Assert.Equal("AllUsers", assignment["Target"]);
        Assert.Equal(CimKeyword, assignment["CIMInstance"]);
    }

    [Fact]
    public void ConvertToDscObject_WithSchemaCacheKeywords_ShouldLeaveKeywordTableEmpty()
    {
        Register();

        _ = Parse("""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                    }
                }
            }
            """);

        Assert.False(DynamicKeyword.ContainsKeyword(ResourceKeyword));
    }

    [Fact]
    public void ConvertToDscObject_WithoutRegisteredKeywords_ShouldThrow()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Parse("""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                    }
                }
            }
            """));

        Assert.Contains("RegisterFromSchemaCache", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertToDscObject_WithoutNodeStatement_ShouldReadTheWholeContent()
    {
        Register();

        List<DscResourceInstance> result = Parse("""
            ContosoPolicy "Corp"
            {
                DisplayName = "Corp policy"
            }
            """);

        Assert.Equal("Corp", Assert.Single(result).ResourceInstanceName);
    }

    [Fact]
    public void ConvertToDscObject_WithSettingsProperty_ShouldReadEveryResource()
    {
        _ = DscKeywordRegistry.RegisterFromSchemaCache(Microsoft365SchemaCacheEntries());

        const string configuration = """
            Configuration M365TenantConfig
            {
                param (
                )

                $OrganizationName = $ConfigurationData.NonNodeData.OrganizationName

                Import-DscResource -ModuleName 'Microsoft365DSC' -ModuleVersion '1.26.1007.1'

                Node localhost
                {
                    IntuneSettingCatalogCustomPolicyWindows10 "IntuneSettingCatalogCustomPolicyWindows10-Windows 11 Device Lockdown"
                    {
                        ApplicationId         = $ConfigurationData.NonNodeData.ApplicationId;
                        Assignments           = @(
                            MSFT_DeviceManagementConfigurationPolicyAssignments{
                                deviceAndAppManagementAssignmentFilterType = 'none'
                                dataType = '#microsoft.graph.allDevicesAssignmentTarget'
                            }
                        );
                        CertificateThumbprint = $ConfigurationData.NonNodeData.CertificateThumbprint;
                        Description           = "Baseline lockdown settings for shared Windows 11 devices";
                        Ensure                = "Present";
                        Id                    = "5f3c7a52-9d0e-4b8f-a1c6-2e7d94b3f061";
                        Name                  = "Windows 11 Device Lockdown";
                        Platforms             = "windows10";
                        RoleScopeTagIds       = @("0");
                        Settings              = @(
                            MSFT_MicrosoftGraphdeviceManagementConfigurationSetting{
                                SettingInstance = MSFT_MicrosoftGraphDeviceManagementConfigurationSettingInstance{
                                    ChoiceSettingValue = MSFT_MicrosoftGraphDeviceManagementConfigurationChoiceSettingValue{
                                        Value = 'device_vendor_msft_policy_config_abovelock_allowcortanaabovelock_1'
                                    }
                                    SettingDefinitionId = 'device_vendor_msft_policy_config_abovelock_allowcortanaabovelock'
                                    odataType = '#microsoft.graph.deviceManagementConfigurationChoiceSettingInstance'
                                }
                            }
                        );
                        Technologies          = "mdm";
                        TenantId              = $OrganizationName;
                    }
                    IntuneAppConfigurationDevicePolicy "IntuneAppConfigurationDevicePolicy-Outlook for iOS"
                    {
                        ApplicationId         = $ConfigurationData.NonNodeData.ApplicationId;
                        Assignments           = @();
                        CertificateThumbprint = $ConfigurationData.NonNodeData.CertificateThumbprint;
                        ConnectedAppsEnabled  = $False;
                        Description           = "";
                        DisplayName           = "Outlook for iOS";
                        Ensure                = "Present";
                        Id                    = "9b1d4e27-6c3a-4f80-b5e2-71a0c8d3e94f";
                        RoleScopeTagIds       = @("0");
                        Settings              = @(
                            MSFT_MicrosoftGraphappConfigurationSettingItem{
                                AppConfigKey = 'com.microsoft.outlook.Mail.FocusedInbox'
                                AppConfigKeyType = 'booleanType'
                                AppConfigKeyValue = 'true'
                            }
                        );
                        TargetedMobileApps    = @("3f2a9c1e-8b47-4d65-a0e3-c5b9174d2f80");
                        TenantId              = $OrganizationName;
                    }
                }
            }
            """;

        List<DscResourceInstance> result = DscParser.ConvertToDscObject(
            content: configuration,
            options: new DscParseOptions { UseRegisteredKeywords = true },
            dscResources: Microsoft365ResourceDefinitions());

        Assert.Equal(
            ["IntuneSettingCatalogCustomPolicyWindows10", "IntuneAppConfigurationDevicePolicy"],
            result.Select(resource => resource.ResourceName));

        object[] catalogSettings = Assert.IsType<object[]>(result[0].ToHashtable()["Settings"]);
        Hashtable setting = Assert.IsType<Hashtable>(Assert.Single(catalogSettings));
        Hashtable settingInstance = Assert.IsType<Hashtable>(setting["SettingInstance"]);
        Hashtable choice = Assert.IsType<Hashtable>(settingInstance["ChoiceSettingValue"]);
        Assert.Equal("device_vendor_msft_policy_config_abovelock_allowcortanaabovelock_1", choice["Value"]);
        Assert.Equal("mdm", result[0].Properties["Technologies"]);

        object[] appSettings = Assert.IsType<object[]>(result[1].ToHashtable()["Settings"]);
        Hashtable appSetting = Assert.IsType<Hashtable>(Assert.Single(appSettings));
        Assert.Equal("com.microsoft.outlook.Mail.FocusedInbox", appSetting["AppConfigKey"]);
        Assert.Equal("Outlook for iOS", result[1].Properties["DisplayName"]);
    }

    [Fact]
    public void ConvertToDscObject_WithUnknownPropertyInExport_ShouldReportTheFileLine()
    {
        List<string> warnings = ParseMicrosoft365Warnings("""
            # Generated with Microsoft365DSC version 1.26.1007.1
            # For additional information on the Dependencies of the Microsoft365DSC module, please visit
            # https://microsoft365dsc.com/user-guide/get-started/powershell7-support/
            param (
            )

            Configuration M365TenantConfig
            {
                param (
                )

                $OrganizationName = $ConfigurationData.NonNodeData.OrganizationName

                Import-DscResource -ModuleName 'Microsoft365DSC' -ModuleVersion '1.26.1007.1'

                Node localhost
                {
                    IntuneAppConfigurationDevicePolicy "IntuneAppConfigurationDevicePolicy-Outlook for iOS"
                    {
                        ApplicationId         = $ConfigurationData.NonNodeData.ApplicationId;
                        CertificateThumbprint = $ConfigurationData.NonNodeData.CertificateThumbprint;
                        DisplayName           = "Outlook for iOS";
                        NoSuchSetting         = "Unknown";
                        Ensure                = "Present";
                        TenantId              = $OrganizationName;
                    }
                }
            }

            M365TenantConfig -ConfigurationData .\ConfigurationData.psd1
            """);

        Assert.Contains(warnings, warning => warning.Contains("Property 'NoSuchSetting' (line 23)", StringComparison.Ordinal));
    }

    [Fact]
    public void ConvertToDscObject_WithNodeAfterMultiLineParamBlock_ShouldReportTheFileLine()
    {
        List<string> warnings = ParseMicrosoft365Warnings("""
            Configuration M365TenantConfig
            {
                param (
                    [parameter()]
                    [System.Management.Automation.PSCredential]
                    $Credential
                )

                if ($null -eq $Credential)
                {
                    $CredsCredential = Get-Credential -Message "Credentials"
                }
                else
                {
                    $CredsCredential = $Credential
                }

                $OrganizationName = $CredsCredential.UserName.Split('@')[1]
                Import-DscResource -ModuleName 'Microsoft365DSC' -ModuleVersion '1.26.1007.1'

                Node localhost
                {
                    IntuneAppConfigurationDevicePolicy "IntuneAppConfigurationDevicePolicy-Outlook for iOS"
                    {
                        DisplayName           = "Outlook for iOS";
                        Ensure                = "Present";
                        Settings              = @(
                            MSFT_MicrosoftGraphappConfigurationSettingItem{
                                AppConfigKey = 'com.microsoft.outlook.Mail.FocusedInbox'
                                AppConfigKeyValue = 'true'
                            }
                        );
                        NoSuchSetting         = "Unknown";
                        TenantId              = $OrganizationName;
                    }
                }
            }
            """);

        Assert.Contains(warnings, warning => warning.Contains("Property 'NoSuchSetting' (line 33)", StringComparison.Ordinal));
    }

    [Fact]
    public void ConvertToDscObject_WithUnknownPropertyAfterQuotedValueRepair_ShouldReportTheFileLine()
    {
        List<string> warnings = ParseMicrosoft365Warnings("""
            Configuration M365TenantConfig
            {
                param (
                )

                Import-DscResource -ModuleName 'Microsoft365DSC' -ModuleVersion '1.26.1007.1'

                Node localhost
                {
                    IntuneAppConfigurationDevicePolicy "IntuneAppConfigurationDevicePolicy-Outlook for iOS"
                    {
                        DisplayName           = "Outlook for iOS";
                        Id                    = 12345678-1234-1234-ad9c-123456789abc;
                        NoSuchSetting         = "Unknown";
                        Ensure                = "Present";
                    }
                }
            }
            """);

        Assert.Contains(warnings, warning => warning.StartsWith("Quoted 1 unquoted value(s)", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("Property 'NoSuchSetting' (line 14)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Settings")]
    [InlineData("LocalConfigurationManager")]
    [InlineData("ConfigurationRepositoryWeb")]
    [InlineData("ResourceRepositoryWeb")]
    [InlineData("ReportServerWeb")]
    [InlineData("PartialConfiguration")]
    [InlineData("User")]
    [InlineData("Configuration")]
    [InlineData("End")]
    public void ConvertToDscObject_WithPropertyNamedLikeAKeyword_ShouldReadEveryResource(string property)
    {
        _ = DscKeywordRegistry.RegisterFromSchemaCache(
        [
            Entry(ResourceKeyword, "NameRequired", new Dictionary<string, string[]>
            {
                ["DisplayName"] = ["String"],
                [property] = ["StringArray"],
            }),
        ]);

        List<DscResourceInstance> result = Parse($$"""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "First"
                    {
                        {{property}} = @(
                            'alpha'
                            'beta'
                        )
                        DisplayName = "First policy"
                    }
                    ContosoPolicy "Second"
                    {
                        DisplayName = "Second policy"
                    }
                }
            }
            """);

        Assert.Equal(["First", "Second"], result.Select(resource => resource.ResourceInstanceName));
        Assert.Equal(new object[] { "alpha", "beta" }, result[0].Properties[property]);
    }

    [Fact]
    public void ConvertToDscObject_WithNodeOutsideConfigurationAndKeywordProperty_ShouldReadEveryResource()
    {
        _ = DscKeywordRegistry.RegisterFromSchemaCache(
        [
            Entry(ResourceKeyword, "NameRequired", new Dictionary<string, string[]>
            {
                ["DisplayName"] = ["String"],
                ["End"] = ["String"],
            }),
        ]);

        List<DscResourceInstance> result = Parse("""
            Node localhost
            {
                ContosoPolicy "First"
                {
                    End = '17:00'
                    DisplayName = "First policy"
                }
                ContosoPolicy "Second"
                {
                    DisplayName = "Second policy"
                }
            }
            """);

        Assert.Equal(["First", "Second"], result.Select(resource => resource.ResourceInstanceName));
    }

    [Fact]
    public void ConvertToDscObject_WithUnclosedNodeBlock_ShouldThrow()
    {
        Register();

        _ = Assert.Throws<InvalidOperationException>(() => Parse("""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                    }
            """));
    }

    [Fact]
    public void ConvertToDscObject_WithStringBeforeCimInstanceInArray_ShouldReadBoth()
    {
        Register();

        List<DscResourceInstance> result = Parse("""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                        Assignments = @(
                            'plain'
                            MSFT_ContosoAssignment {
                                Target = 'AllUsers'
                            }
                        )
                    }
                }
            }
            """);

        List<object> assignments = Assert.IsType<List<object>>(Assert.Single(result).Properties["Assignments"]);
        Assert.Equal("plain", assignments[0]);
        Assert.Equal("AllUsers", Assert.IsType<Dictionary<string, object?>>(assignments[1])["Target"]);
    }

    [Fact]
    public void ConvertToDscObject_WithConfigurationWithoutNode_ShouldReadItsResources()
    {
        Register();
        List<string> warnings = [];
        DscParser.WarningSink = warnings.Add;

        List<DscResourceInstance> result = Parse("""
            Configuration TenantConfig
            {
                param ()

                $OrganizationName = 'contoso.onmicrosoft.com'

                ContosoPolicy "Corp"
                {
                    DisplayName = "Corp policy"
                }
                ContosoPolicy "Branch"
                {
                    DisplayName = "Branch policy"
                }
            }

            TenantConfig
            """);

        Assert.Equal(["Corp", "Branch"], result.Select(resource => resource.ResourceInstanceName));
        Assert.Empty(warnings);
    }

    [Fact]
    public void ConvertToDscObject_WithMissingMandatoryProperties_ShouldWarnPerResourceAndKeepTheResource()
    {
        _ = DscKeywordRegistry.RegisterFromSchemaCache(
        [
            Entry(ResourceKeyword, "NameRequired", new Dictionary<string, string[]>
            {
                ["DisplayName"] = ["String"],
                ["Enabled"] = ["Boolean"],
                ["Assignments"] = ["ContosoAssignment[]"],
            }, "DisplayName"),
            Entry(CimKeyword, "NoName", new Dictionary<string, string[]>
            {
                ["Target"] = ["String"],
                ["Scope"] = ["String"],
            }, "Target"),
        ]);
        List<string> warnings = [];
        DscParser.WarningSink = warnings.Add;

        List<DscResourceInstance> result = Parse("""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        Enabled     = $true
                        Assignments = @(
                            MSFT_ContosoAssignment {
                                Scope = 'Tenant'
                            }
                        )
                    }
                }
            }
            """);

        DscResourceInstance policy = Assert.Single(result);
        Assert.Equal(true, policy.Properties["Enabled"]);
        Assert.Equal(2, warnings.Count);
        Assert.Contains("Resource 'ContosoPolicy' (instance 'Corp', line 5) is missing the mandatory property 'DisplayName'.", warnings);
        Assert.Contains("'MSFT_ContosoAssignment' (line 9) in resource 'ContosoPolicy' (instance 'Corp') is missing the mandatory property 'Target'.", warnings);
    }

    [Fact]
    public void ConvertToDscObject_WithTwoNodeBlocks_ShouldReadBothAndWarnAboutTheDifferentNames()
    {
        Register();
        List<string> warnings = [];
        DscParser.WarningSink = warnings.Add;

        List<DscResourceInstance> result = Parse("""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "First"
                    {
                        DisplayName = "First policy"
                    }
                }
                Node other
                {
                    ContosoPolicy "Second"
                    {
                        DisplayName = "Second policy"
                    }
                }
            }
            """);

        Assert.Equal(["First", "Second"], result.Select(resource => resource.ResourceInstanceName));
        Assert.Equal(
            ["Read the resources of the Node blocks 'localhost', 'other' into one configuration."],
            warnings);
    }

    [Fact]
    public void ConvertToDscObject_WithResourceBesideNode_ShouldReadItInDocumentOrder()
    {
        Register();

        List<DscResourceInstance> result = Parse("""
            Configuration TenantConfig
            {
                ContosoPolicy "Before"
                {
                    DisplayName = "Before policy"
                }
                Node localhost
                {
                    ContosoPolicy "Inside"
                    {
                        DisplayName = "Inside policy"
                    }
                }
            }
            """);

        Assert.Equal(["Before", "Inside"], result.Select(resource => resource.ResourceInstanceName));
    }

    [Fact]
    public void ConvertToDscObject_WithConcatenatedValues_ShouldFoldConstantsAndWarnAboutTheRest()
    {
        Register();
        List<string> warnings = [];
        DscParser.WarningSink = warnings.Add;

        List<DscResourceInstance> result = Parse("""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp" + " " + "policy"
                        Threshold   = 1 + 2
                        Ensure      = $prefix + "Present"
                        Assignments = @(
                            MSFT_ContosoAssignment {
                                Target = 'Kiosk' + ' Devices'
                            }
                        )
                    }
                }
            }
            """);

        DscResourceInstance policy = Assert.Single(result);
        Assert.Equal("Corp policy", policy.Properties["DisplayName"]);
        Assert.Equal(3, policy.Properties["Threshold"]);
        Assert.Equal("$prefix + \"Present\"", policy.Properties["Ensure"]);
        List<object> assignments = Assert.IsType<List<object>>(policy.Properties["Assignments"]);
        Assert.Equal("Kiosk Devices", Assert.IsType<Dictionary<string, object?>>(Assert.Single(assignments))["Target"]);
        Assert.Equal(
            ["The value '$prefix + \"Present\"' (line 9) is not a constant expression and was kept as text."],
            warnings);
    }

    [Fact]
    public void ConvertToDscObject_WithConcatenationInSingleCimInstance_ShouldFoldTheWholeValue()
    {
        Register();
        List<string> warnings = [];
        DscParser.WarningSink = warnings.Add;

        List<DscResourceInstance> result = Parse("""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                        Assignments = MSFT_ContosoAssignment {
                            Target = 'Kiosk' + ' Devices'
                            Scope  = $tenant + '.onmicrosoft.com'
                        }
                    }
                }
            }
            """);

        Dictionary<string, object?> assignment = Assert.IsType<Dictionary<string, object?>>(Assert.Single(result).Properties["Assignments"]);
        Assert.Equal("Kiosk Devices", assignment["Target"]);
        Assert.Equal("$tenant + '.onmicrosoft.com'", assignment["Scope"]);
        Assert.Equal(
            ["The value '$tenant + '.onmicrosoft.com'' (line 10) is not a constant expression and was kept as text."],
            warnings);
    }

    [Fact]
    public void ConvertToDscObject_WithCommaListWithoutArrayOperator_ShouldReturnAList()
    {
        Register();

        List<DscResourceInstance> result = Parse("""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                        Tags        = 'alpha', 'beta'
                        Assignments = MSFT_ContosoAssignment {
                            Target = 'a', 'b'
                        }
                    }
                }
            }
            """);

        DscResourceInstance policy = Assert.Single(result);
        Assert.Equal(new object[] { "alpha", "beta" }, Assert.IsType<List<object>>(policy.Properties["Tags"]));
        Dictionary<string, object?> assignment = Assert.IsType<Dictionary<string, object?>>(policy.Properties["Assignments"]);
        Assert.Equal(new object[] { "a", "b" }, Assert.IsType<List<object>>(assignment["Target"]));
    }

    [Fact]
    public void ConvertToDscObject_WithCommandFormValueOnTheNextLine_ShouldReadIt()
    {
        Register();

        List<DscResourceInstance> result = Parse("""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                        Assignments = MSFT_Credential {
                            UserName =
                                'admin'
                            Password = 'secret'
                        }
                    }
                }
            }
            """);

        Dictionary<string, object?> credential = Assert.IsType<Dictionary<string, object?>>(Assert.Single(result).Properties["Assignments"]);
        Assert.Equal("admin", credential["UserName"]);
        Assert.Equal("secret", credential["Password"]);
    }

    [Fact]
    public void ConvertToDscObject_WithCommandInArray_ShouldKeepItsTextAndWarn()
    {
        Register();
        List<string> warnings = [];
        DscParser.WarningSink = warnings.Add;

        List<DscResourceInstance> result = Parse("""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                        Tags        = @(
                            'alpha'
                            Get-Item $path
                        )
                    }
                }
            }
            """);

        Assert.Equal(new object[] { "alpha", "Get-Item $path" }, Assert.IsType<List<object>>(Assert.Single(result).Properties["Tags"]));
        Assert.Equal(["The value 'Get-Item $path' (line 10) is not a constant expression and was kept as text."], warnings);
    }

    [Fact]
    public void ConvertToDscObject_WithNodeBlocksInsideStatementBlocks_ShouldReadThem()
    {
        Register();

        List<DscResourceInstance> result = Parse("""
            Configuration TenantConfig
            {
                ContosoPolicy "Beside"
                {
                    DisplayName = "Beside policy"
                }
                if ($ConfigurationData)
                {
                    Node localhost
                    {
                        ContosoPolicy "InIf"
                        {
                            DisplayName = "If policy"
                        }
                    }
                }
                foreach ($nodeName in @('localhost'))
                {
                    Node $nodeName {
                        ContosoPolicy "InLoop"
                        {
                            DisplayName = "Loop policy"
                        }
                    }
                }
            }
            """);

        Assert.Equal(["Beside", "InIf", "InLoop"], result.Select(resource => resource.ResourceInstanceName));
    }

    [Fact]
    public void RegisterFromSchemaCache_WithEmptyValueMapKey_ShouldKeepIt()
    {
        Hashtable entry = Entry(ResourceKeyword, "NameRequired", new Dictionary<string, string[]>
        {
            ["Mode"] = ["String", "", "Strict"],
        });
        _ = DscKeywordRegistry.RegisterFromSchemaCache([entry]);

        DscKeywordRegistry.MaterializeSchemaCacheKeywords();
        try
        {
            Assert.Equal(["", "Strict"], DynamicKeyword.GetKeyword(ResourceKeyword).Properties["Mode"].ValueMap.Keys.Order());
        }
        finally
        {
            DscKeywordRegistry.ClearKeywordTable();
        }
    }

    [Fact]
    public void RegisterFromSchemaCache_WithSameKeywordTwice_ShouldNotDuplicate()
    {
        Assert.Equal(2, DscKeywordRegistry.RegisterFromSchemaCache(SchemaCacheEntries()));
        Assert.Equal(2, DscKeywordRegistry.RegisterFromSchemaCache(SchemaCacheEntries()));
    }

    [Fact]
    public void RegisterFromSchemaCache_WithPSObjectEntries_ShouldRegisterKeywords()
    {
        List<object> entries = [.. SchemaCacheEntries().Select(entry =>
            (object)PSObject.AsPSObject(entry))];

        Assert.Equal(2, DscKeywordRegistry.RegisterFromSchemaCache(entries));
        Assert.True(DscKeywordRegistry.HasSchemaCacheKeywords);
    }

    private static List<DscResourceInstance> Parse(string configuration)
    {
        return DscParser.ConvertToDscObject(
            content: configuration,
            options: new DscParseOptions { UseRegisteredKeywords = true },
            dscResources: ResourceDefinitions());
    }

    private static List<string> ParseMicrosoft365Warnings(string configuration)
    {
        _ = DscKeywordRegistry.RegisterFromSchemaCache(Microsoft365SchemaCacheEntries());

        List<string> warnings = [];
        DscParser.WarningSink = warnings.Add;

        _ = DscParser.ConvertToDscObject(
            content: configuration,
            options: new DscParseOptions { UseRegisteredKeywords = true },
            dscResources: Microsoft365ResourceDefinitions());

        return warnings;
    }

    private static void Register()
    {
        _ = DscKeywordRegistry.RegisterFromSchemaCache(SchemaCacheEntries());
    }

    private static List<object> ResourceDefinitions()
    {
        DscResourceInfo policy = new() { Name = ResourceKeyword, ResourceType = ResourceKeyword };
        policy.AddProperty(new DscResourcePropertyInfo { Name = "DisplayName", PropertyType = "[string]", IsMandatory = true });

        DscResourceInfo assignment = new() { Name = CimKeyword, ResourceType = CimKeyword };
        assignment.AddProperty(new DscResourcePropertyInfo { Name = "Target", PropertyType = "[string]" });

        return [policy, assignment];
    }

    private static List<object> SchemaCacheEntries() =>
    [
        Entry(ResourceKeyword, "NameRequired", new Dictionary<string, string[]>
        {
            ["DisplayName"] = ["String"],
            ["Enabled"] = ["Boolean"],
            ["Threshold"] = ["UInt32"],
            ["Tags"] = ["StringArray"],
            ["Assignments"] = ["ContosoAssignment[]"],
            ["Ensure"] = ["String", "Present", "Absent"],
        }),
        Entry(CimKeyword, "NoName", new Dictionary<string, string[]>
        {
            ["Target"] = ["String"],
            ["Scope"] = ["String"],
        }),
    ];

    private static List<object> Microsoft365ResourceDefinitions() =>
    [
        new DscResourceInfo { Name = "IntuneSettingCatalogCustomPolicyWindows10", ResourceType = "IntuneSettingCatalogCustomPolicyWindows10" },
        new DscResourceInfo { Name = "IntuneAppConfigurationDevicePolicy", ResourceType = "IntuneAppConfigurationDevicePolicy" },
    ];

    private static List<object> Microsoft365SchemaCacheEntries() =>
    [
        Entry("IntuneSettingCatalogCustomPolicyWindows10", "NameRequired", new Dictionary<string, string[]>
        {
            ["ApplicationId"] = ["String"],
            ["Assignments"] = ["MSFT_DeviceManagementConfigurationPolicyAssignments[]"],
            ["CertificateThumbprint"] = ["String"],
            ["Description"] = ["String"],
            ["Ensure"] = ["String", "Present", "Absent"],
            ["Id"] = ["String"],
            ["Name"] = ["String"],
            ["Platforms"] = ["String"],
            ["RoleScopeTagIds"] = ["StringArray"],
            ["Settings"] = ["MSFT_MicrosoftGraphdeviceManagementConfigurationSetting[]"],
            ["Technologies"] = ["String"],
            ["TenantId"] = ["String"],
        }),
        Entry("IntuneAppConfigurationDevicePolicy", "NameRequired", new Dictionary<string, string[]>
        {
            ["ApplicationId"] = ["String"],
            ["Assignments"] = ["MSFT_DeviceManagementConfigurationPolicyAssignments[]"],
            ["CertificateThumbprint"] = ["String"],
            ["ConnectedAppsEnabled"] = ["Boolean"],
            ["Description"] = ["String"],
            ["DisplayName"] = ["String"],
            ["Ensure"] = ["String", "Present", "Absent"],
            ["Id"] = ["String"],
            ["RoleScopeTagIds"] = ["StringArray"],
            ["Settings"] = ["MSFT_MicrosoftGraphappConfigurationSettingItem[]"],
            ["TargetedMobileApps"] = ["StringArray"],
            ["TenantId"] = ["String"],
        }),
        Entry("MSFT_DeviceManagementConfigurationPolicyAssignments", "NoName", new Dictionary<string, string[]>
        {
            ["dataType"] = ["String"],
            ["deviceAndAppManagementAssignmentFilterType"] = ["String"],
        }),
        Entry("MSFT_MicrosoftGraphdeviceManagementConfigurationSetting", "NoName", new Dictionary<string, string[]>
        {
            ["SettingInstance"] = ["MSFT_MicrosoftGraphDeviceManagementConfigurationSettingInstance"],
        }),
        Entry("MSFT_MicrosoftGraphDeviceManagementConfigurationSettingInstance", "NoName", new Dictionary<string, string[]>
        {
            ["ChoiceSettingValue"] = ["MSFT_MicrosoftGraphDeviceManagementConfigurationChoiceSettingValue"],
            ["odataType"] = ["String"],
            ["SettingDefinitionId"] = ["String"],
        }),
        Entry("MSFT_MicrosoftGraphDeviceManagementConfigurationChoiceSettingValue", "NoName", new Dictionary<string, string[]>
        {
            ["Value"] = ["String"],
        }),
        Entry("MSFT_MicrosoftGraphappConfigurationSettingItem", "NoName", new Dictionary<string, string[]>
        {
            ["AppConfigKey"] = ["String"],
            ["AppConfigKeyType"] = ["String"],
            ["AppConfigKeyValue"] = ["String"],
        }),
    ];

    private static Hashtable Entry(
        string keyword, string nameMode, Dictionary<string, string[]> properties, params string[] mandatory)
    {
        Hashtable propertyMap = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string name, string[] typeAndValues) in properties)
        {
            propertyMap[name] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = name,
                ["typeConstraint"] = typeAndValues[0],
                ["mandatory"] = mandatory.Contains(name, StringComparer.OrdinalIgnoreCase),
                ["isKey"] = false,
                ["attributes"] = Array.Empty<object>(),
                ["values"] = typeAndValues.Skip(1).Cast<object>().ToArray(),
                ["valueMap"] = typeAndValues.Skip(1)
                    .Select(value => new Hashtable(StringComparer.OrdinalIgnoreCase) { ["key"] = value, ["value"] = value })
                    .Cast<object>()
                    .ToArray(),
            };
        }

        return new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["keyword"] = keyword,
            ["resourceName"] = keyword,
            ["implementingModule"] = "Contoso",
            ["implementingModuleVersion"] = "2.0.0",
            ["nameMode"] = nameMode,
            ["bodyMode"] = "Hashtable",
            ["directCall"] = false,
            ["metaStatement"] = false,
            ["properties"] = propertyMap,
        };
    }
}
