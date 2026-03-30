using Microsoft.Management.Infrastructure;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation.Language;
using System.Text;
using System.Text.RegularExpressions;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using DscResourcePropertyInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourcePropertyInfo;

namespace DSCParser.CSharp
{
    /// <summary>
    /// Main DSC Parser class that converts DSC configurations to/from objects
    /// </summary>
    public static class DscParser
    {
        private static readonly Dictionary<string, CimClass> _cimClasses = new(StringComparer.InvariantCultureIgnoreCase);
        private static readonly Dictionary<string, DscResourceInfo> _dscResources = new(StringComparer.InvariantCultureIgnoreCase);
        private static readonly Dictionary<string, string> _mofSchemas = new(StringComparer.InvariantCultureIgnoreCase);
        private static readonly Dictionary<string, Dictionary<string, DscResourcePropertyInfo>> _resourcePropertyCache = new(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>
        /// Converts a DSC configuration file or content to DSC objects
        /// </summary>
        public static List<DscResourceInstance> ConvertToDscObject(string? path = null, string content = "", DscParseOptions? options = null, List<object>? dscResources = null)
        {
            options ??= new DscParseOptions();

            if (_dscResources.Count == 0 && dscResources == null)
            {
                throw new InvalidOperationException("No DSC resources loaded. Please provide DSC resources to parse the configuration.");
            }

            List<DscResourceInfo> dscResourcesConverted = [];
            if (dscResources is not null)
            {
                dscResources.ForEach(r =>
                {
                    _dscResources[((dynamic)r).Name] = DscResourceInfoMapper.MapPSObjectToResourceInfo(r);
                    dscResourcesConverted.Add(_dscResources[((dynamic)r).Name]);
                });
            }
            else
            {
                dscResourcesConverted = _dscResources.Values.ToList();
            }

            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(content))
            {
                throw new ArgumentException("Either path or content must be provided");
            }

            string dscContent = string.IsNullOrEmpty(content) ? File.ReadAllText(path!) : content;
            string errorPrefix = string.IsNullOrEmpty(path) ? string.Empty : $"{path} - ";

            // Remove module version information
            dscContent = RemoveModuleVersionInfo(dscContent);

            // Initialize CIM classes cache
            // InitializeCimClasses();

            // Parse the DSC configuration using PowerShell AST
            ScriptBlockAst ast = Parser.ParseInput(dscContent, out Token[] tokens, out ParseError[] parseErrors);

            // Check for parse errors
            foreach (ParseError error in parseErrors)
            {
                if (error.Message.Contains("Could not find the module") ||
                    error.Message.Contains("Undefined DSC resource"))
                {
                    Console.WriteLine($"Warning: {errorPrefix}Failed to find module or DSC resource: {error.Message}");
                }
                else
                {
                    throw new InvalidOperationException($"{errorPrefix}Error parsing configuration: {error.Message}");
                }
            }

            // Find the Configuration definition
            if (ast.Find(a => a is ConfigurationDefinitionAst, false) is not ConfigurationDefinitionAst configAst)
            {
                throw new InvalidOperationException("No Configuration definition found in the DSC content");
            }

            // Get modules to load
            List<Dictionary<string, object>> modulesToLoad = GetModulesToLoad(configAst);

            // Initialize DSC resources
            InitializeDscResources(modulesToLoad, dscResourcesConverted);

            // Get resource instances
            List<DscResourceInstance> resourceInstances = GetResourceInstances(configAst, options);

            // Add comment metadata if requested
            List<DscResourceInstance> result = resourceInstances;
            if (options.IncludeComments)
            {
                result = UpdateWithMetadata(tokens, resourceInstances);
            }

            return result;
        }

        /// <summary>
        /// Converts DSC objects back to DSC configuration text
        /// </summary>
        public static string ConvertFromDscObject(IEnumerable<Hashtable> dscResources, int childLevel = 0)
        {
            StringBuilder result = new();
            string[] parametersToSkip = ["ResourceInstanceName", "CIMInstance"];
            if (childLevel == 0)
                parametersToSkip = [..parametersToSkip, "ResourceName"];

            string childSpacer = new(' ', childLevel * 4);

            foreach (Hashtable entry in dscResources)
            {
                int longestParameter = entry.Keys.Cast<string>().Max(k => k.Length);

                if (entry.ContainsKey("CIMInstance"))
                {
                    _ = result.AppendLine($"{childSpacer}{entry["CIMInstance"]}{{");
                }
                else if (entry.ContainsKey("ResourceName") && entry.ContainsKey("ResourceInstanceName"))
                {
                    _ = result.AppendLine($"{childSpacer}{entry["ResourceName"]} \"{entry["ResourceInstanceName"]}\"");
                    _ = result.AppendLine($"{childSpacer}{{");
                }
                else
                {
                    _ = result.AppendLine($"{childSpacer}@{{");
                }

                List<string> sortedKeys = [.. entry.Keys.Cast<string>().OrderBy(k => k)];

                foreach (string property in sortedKeys)
                {
                    if (parametersToSkip.Contains(property)) continue;

                    string additionalSpaces = new(' ', longestParameter - property.Length + 1);
                    object value = entry[property];

                    _ = result.Append(FormatProperty(property, value, additionalSpaces, childSpacer));
                }

                _ = result.Append($"{childSpacer}}}");
                _ = result.Append(Environment.NewLine);
            }

            return result.ToString();
        }

        private static string RemoveModuleVersionInfo(string content)
        {
            int start = content.IndexOf("import-dscresource", StringComparison.CurrentCultureIgnoreCase);
            if (start >= 0)
            {
                int end = content.IndexOf("\n", start);
                if (end > start)
                {
                    start = content.IndexOf("-moduleversion", start, StringComparison.CurrentCultureIgnoreCase);
                    if (start >= 0 && start < end)
                    {
                        content = content.Remove(start, end - start);
                    }
                }
            }
            return content;
        }

        private static void InitializeCimClasses()
        {
            try
            {
                using CimSession session = CimSession.Create(null);
                IEnumerable<CimClass> classes = session.EnumerateClasses("ROOT/Microsoft/Windows/DesiredStateConfiguration");
                foreach (CimClass? cimClass in classes)
                {
                    if (!_cimClasses.ContainsKey(cimClass.CimSystemProperties.ClassName))
                    {
                        _cimClasses.Add(cimClass.CimSystemProperties.ClassName, cimClass);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not enumerate CIM classes: {ex.Message}");
            }
        }

        private static List<Dictionary<string, object>> GetModulesToLoad(ConfigurationDefinitionAst configAst)
        {
            List<Dictionary<string, object>> modulesToLoad = [];
            ReadOnlyCollection<StatementAst> statements = configAst.Body.ScriptBlock.EndBlock.Statements;

            foreach (CommandAst statement in statements.OfType<CommandAst>())
            {
                if (statement.GetCommandName() == "Import-DSCResource")
                {
                    Dictionary<string, object> currentModule = [];
                    for (int i = 0; i < statement.CommandElements.Count; i++)
                    {
                        if (statement.CommandElements[i] is CommandParameterAst param)
                        {
                            if (param.ParameterName == "ModuleName" && i + 1 < statement.CommandElements.Count)
                            {
                                if (statement.CommandElements[i + 1] is StringConstantExpressionAst moduleName)
                                {
                                    currentModule["ModuleName"] = moduleName.Value;
                                }
                            }
                            else if (param.ParameterName == "ModuleVersion" && i + 1 < statement.CommandElements.Count)
                            {
                                if (statement.CommandElements[i + 1] is StringConstantExpressionAst moduleVersion)
                                {
                                    currentModule["ModuleVersion"] = moduleVersion.Value;
                                }
                            }
                        }
                    }

                    if (currentModule.Count > 0)
                    {
                        modulesToLoad.Add(currentModule);
                    }
                }
            }

            return modulesToLoad;
        }

        private static void InitializeDscResources(List<Dictionary<string, object>> modulesToLoad, List<DscResourceInfo> allDscResources)
        {
            allDscResources.Where(r =>
                modulesToLoad.Any(m =>
                    m.ContainsKey("ModuleName") &&
                    r.Module.Name.Equals(m["ModuleName"].ToString(), StringComparison.OrdinalIgnoreCase) &&
                    (!m.ContainsKey("ModuleVersion") ||
                     r.Module.Version.Equals(new(m["ModuleVersion"].ToString())))
                )
            ).ToList().ForEach(r =>
            {
                if (_dscResources.ContainsKey(r.Name)) return;
                _dscResources.Add(r.Name, r);
            });
        }

        private static List<DscResourceInstance> GetResourceInstances(ConfigurationDefinitionAst configAst, DscParseOptions? options = null)
        {
            // Try to find Node statement first
            DynamicKeywordStatementAst? dynamicNodeStatement = configAst.Body.ScriptBlock.EndBlock.Statements
                .Where(ast => ast is DynamicKeywordStatementAst dynAst &&
                        dynAst.CommandElements[0] is StringConstantExpressionAst constant &&
                        constant.StringConstantType == StringConstantType.BareWord &&
                        constant.Value.Equals("Node", StringComparison.CurrentCultureIgnoreCase))
                .Select(ast => (DynamicKeywordStatementAst)ast)
                .FirstOrDefault() ?? throw new InvalidOperationException("No Node statement found in the DSC configuration");

            List<DscResourceInstance> result = [];

            ScriptBlockExpressionAst nodeBody = dynamicNodeStatement.CommandElements[2] as ScriptBlockExpressionAst
                ?? throw new InvalidOperationException("Failed to parse Node body in DSC configuration.");
            NamedBlockAst? scriptBlockBody = nodeBody.ScriptBlock.Find(ast => ast is NamedBlockAst, false) as NamedBlockAst
                ?? throw new InvalidOperationException("Failed to parse Node body statements in DSC configuration.");
            ReadOnlyCollection<StatementAst> resourceInstancesInNode = scriptBlockBody.Statements;

            foreach (DynamicKeywordStatementAst resource in resourceInstancesInNode.Cast<DynamicKeywordStatementAst>())
            {
                DscResourceInstance currentResourceInfo = new();
                Dictionary<string, object?> currentResourceProperties = [];

                // CommandElements
                // 0 - Resource Type
                // 1 - Resource Instance Name
                // 2 - Key/Pair Value list of parameters.
                string resourceType = resource.CommandElements[0].ToString();
                string resourceInstanceName = string.Empty;
                if (resource.CommandElements[1] is StringConstantExpressionAst resourceInstanceNameAst)
                {
                    resourceInstanceName = resourceInstanceNameAst.Value;
                }
                else if (resource.CommandElements[1] is ExpandableStringExpressionAst resourceInstanceNameExpAst)
                {
                    resourceInstanceName = resourceInstanceNameExpAst.Value;
                }
                else
                {
                    throw new InvalidOperationException("Failed to parse resource instance name in DSC configuration.");
                }

                currentResourceInfo.ResourceName = resourceType;
                currentResourceInfo.ResourceInstanceName = resourceInstanceName;

                // Get reference to the current resource
                DscResourceInfo currentResource = _dscResources[resourceType];

                // Create property lookup hashtable for this resource type if not already cached
                if (!_resourcePropertyCache.ContainsKey(resourceType))
                {
                    Dictionary<string, DscResourcePropertyInfo> propertyLookup = new(StringComparer.CurrentCultureIgnoreCase);
                    foreach (DscResourcePropertyInfo property in currentResource.PropertiesAsResourceInfo)
                    {
                        propertyLookup[property.Name] = property;
                    }
                    _resourcePropertyCache[resourceType] = propertyLookup;
                }
                Dictionary<string, DscResourcePropertyInfo>? resourcePropertyLookup = _resourcePropertyCache[resourceType];

                foreach (Tuple<ExpressionAst, StatementAst> keyValuePair in ((HashtableAst)resource.CommandElements[2]).KeyValuePairs)
                {
                    string key = keyValuePair.Item1.ToString();
                    object? value = null;

                    // Retrieve the current property's type based on the resource's schema.
                    DscResourcePropertyInfo currentPropertyInResourceSchema = resourcePropertyLookup[key];
                    string valueType = currentPropertyInResourceSchema.PropertyType;

                    // Process every kind of property except single CIM instance assignments like:
                    // PsDscRunAsCredential = MSFT_Credential{
                    //    UserName = $ConfigurationData.NonNodeData.AdminUserName
                    //    Password = $ConfigurationData.NonNodeData.AdminPassword
                    // };
                    if (keyValuePair.Item2 is PipelineAst pip)
                    {
                        value = ProcessPipelineAst(pip, resourceType, options?.IncludeCIMInstanceInfo ?? true);
                    }
                    else if (keyValuePair.Item2 is DynamicKeywordStatementAst dynamicStatement)
                    {
                        value = ProcessDynamicKeywordStatementAst(dynamicStatement, resourceType, options?.IncludeCIMInstanceInfo ?? true);
                    }
                    currentResourceProperties.Add(key, value!);
                }

                currentResourceInfo.Properties = currentResourceProperties;
                result.Add(currentResourceInfo);
            }

            return result;
        }

        private static object? ProcessPipelineAst(PipelineAst pip, string resourceName, bool includeCimInstanceInfo)
        {
            // CommandExpressionAst is for Strings, Integers, Arrays, Variables, the "basic" types in a PowerShell DSC configuration
            if (pip.PipelineElements[0] is not CommandExpressionAst expr)
            {
                // CommandAst is for "complex" objects like CIMInstances, e.g. PsDscRunAsCredential or commands like New-Object System.Management.Automation.PSCredential('Password', (ConvertTo-SecureString ((New-Guid).ToString()) -AsPlainText -Force));
                CommandAst ast = pip.PipelineElements[0] as CommandAst ?? throw new InvalidOperationException("Unexpected AST structure in DSC configuration parsing.");
                return ProcessCommandAst(ast, resourceName, includeCimInstanceInfo).Item2;
            }

            return expr.Expression is not null
                ? ProcessCommandExpressionAst(expr, resourceName, includeCimInstanceInfo)
                : pip.Parent.ToString();
        }

        private static (string, object?) ProcessCommandAst(CommandAst commandAst, string resourceName, bool includeCimInstanceInfo)
        {
            Dictionary<string, object?> result = [];
            ReadOnlyCollection<CommandElementAst>? elements = commandAst.CommandElements;

            // A single CIM instance is defined as a CommandAst with a ScriptBlockExpressionAst body
            if (elements.Count >= 2)
            {
                ScriptBlockExpressionAst? cimInstanceBody = elements.Count is 2 or 3
                    ? elements[1] as ScriptBlockExpressionAst
                    : elements[elements.Count - 1] as ScriptBlockExpressionAst;

                if (cimInstanceBody is not null)
                {
                    StringConstantExpressionAst? cimInstanceNameExpression = elements.Count is 2 or 3
                        ? elements[0] as StringConstantExpressionAst
                        : elements[elements.Count - 2] as StringConstantExpressionAst;

                    string cimInstanceName = cimInstanceNameExpression is not null
                    ? cimInstanceNameExpression.Value
                    : throw new InvalidOperationException("CIM Instance name not found in DSC configuration.");

                    if (includeCimInstanceInfo)
                    {
                        result.Add("CIMInstance", cimInstanceName);
                    }

                    // Each line in the script block (the contents of the scriptblock is defined as a "NamedBlockAst") is a PipelineAst
                    ReadOnlyCollection<StatementAst> propertyStatementsInCimInstanceBody = cimInstanceBody.ScriptBlock.EndBlock.Statements;
                    foreach (StatementAst statement in propertyStatementsInCimInstanceBody)
                    {
                        PipelineAst pipelineAst = statement as PipelineAst
                            ?? throw new InvalidOperationException("Failed to parse as pipeline statement in CIM instance scriptblock.");

                        CommandAst propertyStatement = pipelineAst.PipelineElements[0] as CommandAst
                            ?? throw new InvalidOperationException("Failed to parse property statement in CIM instance scriptblock.");

                        // Evaluate each property assignment
                        (string, object?) res = ProcessCommandAst(propertyStatement, resourceName, includeCimInstanceInfo);
                        result.Add(res.Item1, res.Item2);
                    }

                    string propertyName = string.Empty;
                    // If the CIM instance is part of a property assignment, the property name is the first element
                    // This is the same logic as below, but simplified. We assume it is a property assignment if there are more than 3 elements
                    if (elements.Count > 3)
                    {
                        propertyName = ((StringConstantExpressionAst)elements[0]).Value;
                    }
                    return (propertyName, result);
                }

                // If however it is a property assignment inside of a CIM instance, it can either be a StringConstantExpression with the value "="
                // Example: PsDscRunAsCredential = MSFT_Credential{
                //             UserName = $ConfigurationData.NonNodeData.AdminUserName <-- This is such a thing
                //             Password = $ConfigurationData.NonNodeData.AdminPassword <-- And this is one too
                //          };
                // Or it can be a real command expression. If the cound is equal to 3 and the second element is an equal sign, then it is a property assignment
                // In the other cases, we treat is a command execution
                ConstantExpressionAst assignmentOperator = elements[1] as ConstantExpressionAst
                ?? throw new InvalidOperationException($"Failed to find a matching type for statement '{commandAst}'.");

                if (assignmentOperator.Value.Equals("="))
                {
                    StringConstantExpressionAst key = (StringConstantExpressionAst)elements[0];
                    return (key.Value, ProcessExpressionAst((ExpressionAst)elements[2], resourceName, includeCimInstanceInfo));
                }

                return ("", commandAst.ToString());
            }

            return ("", commandAst.ToString());
        }

        private static object ProcessExpressionAst(ExpressionAst expr, string resourceName, bool includeCimInstanceInfo)
        {
            return expr switch
            {
                // A variable like $varName. Is either a normal variable or $true/$false
                VariableExpressionAst variable => ProcessVariableExpressionAst(variable),
                // A constant like "stringValue" or 123
                ConstantExpressionAst constant => ProcessConstantExpressionAst(constant),
                // A member of an object like $obj.Property. Used for configuration data, e.g. $ConfigurationData.NonNodeData.ApplicationId
                MemberExpressionAst member => ProcessMemberExpressionAst(member),
                // An array like @("value1", "value2")
                ArrayExpressionAst array => ProcessArrayExpressionAst(array, resourceName, includeCimInstanceInfo),
                // An expandable string like "https://$OrganizationName/"
                ExpandableStringExpressionAst expString => expString.Value,
                // A hashtable like @{key=value; key2=value2}
                HashtableAst hashtable => ProcessHashtableExpressionAst(hashtable),
                _ => expr.ToString()
            };
        }

        private static object ProcessCommandExpressionAst(CommandExpressionAst expr, string resourceName, bool includeCimInstanceInfo)
        {
            return ProcessExpressionAst(expr.Expression, resourceName, includeCimInstanceInfo);
        }

        private static List<object> ProcessArrayExpressionAst(ArrayExpressionAst arrayAst, string resourceName, bool includeCimInstanceInfo)
        {
            StatementBlockAst arrayDefinition = arrayAst.SubExpression;

            if (arrayDefinition.Statements.Count == 0)
            {
                return [];
            }

            // Arrays can contain strings, integers, variables, and CIM instances
            // Strings, integers and variables are represented as a PipelineAst
            PipelineAst? firstArrayValue = arrayDefinition.Statements[0] as PipelineAst;
            if (firstArrayValue is not null)
            {
                List<object> returnList = [];
                foreach (PipelineAst pipelineArrayValue in arrayDefinition.Statements.Cast<PipelineAst>())
                {
                    if (pipelineArrayValue.PipelineElements[0] is not CommandExpressionAst arrayElementDefinition)
                    {
                        // Complex array items, defined e.g. for Intune assignments
                        // Assignments = @(
                        //     MSFT_DeviceManagementManagedGooglePlayMobileAppAssignment{
                        //         groupDisplayName = "AADGroup_10"
                        //         deviceAndAppManagementAssignmentFilterType = "none"
                        //         dataType = "#microsoft.graph.groupAssignmentTarget"
                        //         intent = "required"
                        //         assignmentSettings = MSFT_DeviceManagementManagedGooglePlayMobileAppAssignmentSettings{
                        //             odataType = "#microsoft.graph.androidManagedStoreAppAssignmentSettings"
                        //             autoUpdateMode = "priority"
                        //         }
                        //     }
                        // );
                        (string, object?) complexArrayItemTuple = ProcessCommandAst((CommandAst)pipelineArrayValue.PipelineElements[0], resourceName, includeCimInstanceInfo);
                        returnList.Add(complexArrayItemTuple.Item2);
                        continue;
                    }
                    switch (arrayElementDefinition.Expression)
                    {
                        // Array literals are arrays of strings like @("value1", "value2"), integers like @(1,2,3)
                        // variables like @($var1, $var2), expandable strings like @("https://$OrganizationName/", "https://$TenantGuid/")
                        // or more types of elements
                        case ArrayLiteralAst arrayLiteral:
                            {
                                return arrayLiteral.Elements
                                    .Select(element => ProcessExpressionAst(element, resourceName, includeCimInstanceInfo))
                                    .ToList();
                            }
                        // Any other type of expression inside the array
                        case ExpressionAst expression:
                            returnList.Add(ProcessExpressionAst(expression, resourceName, includeCimInstanceInfo));
                            break;
                        default:
                            break;
                    }
                }
                return returnList;
            }

            // Arrays containing CIM instances are represented as DynamicKeywordStatementAst
            List<object> arrayCimInstances = [];
            foreach (DynamicKeywordStatementAst arrayCimInstance in arrayDefinition.Statements.Cast<DynamicKeywordStatementAst>())
            {
                arrayCimInstances.Add(ProcessDynamicKeywordStatementAst(arrayCimInstance, resourceName, includeCimInstanceInfo));
            }
            return arrayCimInstances;
        }

        private static Dictionary<string, object?> ProcessDynamicKeywordStatementAst(
            DynamicKeywordStatementAst commandAst,
            string resourceName,
            bool includeCimInstanceInfo)
        {
            ReadOnlyCollection<CommandElementAst>? elements = commandAst.CommandElements;

            // Process in groups of 3: CIMInstanceName, dash, Hashtable
            Dictionary<string, object?> currentResult = [];

            if (elements[0] is StringConstantExpressionAst cimInstanceNameAst &&
                elements[2] is HashtableAst hashtableAst)
            {
                string cimInstanceName = cimInstanceNameAst.Value;

                // Get CIM class properties
                // CimClass cimClass = GetCimClass(cimInstanceName, resourceName);

                if (includeCimInstanceInfo)
                {
                    currentResult["CIMInstance"] = cimInstanceName;
                }

                foreach (Tuple<ExpressionAst, StatementAst> kvp in hashtableAst.KeyValuePairs)
                {
                    string key = kvp.Item1.ToString().Trim('"', '\'');

                    object? value = null;
                    if (kvp.Item2 is PipelineAst pip)
                    {
                        value = ProcessPipelineAst(pip, resourceName, includeCimInstanceInfo);
                    }
                    else if (kvp.Item2 is DynamicKeywordStatementAst dynamicStatement)
                    {
                        value = ProcessDynamicKeywordStatementAst(dynamicStatement, resourceName, includeCimInstanceInfo);
                    }
                    currentResult[key] = value;
                }
            }

            return currentResult;
        }

        private static object ProcessVariableExpressionAst(VariableExpressionAst variableAst)
        {
            if (variableAst.ToString().Equals("$true", StringComparison.InvariantCultureIgnoreCase) ||
                variableAst.ToString().Equals("$false", StringComparison.InvariantCultureIgnoreCase))
            {
                return bool.Parse(variableAst.ToString().TrimStart('$'));
            }

            return variableAst.ToString();
        }

        private static object ProcessConstantExpressionAst(ConstantExpressionAst constantAst) => constantAst.Value;

        private static string ProcessMemberExpressionAst(MemberExpressionAst memberAst) => memberAst.ToString();

        private static Hashtable ProcessHashtableExpressionAst(HashtableAst hashtableAst)
        {
            Hashtable result = [];
            foreach (Tuple<ExpressionAst, StatementAst> kvp in hashtableAst.KeyValuePairs)
            {
                string key = kvp.Item1.ToString();
                object? value = null;
                if (kvp.Item2 is PipelineAst pip)
                {
                    value = ProcessPipelineAst(pip, "", true);
                }
                else if (kvp.Item2 is DynamicKeywordStatementAst dynamicStatement)
                {
                    value = ProcessDynamicKeywordStatementAst(dynamicStatement, "", true);
                }
                result[key] = value;
            }
            return result;
        }

        private static List<DscResourceInstance> UpdateWithMetadata(Token[] tokens, List<DscResourceInstance> parsedObjects)
        {
            // Find Node token position
            int tokenPositionOfNode = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Kind == TokenKind.DynamicKeyword && tokens[i].Text == "Node")
                {
                    tokenPositionOfNode = i;
                    break;
                }
            }

            // Process comments after Node
            for (int i = tokenPositionOfNode; i < tokens.Length; i++)
            {
                if (tokens[i].Kind is TokenKind.Comment)
                {
                    int stepback = 1;
                    while (tokens[i - stepback].Kind is not TokenKind.DynamicKeyword)
                    {
                        stepback++;
                    }

                    string commentResourceType = tokens[i - stepback].Text;
                    StringExpandableToken resourceInstanceName = tokens[i - stepback + 1] as StringExpandableToken
                        ?? throw new InvalidOperationException($"Failed to find corresponding resource for comment {tokens[i].Text}");
                    string commentResourceInstanceName = resourceInstanceName.Value;

                    // Backtrack to find associated property
                    stepback = 0;
                    while (tokens[i - stepback].Kind is not TokenKind.Identifier and not TokenKind.NewLine)
                    {
                        stepback++;
                    }

                    if (tokens[i - stepback].Kind is TokenKind.Identifier)
                    {
                        string commentAssociatedProperty = tokens[i - stepback].Text;

                        // Loop through all instances in the ParsedObject to retrieve
                        // the one associated with the comment
                        for (int j = 0; j < parsedObjects.Count; j++)
                        {
                            if (parsedObjects[j].ResourceName.Equals(commentResourceType) &&
                                parsedObjects[j].ResourceInstanceName.Equals(commentResourceInstanceName) &&
                                parsedObjects[j].Properties.ContainsKey(commentAssociatedProperty))
                            {
                                parsedObjects[j].AddProperty($"_metadata_{commentAssociatedProperty}", tokens[i].Text);
                            }
                        }
                    }
                }
            }

            return parsedObjects;
        }

        private static string FormatProperty(string property, object? value, string additionalSpaces, string childSpacer)
        {
            StringBuilder result = new();

            switch (value)
            {
                case string strValue:
                    // If the string starts with a $ and does not contain any spaces, we treat it as a variable and do not wrap it in quotes
                    if (strValue.StartsWith("$") && !strValue.Contains(' '))
                    {
                        _ = result.AppendLine($"{childSpacer}    {property}{additionalSpaces}= {strValue}");
                    }
                    else if (strValue.StartsWith("New-Object"))
                    {
                        _ = result.AppendLine($"{childSpacer}    {property}{additionalSpaces}= {strValue.TrimStart('"').TrimEnd('"')}");
                    }
                    else
                    {
                        string escaped = strValue.Replace("`", "``").Replace("\"", "`\"");
                        _ = result.AppendLine($"{childSpacer}    {property}{additionalSpaces}= \"{escaped}\"");
                    }
                    break;

                case int intValue:
                    _ = result.AppendLine($"{childSpacer}    {property}{additionalSpaces}= {intValue}");
                    break;

                case bool boolValue:
                    _ = result.AppendLine($"{childSpacer}    {property}{additionalSpaces}= ${boolValue}");
                    break;

                case Array arrayValue:
                    _ = result.Append($"{childSpacer}    {property}{additionalSpaces}= @(");
                    // If no elements are provided, close the array immediately
                    if (arrayValue.Length == 0)
                    {
                        _ = result.AppendLine(")");
                        break;
                    }

                    List<string> arrayItemsAsString = [];
                    bool isSimpleArray = true;
                    foreach (object item in arrayValue)
                    {
                        if (item is string or int or bool)
                        {
                            arrayItemsAsString.Add(item is string s ? $"\"{s.Replace("`", "``").Replace("\"", "`\"")}\"" : item.ToString());
                        }
                        else if (item is Hashtable ht)
                        {
                            string converted = ConvertFromDscObject([ht], (childSpacer.Length / 4) + 2);
                            arrayItemsAsString.Add(converted.TrimEnd());
                            isSimpleArray = false;
                        }
                        else
                        {
                            arrayItemsAsString.Add($"{new string(' ', childSpacer.Length + 8)}{item.ToString() ?? string.Empty}");
                        }
                    }

                    if (isSimpleArray && arrayItemsAsString.Count == 1)
                    {
                        _ = result.Append(arrayItemsAsString[0]);
                        _ = result.AppendLine(")");
                    }
                    else
                    {
                        _ = result.AppendLine();
                        _ = result.AppendLine(string.Join(Environment.NewLine, arrayItemsAsString.Select(line =>
                        {
                            if (!line.StartsWith(new string(' ', childSpacer.Length + 8)))
                            {
                                return $"{new string(' ', childSpacer.Length + 8)}{line.TrimStart()}";
                            }
                            return line;
                        })));
                        _ = result.AppendLine($"{childSpacer}    )");
                    }
                    break;

                case Hashtable hashtable:
                    _ = result.Append($"{childSpacer}    {property}{additionalSpaces}= ");
                    if (hashtable.ContainsKey("CIMInstance") || hashtable.ContainsKey("ResourceInstanceName"))
                    {
                        ConvertFromDscObject([hashtable], (childSpacer.Length / 4) + 1)
                            .Split([Environment.NewLine], StringSplitOptions.None)
                            .Where(line => !string.IsNullOrWhiteSpace(line))
                            .ToList()
                            .ForEach(line =>
                            {
                                // Trim the first spaces to align properly for only the first declaration
                                string regex = $"^\\s{{{childSpacer.Length}}}\\s{{4}}\\w*{{";
                                if (Regex.IsMatch(line, regex))
                                    line = line.Replace(childSpacer + "    ", "");
                                _ = result.AppendLine(line);
                            });
                    }
                    else
                    {
                        _ = result.Append("@");
                        List<string> lines = ConvertFromDscObject([hashtable], childSpacer.Length / 4 + 1)
                            .Split([Environment.NewLine], StringSplitOptions.None)
                            .Where(line => !string.IsNullOrWhiteSpace(line))
                            .ToList();
                        lines[0] = lines[0].TrimStart();
                        lines.ForEach(line => _ = result.AppendLine(line));
                    }
                    break;

                default:
                    if (value != null)
                    {
                        _ = result.AppendLine($"{childSpacer}    {property}{additionalSpaces}= {value}");
                    }
                    break;
            }

            return result.ToString();
        }
    }
}
