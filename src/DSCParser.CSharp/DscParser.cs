using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DSCParser.PSDSC;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using DscResourcePropertyInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourcePropertyInfo;

namespace DSCParser.CSharp
{
    /// <summary>
    /// Main DSC Parser class that converts DSC configurations to/from objects
    /// </summary>
    public static class DscParser
    {
        private static readonly Dictionary<string, DscResourceInfo> _dscResources = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex ImportDscResourceVersionRegex = new(
            @"(import-dscresource\b[^\n]*?)\s+-moduleversion\s+(?:""[^""]*""|'[^']*'|\S+)([^\n]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ImportDscResourceStatementRegex = new(
            @"^[ \t]*import-dscresource\b([^\r\n]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Compiled);

        private const string ImportDscResourcePlaceholder = "DscParserImportDscResource";

        private const string MetadataPrefix = "_metadata_";

        // Matches a string that is syntactically nothing but a variable reference,
        // optionally scoped and with member access: $name, $scope:name, $config.Credential.
        // Anything else that merely starts with '$' (e.g. "$OrganizationName\Default")
        // must be emitted quoted or the generated text does not reparse.
        private static readonly Regex BareVariableReferenceRegex = new(
            @"^\$(?:\w+:)?\w+(?:\.\w+)*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Receives non-fatal diagnostics such as unresolvable modules. When unset, they are dropped
        /// rather than written to the console, which would corrupt a PowerShell host's output stream.
        /// </summary>
        public static Action<string>? WarningSink { get; set; }

        /// <summary>
        /// Clears the process-wide resource, property and module caches.
        /// </summary>
        public static void ClearCaches()
        {
            _dscResources.Clear();
            DscKeywordRegistry.Reset();
        }

        private static void ReportWarning(string message) => WarningSink?.Invoke(message);

        // A configuration exported by an older module version references resources and properties the
        // installed version no longer has. Warn and convert what is left instead of failing outright.
        private static readonly HashSet<string> RecoverableParseErrorIds = new(StringComparer.Ordinal)
        {
            "ResourceNotDefined",
            "InvalidInstanceProperty",
            DscSchemaCacheKeywords.MissingMandatoryPropertyErrorId
        };

        private static readonly PropertyInfo? StatementKeywordProperty =
            typeof(DynamicKeywordStatementAst).GetProperty("Keyword", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static bool IsRecoverableParseError(ParseError error)
        {
            return RecoverableParseErrorIds.Contains(error.ErrorId) ||
                   error.Message.Contains("Could not find the module") ||
                   error.Message.Contains("Undefined DSC resource");
        }

        private static string DescribeParseError(ParseError error)
        {
            // The raw message lists every valid member, which is too noisy to surface.
            return error.ErrorId switch
            {
                "InvalidInstanceProperty" =>
                    $"Property '{error.Extent.Text}' (line {error.Extent.StartLineNumber}) does not exist on its resource in the installed module version.",
                DscSchemaCacheKeywords.MissingMandatoryPropertyErrorId =>
                    $"{error.Message} (line {error.Extent.StartLineNumber})",
                _ => error.Message
            };
        }

        /// <summary>
        /// The errors a statement reports for each missing mandatory property become one warning.
        /// </summary>
        private static void ReportParseError(
            ParseError error, Ast? root, string errorPrefix, Dictionary<int, string?> describedStatements)
        {
            if (!IsRecoverableParseError(error))
            {
                throw new InvalidOperationException($"{errorPrefix}Error parsing configuration: {error.Message}");
            }

            if (error.ErrorId.Equals(DscSchemaCacheKeywords.MissingMandatoryPropertyErrorId, StringComparison.Ordinal) &&
                root?.Find(ast => IsKeywordStatementAt(ast, error.Extent), searchNestedScriptBlocks: true) is DynamicKeywordStatementAst statement)
            {
                if (!describedStatements.TryGetValue(statement.Extent.StartOffset, out string? description))
                {
                    description = DescribeMissingMandatoryProperties(statement);
                    describedStatements[statement.Extent.StartOffset] = description;

                    if (description is not null)
                    {
                        ReportWarning(errorPrefix + description);
                    }
                }

                if (description is not null)
                {
                    return;
                }
            }

            ReportWarning($"{errorPrefix}{DescribeParseError(error)}");
        }

        private static bool IsKeywordStatementAt(Ast ast, IScriptExtent extent)
        {
            return ast is DynamicKeywordStatementAst { CommandElements.Count: > 0 } statement &&
                   statement.CommandElements[0].Extent.StartOffset == extent.StartOffset &&
                   statement.CommandElements[0].Extent.EndOffset == extent.EndOffset;
        }

        private static string? DescribeMissingMandatoryProperties(DynamicKeywordStatementAst statement)
        {
            if (StatementKeywordProperty?.GetValue(statement) is not DynamicKeyword keyword)
            {
                return null;
            }

            HashSet<string> present = new(
                statement.CommandElements.OfType<HashtableAst>()
                    .SelectMany(body => body.KeyValuePairs)
                    .Select(pair => pair.Item1)
                    .OfType<StringConstantExpressionAst>()
                    .Select(key => key.Value),
                StringComparer.OrdinalIgnoreCase);

            List<string> missing = keyword.Properties
                .Where(property => property.Value.Mandatory && !present.Contains(property.Key))
                .Select(property => property.Key)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => $"'{name}'")
                .ToList();

            if (missing.Count == 0)
            {
                return null;
            }

            string properties = missing.Count == 1
                ? $"property {missing[0]}"
                : $"properties {string.Join(", ", missing)}";
            string keywordName = statement.CommandElements[0].Extent.Text;
            int line = statement.Extent.StartLineNumber;

            DynamicKeywordStatementAst? resource = FindEnclosingKeywordStatement(statement);
            if (resource is null)
            {
                return ReadInstanceName(statement) is { } instance
                    ? $"Resource '{keywordName}' (instance '{instance}', line {line}) is missing the mandatory {properties}."
                    : $"Resource '{keywordName}' (line {line}) is missing the mandatory {properties}.";
            }

            return $"'{keywordName}' (line {line}) in resource '{resource.CommandElements[0].Extent.Text}' " +
                   $"(instance '{ReadInstanceName(resource) ?? string.Empty}') is missing the mandatory {properties}.";
        }

        private static DynamicKeywordStatementAst? FindEnclosingKeywordStatement(Ast ast)
        {
            DynamicKeywordStatementAst? outermost = null;
            for (Ast? parent = ast.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is DynamicKeywordStatementAst { CommandElements.Count: 3 } statement &&
                    statement.CommandElements[2] is HashtableAst &&
                    ReadInstanceName(statement) is not null)
                {
                    outermost = statement;
                }
            }

            return outermost;
        }

        private static string? ReadInstanceName(DynamicKeywordStatementAst statement)
        {
            if (statement.CommandElements.Count < 3)
            {
                return null;
            }

            return statement.CommandElements[1] switch
            {
                StringConstantExpressionAst constant => constant.Value,
                ExpandableStringExpressionAst expandable => expandable.Value,
                _ => null
            };
        }

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

            List<DscResourceInfo> dscResourcesConverted;
            if (dscResources is not null)
            {
                dscResourcesConverted = new List<DscResourceInfo>(dscResources.Count);
                foreach (object resource in dscResources)
                {
                    DscResourceInfo mapped = DscResourceInfoMapper.MapPSObjectToResourceInfo(resource);
                    if (string.IsNullOrEmpty(mapped.Name))
                    {
                        throw new InvalidOperationException("A supplied DSC resource has no Name and cannot be used for parsing.");
                    }

                    _dscResources[mapped.Name!] = mapped;
                    dscResourcesConverted.Add(mapped);
                }
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

            if (options.UseRegisteredKeywords)
            {
                return ConvertUsingRegisteredKeywords(dscContent, errorPrefix, options);
            }

            HashSet<string> referencedModules = new(StringComparer.OrdinalIgnoreCase);
            foreach (DscResourceInfo resource in dscResourcesConverted)
            {
                string? moduleName = resource.Module?.Name;
                if (!string.IsNullOrEmpty(moduleName))
                {
                    _ = referencedModules.Add(moduleName!);
                }
            }

            List<string> modulesToRemoveVersionFrom = GetSingleVersionModules(referencedModules);

            dscContent = RemoveModuleVersionInfo(dscContent, modulesToRemoveVersionFrom);

            List<ModuleReference> modulesToLoad = GetModulesToLoad(dscContent);

            // Parse the DSC configuration using PowerShell AST instead of with "Import-DscResource"
            // to avoid loading modules from disk, which may be very slow with many class-based resources.
            // The DynamicKeyword table only exists for the duration of the parse because leaving it populated
            // breaks the engine's own Configuration-to-MOF compilation afterwards.
            ScriptBlockAst ast;
            Token[] tokens;
            ParseError[] parseErrors;
            ConfigurationDefinitionAst? configAst;
            try
            {
                RegisterKeywords(modulesToLoad, errorPrefix);
                DscKeywordRegistry.MaterializeKeywordTable();
                string parseContent = RemoveImportDscResourceStatements(dscContent);
                ast = Parser.ParseInput(parseContent, out tokens, out parseErrors);
                configAst = FindConfigurationAst(ast);

                if (HasFatalParseError(parseErrors, configAst) &&
                    TryReparseWithQuotedValues(parseContent, errorPrefix, hasFatalError: (errors, candidateAst) =>
                        HasFatalParseError(errors, FindConfigurationAst(candidateAst)),
                        out ScriptBlockAst repairedAst, out Token[] repairedTokens, out ParseError[] repairedErrors))
                {
                    ast = repairedAst;
                    tokens = repairedTokens;
                    parseErrors = repairedErrors;
                    configAst = FindConfigurationAst(ast);
                }
            }
            finally
            {
                DscKeywordRegistry.ClearKeywordTable();
            }

            ReportParseErrors(parseErrors, configAst, errorPrefix);

            if (configAst is null)
            {
                throw new InvalidOperationException("No Configuration definition found in the DSC content");
            }

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
        /// Names of the modules the configuration's Import-DscResource statements reference.
        /// </summary>
        public static string[] GetReferencedModuleNames(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return [];
            }

            return GetModulesToLoad(content)
                .Select(reference => reference.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Converts DSC objects back to DSC configuration text
        /// </summary>
        public static string ConvertFromDscObject(IEnumerable<Hashtable> dscResources, int childLevel = 0)
        {
            StringBuilder result = new();
            AppendDscObjects(result, dscResources, childLevel);
            return result.ToString();
        }

        /// <summary>
        /// Renders resources into <paramref name="result"/>. Nested hashtables and arrays recurse into
        /// the same builder, so no intermediate strings are produced per nesting level.
        /// </summary>
        private static void AppendDscObjects(StringBuilder result, IEnumerable<IDictionary> dscResources, int childLevel)
        {
            string childSpacer = new(' ', childLevel * 4);

            foreach (IDictionary entry in dscResources)
            {
                List<string> sortedKeys = [];
                int longestParameter = 0;
                foreach (string key in entry.Keys)
                {
                    if (key.StartsWith(MetadataPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    sortedKeys.Add(key);
                    if (key.Length > longestParameter)
                    {
                        longestParameter = key.Length;
                    }
                }
                sortedKeys.Sort(StringComparer.Ordinal);

                if (entry.Contains("CIMInstance"))
                {
                    _ = result.Append(childSpacer).Append(entry["CIMInstance"]).AppendLine("{");
                }
                else if (entry.Contains("ResourceName") && entry.Contains("ResourceInstanceName"))
                {
                    _ = result.Append(childSpacer).Append(entry["ResourceName"]).Append(" \"").Append(entry["ResourceInstanceName"]).AppendLine("\"");
                    _ = result.Append(childSpacer).AppendLine("{");
                }
                else
                {
                    _ = result.Append(childSpacer).AppendLine("@{");
                }

                foreach (string property in sortedKeys)
                {
                    if (property is "ResourceInstanceName" or "CIMInstance" ||
                        (childLevel == 0 && property is "ResourceName"))
                    {
                        continue;
                    }

                    AppendProperty(result, property, entry[property], longestParameter - property.Length + 1, childSpacer, childLevel);
                }

                _ = result.Append(childSpacer).Append('}').Append(Environment.NewLine);
            }
        }

        private static string RemoveModuleVersionInfo(string content, List<string>? uniqueModules = null)
        {
            if (uniqueModules is null || uniqueModules.Count == 0)
            {
                return content;
            }

            return ImportDscResourceVersionRegex.Replace(content, match =>
            {
                string fullLine = match.Value;
                foreach (string module in uniqueModules)
                {
                    if (fullLine.IndexOf(module, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return match.Groups[1].Value + match.Groups[2].Value;
                    }
                }
                return fullLine;
            });
        }

        /// <summary>
        /// Returns the subset of <paramref name="moduleNames"/> that has exactly one version installed.
        /// Only those may have their -ModuleVersion stripped from the configuration; for modules with
        /// several versions installed the version is what selects the right one.
        /// </summary>
        private static List<string> GetSingleVersionModules(HashSet<string> moduleNames)
        {
            List<string> singleVersionModules = [];
            if (moduleNames.Count == 0)
            {
                return singleVersionModules;
            }

            PSModuleInfo[] installed = PowerShellInvoker.ListAvailableModules(moduleNames);

            foreach (string moduleName in moduleNames)
            {
                HashSet<string> versions = new(StringComparer.OrdinalIgnoreCase);
                foreach (PSModuleInfo module in installed)
                {
                    if (moduleName.Equals(module.Name, StringComparison.OrdinalIgnoreCase) && module.Version is not null)
                    {
                        _ = versions.Add(module.Version.ToString());
                    }
                }

                if (versions.Count <= 1)
                {
                    singleVersionModules.Add(moduleName);
                }
            }

            return singleVersionModules;
        }

        /// <summary>
        /// Reads the Import-DSCResource statements straight from the configuration text, so the
        /// keywords can be registered before the configuration is parsed and the statements removed
        /// from that parse.
        /// </summary>
        private static List<ModuleReference> GetModulesToLoad(string content)
        {
            List<ModuleReference> modulesToLoad = [];

            foreach (Match statement in ImportDscResourceStatementRegex.Matches(content))
            {
                ScriptBlockAst statementAst = Parser.ParseInput(
                    ImportDscResourcePlaceholder + statement.Groups[1].Value, out Token[] _, out ParseError[] _);

                if (statementAst.Find(a => a is CommandAst, true) is not CommandAst command)
                {
                    continue;
                }

                ReadOnlyCollection<CommandElementAst> elements = command.CommandElements;

                string? moduleName = null;
                Version? moduleVersion = null;

                for (int i = 1; i < elements.Count - 1; i++)
                {
                    if (elements[i] is not CommandParameterAst param ||
                        elements[i + 1] is not StringConstantExpressionAst value)
                    {
                        continue;
                    }

                    if (param.ParameterName.Equals("ModuleName", StringComparison.OrdinalIgnoreCase))
                    {
                        moduleName = value.Value;
                    }
                    else if (param.ParameterName.Equals("ModuleVersion", StringComparison.OrdinalIgnoreCase) &&
                             Version.TryParse(value.Value, out Version? parsed))
                    {
                        moduleVersion = parsed;
                    }
                }

                if (moduleName is not null)
                {
                    modulesToLoad.Add(new ModuleReference(moduleName, moduleVersion));
                }
            }

            return modulesToLoad;
        }

        private static string RemoveImportDscResourceStatements(string content)
        {
            return ImportDscResourceStatementRegex.Replace(content, string.Empty);
        }

        private static void RegisterKeywords(List<ModuleReference> modulesToLoad, string errorPrefix)
        {
            foreach (ModuleReference reference in modulesToLoad)
            {
                if (!DscKeywordRegistry.EnsureRegistered(reference.Name, reference.Version))
                {
                    string version = reference.Version is null ? string.Empty : $", {reference.Version}";
                    ReportWarning($"{errorPrefix}Could not find the module '<{reference.Name}{version}>'.");
                }
            }
        }

        private static ConfigurationDefinitionAst? FindConfigurationAst(ScriptBlockAst ast)
        {
            return ast.Find(a => a is ConfigurationDefinitionAst, false) as ConfigurationDefinitionAst;
        }

        private static bool IsOutsideConfiguration(ParseError error, ConfigurationDefinitionAst? configAst)
        {
            return configAst is not null &&
                   (error.Extent.StartOffset < configAst.Extent.StartOffset ||
                    error.Extent.EndOffset > configAst.Extent.EndOffset);
        }

        private static bool HasFatalParseError(ParseError[] parseErrors, ConfigurationDefinitionAst? configAst)
        {
            foreach (ParseError error in parseErrors)
            {
                if (!IsOutsideConfiguration(error, configAst) && !IsRecoverableParseError(error))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Errors outside the configuration block never affected the conversion, and registering the
        /// keywords up front makes PowerShell report some constructs that follow the block, such as the
        /// trailing invocation an export appends, as errors. Only errors inside the block are fatal.
        /// </summary>
        private static void ReportParseErrors(ParseError[] parseErrors, ConfigurationDefinitionAst? configAst, string errorPrefix)
        {
            Dictionary<int, string?> describedStatements = [];
            foreach (ParseError error in parseErrors)
            {
                if (!IsOutsideConfiguration(error, configAst))
                {
                    ReportParseError(error, configAst, errorPrefix, describedStatements);
                }
            }
        }

        /// <summary>
        /// Quotes the unparsable bare values in the content and reparses it, keeping the result only
        /// when it removes every fatal error.
        /// </summary>
        /// <returns>True when the reparsed content replaced the original.</returns>
        private static bool TryReparseWithQuotedValues(
            string content,
            string errorPrefix,
            Func<ParseError[], ScriptBlockAst, bool> hasFatalError,
            out ScriptBlockAst ast,
            out Token[] tokens,
            out ParseError[] parseErrors)
        {
            ast = null!;
            tokens = [];
            parseErrors = [];

            string repaired = QuoteUnparsableBareValues(content, out int repairedCount);
            if (repairedCount == 0)
            {
                return false;
            }

            ScriptBlockAst repairedAst = Parser.ParseInput(repaired, out Token[] repairedTokens, out ParseError[] repairedErrors);
            if (hasFatalError(repairedErrors, repairedAst))
            {
                return false;
            }

            ast = repairedAst;
            tokens = repairedTokens;
            parseErrors = repairedErrors;

            ReportWarning(
                $"{errorPrefix}Quoted {repairedCount} unquoted value(s) that PowerShell cannot parse as an expression, " +
                "such as a GUID substituted for a variable reference. They were converted as strings.");

            return true;
        }

        // Matches a line holding nothing but a single bare token, either as the right-hand side of a
        // property assignment or as a standalone array element. Requiring a single token keeps
        // statements such as "Configuration Name" out of the repair.
        private static readonly Regex BareValueLineRegex = new(
            @"^(?<lead>[ \t]*(?:[A-Za-z_]\w*[ \t]*=[ \t]*)?)(?<value>[^\s""'$@(){}\[\]#`,;|]+)(?<trail>[ \t]*[;,]?[ \t]*\r?)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline);

        private const string BareValueProbePrefix = "$DscParserBareValueProbe = ";

        /// <summary>
        /// Wraps every bare value that is not a valid PowerShell expression in single quotes. Textual
        /// variable substitution can leave a value such as "12345678-1234-1234-ad9c-123456789abc"
        /// unquoted, which PowerShell tokenizes as arithmetic and rejects.
        /// </summary>
        private static string QuoteUnparsableBareValues(string content, out int repairedCount)
        {
            int count = 0;
            string result = BareValueLineRegex.Replace(content, match =>
            {
                string value = match.Groups["value"].Value;

                _ = Parser.ParseInput(BareValueProbePrefix + value, out Token[] _, out ParseError[] probeErrors);
                if (probeErrors.Length == 0)
                {
                    return match.Value;
                }

                count++;
                return $"{match.Groups["lead"].Value}'{value}'{match.Groups["trail"].Value}";
            });

            repairedCount = count;

            return result;
        }

        private readonly struct ModuleReference(string name, Version? version)
        {
            public string Name { get; } = name;

            public Version? Version { get; } = version;
        }

        /// <summary>
        /// Converts a configuration against the keywords the caller registered from a schema cache.
        /// </summary>
        /// <remarks>
        /// The Configuration body is reparsed on its own because the engine empties the DynamicKeyword
        /// table when it enters a Configuration block, which would drop the registered keywords before
        /// any resource inside is reached.
        /// </remarks>
        private static List<DscResourceInstance> ConvertUsingRegisteredKeywords(
            string dscContent, string errorPrefix, DscParseOptions options)
        {
            if (!DscKeywordRegistry.HasSchemaCacheKeywords)
            {
                throw new InvalidOperationException(
                    "No keywords are registered. Call DscKeywordRegistry.RegisterFromSchemaCache before parsing with UseRegisteredKeywords.");
            }

            string content = RemoveImportDscResourceStatements(dscContent);
            string? configurationBody = FindConfigurationBody(content);
            string body = configurationBody ?? content;

            ScriptBlockAst bodyAst;
            Token[] tokens;
            ParseError[] parseErrors;
            try
            {
                DscKeywordRegistry.MaterializeSchemaCacheKeywords();
                bodyAst = Parser.ParseInput(body, out tokens, out parseErrors);

                if (HasFatalParseError(parseErrors, configAst: null) &&
                    TryReparseWithQuotedValues(body, errorPrefix, hasFatalError: (errors, _) =>
                        HasFatalParseError(errors, configAst: null),
                        out ScriptBlockAst repairedAst, out Token[] repairedTokens, out ParseError[] repairedErrors))
                {
                    bodyAst = repairedAst;
                    tokens = repairedTokens;
                    parseErrors = repairedErrors;
                }
            }
            finally
            {
                DscKeywordRegistry.ClearKeywordTable();
            }

            Dictionary<int, string?> describedStatements = [];
            foreach (ParseError error in parseErrors)
            {
                ReportParseError(error, bodyAst, errorPrefix, describedStatements);
            }

            ReadOnlyCollection<StatementAst> statements = bodyAst.EndBlock?.Statements ?? new ReadOnlyCollection<StatementAst>([]);
            List<DscResourceInstance> result = configurationBody is null && !ContainsNodeStatement(statements)
                ? ReadResourceInstances(statements, options)
                : ReadConfigurationStatements(statements, options);

            return options.IncludeComments ? UpdateWithMetadata(tokens, result) : result;
        }

        /// <summary>
        /// Returns the statements of the content's Configuration block as parsable text at their
        /// positions in the content, or null when there is no Configuration block.
        /// </summary>
        private static string? FindConfigurationBody(string content)
        {
            ScriptBlockAst ast = Parser.ParseInput(content, out Token[] tokens, out ParseError[] _);

            return FindConfigurationAst(ast) is { } configAst
                ? ReadBlockContents(content, tokens, configAst.Body.Extent.StartOffset)
                : null;
        }

        private static bool ContainsNodeStatement(ReadOnlyCollection<StatementAst> statements)
        {
            for (int index = 0; index < statements.Count; index++)
            {
                if (TryReadNodeStatement(statements, index, out _, out _, out _) ||
                    ListControlBlocks(statements[index]).Any(block => ContainsNodeStatement(block.Statements)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The statement blocks of an if, loop, switch, try or trap statement, without blocks nested
        /// deeper inside them.
        /// </summary>
        private static IEnumerable<StatementBlockAst> ListControlBlocks(StatementAst statement)
        {
            if (statement is not (IfStatementAst or LoopStatementAst or SwitchStatementAst or TryStatementAst or BlockStatementAst or TrapStatementAst))
            {
                return [];
            }

            return statement
                .FindAll(ast => ast is StatementBlockAst block && IsOutermostBlockOf(block, statement), searchNestedScriptBlocks: false)
                .Cast<StatementBlockAst>();
        }

        private static bool IsOutermostBlockOf(StatementBlockAst block, StatementAst statement)
        {
            for (Ast? parent = block.Parent; parent is not null && parent != statement; parent = parent.Parent)
            {
                if (parent is StatementBlockAst)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Reads the resources of every Node block and the resources beside them in document order,
        /// passing over any other statement of the configuration.
        /// </summary>
        private static List<DscResourceInstance> ReadConfigurationStatements(
            ReadOnlyCollection<StatementAst> statements, DscParseOptions options)
        {
            List<DscResourceInstance> result = [];
            List<string> nodeNames = [];

            ReadConfigurationStatements(statements, options, result, nodeNames);
            ReportDifferentNodeNames(nodeNames);

            return result;
        }

        private static void ReadConfigurationStatements(
            ReadOnlyCollection<StatementAst> statements, DscParseOptions options, List<DscResourceInstance> result, List<string> nodeNames)
        {
            for (int index = 0; index < statements.Count; index++)
            {
                if (TryReadNodeStatement(statements, index, out string? nodeName, out ScriptBlockExpressionAst? nodeBody, out int consumed))
                {
                    nodeNames.Add(nodeName!);
                    result.AddRange(ReadResourceInstances(
                        nodeBody!.ScriptBlock.EndBlock?.Statements ?? new ReadOnlyCollection<StatementAst>([]), options));
                    index += consumed;
                }
                else if (statements[index] is DynamicKeywordStatementAst { CommandElements.Count: 3 } resource &&
                         resource.CommandElements[2] is HashtableAst)
                {
                    if (ReadResourceInstance(resource, options) is { } instance)
                    {
                        result.Add(instance);
                    }
                }
                else
                {
                    foreach (StatementBlockAst block in ListControlBlocks(statements[index]))
                    {
                        ReadConfigurationStatements(block.Statements, options, result, nodeNames);
                    }
                }
            }
        }

        /// <summary>
        /// Recognizes a Node statement where Node is not a keyword. Its body is either an argument or,
        /// when the brace opens on the next line, the statement after it.
        /// </summary>
        /// <returns>True for a Node statement, with the number of additional statements it spans.</returns>
        private static bool TryReadNodeStatement(
            ReadOnlyCollection<StatementAst> statements,
            int index,
            out string? nodeName,
            out ScriptBlockExpressionAst? body,
            out int consumed)
        {
            nodeName = null;
            body = null;
            consumed = 0;

            if (statements[index] is not PipelineAst pipeline ||
                pipeline.PipelineElements.Count != 1 ||
                pipeline.PipelineElements[0] is not CommandAst command ||
                command.CommandElements.Count is not (2 or 3) ||
                !IsBareWord(command.CommandElements[0], "Node"))
            {
                return false;
            }

            if (command.CommandElements.Count == 3 &&
                command.CommandElements[2] is ScriptBlockExpressionAst inline)
            {
                body = inline;
            }
            else if (command.CommandElements.Count == 2 &&
                     index + 1 < statements.Count &&
                     statements[index + 1] is PipelineAst next &&
                     next.PipelineElements.Count == 1 &&
                     next.PipelineElements[0] is CommandExpressionAst { Expression: ScriptBlockExpressionAst detached })
            {
                body = detached;
                consumed = 1;
            }
            else
            {
                return false;
            }

            nodeName = ReadNodeName(command.CommandElements[1]);
            return true;
        }

        private static string ReadNodeName(CommandElementAst element)
        {
            return element is StringConstantExpressionAst constant ? constant.Value : element.Extent.Text;
        }

        private static void ReportDifferentNodeNames(List<string> nodeNames)
        {
            List<string> distinct = nodeNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinct.Count > 1)
            {
                ReportWarning(
                    $"Read the resources of the Node blocks {string.Join(", ", distinct.Select(name => $"'{name}'"))} into one configuration.");
            }
        }

        // Tokens keep every brace where the AST closes a block early at a property named like a
        // keyword, such as Settings inside a Configuration block or End anywhere.
        private static string ReadBlockContents(string content, Token[] tokens, int openOffset)
        {
            int depth = 0;
            foreach (Token token in tokens)
            {
                if (token.Extent.StartOffset < openOffset)
                {
                    continue;
                }

                if (token.Kind is TokenKind.LCurly or TokenKind.AtCurly)
                {
                    depth++;
                }
                else if (token.Kind is TokenKind.RCurly && --depth == 0)
                {
                    return BlankBefore(content, openOffset + 1, token.Extent.StartOffset);
                }
            }

            return BlankBefore(content, openOffset, content.Length);
        }

        // Line breaks before start stay and every other character there becomes a space, which keeps
        // the line, column and offset of each parsed position equal to the content's.
        private static string BlankBefore(string content, int start, int end)
        {
            StringBuilder text = new(end);
            for (int index = 0; index < start; index++)
            {
                char character = content[index];
                _ = text.Append(character is '\r' or '\n' ? character : ' ');
            }

            return text.Append(content, start, end - start).ToString();
        }

        private static bool IsBareWord(CommandElementAst element, string value)
        {
            return element is StringConstantExpressionAst { StringConstantType: StringConstantType.BareWord } constant
                   && constant.Value.Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        private static List<DscResourceInstance> GetResourceInstances(ConfigurationDefinitionAst configAst, DscParseOptions? options = null)
        {
            List<DscResourceInstance> result = [];
            List<string> nodeNames = [];

            ReadKeywordStatements(configAst.Body.ScriptBlock.EndBlock.Statements, options, result, nodeNames);

            if (nodeNames.Count == 0)
            {
                throw new InvalidOperationException("No Node statement found in the DSC configuration");
            }

            ReportDifferentNodeNames(nodeNames);

            return result;
        }

        private static void ReadKeywordStatements(
            ReadOnlyCollection<StatementAst> statements, DscParseOptions? options, List<DscResourceInstance> result, List<string> nodeNames)
        {
            foreach (StatementAst statement in statements)
            {
                if (statement is not DynamicKeywordStatementAst { CommandElements.Count: > 1 } keyword)
                {
                    foreach (StatementBlockAst block in ListControlBlocks(statement))
                    {
                        ReadKeywordStatements(block.Statements, options, result, nodeNames);
                    }

                    continue;
                }

                if (IsBareWord(keyword.CommandElements[0], "Node"))
                {
                    ScriptBlockExpressionAst nodeBody = (keyword.CommandElements.Count == 3 ? keyword.CommandElements[2] : null) as ScriptBlockExpressionAst
                        ?? throw new InvalidOperationException("Failed to parse Node body in DSC configuration.");
                    NamedBlockAst scriptBlockBody = nodeBody.ScriptBlock.Find(ast => ast is NamedBlockAst, false) as NamedBlockAst
                        ?? throw new InvalidOperationException("Failed to parse Node body statements in DSC configuration.");

                    nodeNames.Add(ReadNodeName(keyword.CommandElements[1]));
                    result.AddRange(ReadResourceInstances(scriptBlockBody.Statements, options));
                }
                else if (keyword.CommandElements.Count == 3 &&
                         keyword.CommandElements[2] is HashtableAst &&
                         ReadResourceInstance(keyword, options) is { } instance)
                {
                    result.Add(instance);
                }
            }
        }

        private static List<DscResourceInstance> ReadResourceInstances(
            ReadOnlyCollection<StatementAst> resourceInstancesInNode, DscParseOptions? options = null)
        {
            List<DscResourceInstance> result = [];

            for (int index = 0; index < resourceInstancesInNode.Count; index++)
            {
                if (resourceInstancesInNode[index] is not DynamicKeywordStatementAst resource)
                {
                    index += SkipUnresolvedResource(resourceInstancesInNode, index);
                    continue;
                }

                if (ReadResourceInstance(resource, options) is { } instance)
                {
                    result.Add(instance);
                }
            }

            return result;
        }

        private static DscResourceInstance? ReadResourceInstance(DynamicKeywordStatementAst resource, DscParseOptions? options)
        {
            // CommandElements
            // 0 - Resource Type
            // 1 - Resource Instance Name
            // 2 - Key/Pair Value list of parameters.
            string resourceType = resource.CommandElements[0].ToString();
            string resourceInstanceName = resource.CommandElements[1] switch
            {
                StringConstantExpressionAst resourceInstanceNameAst => resourceInstanceNameAst.Value,
                ExpandableStringExpressionAst resourceInstanceNameExpAst => resourceInstanceNameExpAst.Value,
                _ => throw new InvalidOperationException("Failed to parse resource instance name in DSC configuration.")
            };

            if (!_dscResources.ContainsKey(resourceType))
            {
                ReportWarning(
                    $"Resource '{resourceType}' (instance '{resourceInstanceName}') was not found among the loaded DSC resources and was omitted from the converted configuration.");
                return null;
            }

            Dictionary<string, object?> properties = [];
            foreach (Tuple<ExpressionAst, StatementAst> keyValuePair in ((HashtableAst)resource.CommandElements[2]).KeyValuePairs)
            {
                properties.Add(
                    keyValuePair.Item1.ToString(),
                    ProcessStatementValue(keyValuePair.Item2, options?.IncludeCIMInstanceInfo ?? true));
            }

            return new DscResourceInstance
            {
                ResourceName = resourceType,
                ResourceInstanceName = resourceInstanceName,
                Properties = properties
            };
        }

        /// <summary>
        /// Reports a resource the installed modules no longer define and returns how many additional
        /// statements it spans. Such a resource is not a DSC keyword, so PowerShell parses it as a plain
        /// command: "Name Instance" followed by a detached body, or "Name Instance { ... }" on one line.
        /// </summary>
        private static int SkipUnresolvedResource(ReadOnlyCollection<StatementAst> statements, int index)
        {
            if (statements[index] is not PipelineAst pipeline ||
                pipeline.PipelineElements.Count == 0 ||
                pipeline.PipelineElements[0] is not CommandAst command ||
                command.CommandElements.Count is not (2 or 3) ||
                command.CommandElements[0] is not StringConstantExpressionAst resourceName)
            {
                ReportWarning($"Skipped an unrecognized statement in the DSC configuration: {statements[index].Extent.Text}");
                return 0;
            }

            string instanceName = command.CommandElements[1] is StringConstantExpressionAst instance
                ? instance.Value
                : command.CommandElements[1].Extent.Text;

            ReportWarning(
                $"Resource '{resourceName.Value}' (instance '{instanceName}') could not be resolved against the imported module(s) " +
                "and was omitted from the converted configuration. It was likely removed in the installed module version.");

            return command.CommandElements.Count == 2 && IsDetachedResourceBody(statements, index + 1) ? 1 : 0;
        }

        private static bool IsDetachedResourceBody(ReadOnlyCollection<StatementAst> statements, int index)
        {
            return index < statements.Count &&
                   statements[index] is PipelineAst pipeline &&
                   pipeline.PipelineElements.Count == 1 &&
                   pipeline.PipelineElements[0] is CommandExpressionAst { Expression: ScriptBlockExpressionAst };
        }

        private static object? ProcessStatementValue(StatementAst statement, bool includeCimInstanceInfo)
        {
            return statement switch
            {
                PipelineAst pipeline => ProcessPipelineAst(pipeline, includeCimInstanceInfo),
                DynamicKeywordStatementAst dynamicStatement => ProcessDynamicKeywordStatementAst(dynamicStatement, includeCimInstanceInfo),
                _ => null
            };
        }

        private static object? ProcessPipelineAst(PipelineAst pip, bool includeCimInstanceInfo)
        {
            // CommandExpressionAst is for Strings, Integers, Arrays, Variables, the "basic" types in a PowerShell DSC configuration
            if (pip.PipelineElements[0] is not CommandExpressionAst expr)
            {
                // CommandAst is for "complex" objects like CIMInstances, e.g. PsDscRunAsCredential or commands like New-Object System.Management.Automation.PSCredential('Password', (ConvertTo-SecureString ((New-Guid).ToString()) -AsPlainText -Force));
                CommandAst ast = pip.PipelineElements[0] as CommandAst ?? throw new InvalidOperationException("Unexpected AST structure in DSC configuration parsing.");
                return ProcessCommandAst(ast, includeCimInstanceInfo).Item2;
            }

            return expr.Expression is not null
                ? ProcessExpressionAst(expr.Expression, includeCimInstanceInfo)
                : pip.Parent.ToString();
        }

        private static (string, object?) ProcessCommandAst(CommandAst commandAst, bool includeCimInstanceInfo)
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
                    for (int index = 0; index < propertyStatementsInCimInstanceBody.Count; index++)
                    {
                        PipelineAst pipelineAst = propertyStatementsInCimInstanceBody[index] as PipelineAst
                            ?? throw new InvalidOperationException("Failed to parse as pipeline statement in CIM instance scriptblock.");

                        CommandAst propertyStatement = pipelineAst.PipelineElements[0] as CommandAst
                            ?? throw new InvalidOperationException("Failed to parse property statement in CIM instance scriptblock.");

                        // Evaluate each property assignment
                        (string, object?) res = ProcessCommandAst(propertyStatement, includeCimInstanceInfo);

                        // A line ending in "=" takes its value from the statement on the next line
                        if (IsAssignmentWithoutValue(propertyStatement) &&
                            index + 1 < propertyStatementsInCimInstanceBody.Count &&
                            propertyStatementsInCimInstanceBody[index + 1] is PipelineAst { PipelineElements.Count: 1 } valuePipeline)
                        {
                            res.Item2 = ProcessPipelineValue(valuePipeline, includeCimInstanceInfo);
                            index++;
                        }

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
                    if (elements.Count == 2)
                    {
                        return (key.Value, null);
                    }

                    return (key.Value, elements.Count == 3 && elements[2] is ExpressionAst value
                        ? ProcessExpressionAst(value, includeCimInstanceInfo)
                        : ProcessArgumentsAsExpression(commandAst, elements[2], elements[elements.Count - 1], includeCimInstanceInfo));
                }

                return ("", commandAst.ToString());
            }

            return ("", commandAst.ToString());
        }

        private static bool IsAssignmentWithoutValue(CommandAst commandAst)
        {
            return commandAst.CommandElements.Count == 2 &&
                   commandAst.CommandElements[0] is StringConstantExpressionAst &&
                   commandAst.CommandElements[1] is StringConstantExpressionAst { Value: "=" };
        }

        private static bool IsCimInstanceCommand(CommandAst commandAst)
        {
            ReadOnlyCollection<CommandElementAst> elements = commandAst.CommandElements;

            return elements.Count >= 2 &&
                   (elements.Count is 2 or 3 ? elements[1] : elements[elements.Count - 1]) is ScriptBlockExpressionAst;
        }

        /// <summary>
        /// A command that is not a CIM instance is kept as its text.
        /// </summary>
        private static object ProcessPipelineValue(PipelineAst pipeline, bool includeCimInstanceInfo)
        {
            switch (pipeline.PipelineElements[0])
            {
                case CommandExpressionAst { Expression: { } expression }:
                    return ProcessExpressionAst(expression, includeCimInstanceInfo);

                case CommandAst command when IsCimInstanceCommand(command):
                    return ProcessCommandAst(command, includeCimInstanceInfo).Item2!;

                default:
                    ReportNonConstantValue(pipeline.Extent.Text, pipeline.Extent.StartLineNumber);
                    return pipeline.Extent.Text;
            }
        }

        private static object ProcessExpressionAst(ExpressionAst expr, bool includeCimInstanceInfo)
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
                ArrayExpressionAst array => ProcessArrayExpressionAst(array, includeCimInstanceInfo),
                // An expandable string like "https://$OrganizationName/"
                ExpandableStringExpressionAst expString => expString.Value,
                // A hashtable like @{key=value; key2=value2}
                HashtableAst hashtable => ProcessHashtableExpressionAst(hashtable, includeCimInstanceInfo),
                // A list without the array operator like "value1", "value2"
                ArrayLiteralAst arrayLiteral => arrayLiteral.Elements.Select(element => ProcessExpressionAst(element, includeCimInstanceInfo)).ToList(),
                BinaryExpressionAst { Operator: TokenKind.Plus } sum => ProcessSumExpressionAst(sum),
                _ => expr.ToString()
            };
        }

        /// <summary>
        /// Reads the arguments of a command-form property assignment, such as "Name = 'a' + 'b'" in a
        /// CIM instance body, as the one expression they spell.
        /// </summary>
        private static object ProcessArgumentsAsExpression(
            CommandAst commandAst, CommandElementAst first, CommandElementAst last, bool includeCimInstanceInfo)
        {
            string text = commandAst.Extent.Text.Substring(
                first.Extent.StartOffset - commandAst.Extent.StartOffset,
                last.Extent.EndOffset - first.Extent.StartOffset);

            // The padding keeps the line numbers of the reparsed expression equal to the content's.
            string padded = new string('\n', first.Extent.StartLineNumber - 1) +
                            new string(' ', first.Extent.StartColumnNumber - 1) +
                            text;

            ScriptBlockAst parsed = Parser.ParseInput(padded, out Token[] _, out ParseError[] errors);
            if (errors.Length == 0 &&
                parsed.EndBlock?.Statements is { Count: 1 } statements &&
                statements[0] is PipelineAst { PipelineElements.Count: 1 } pipeline &&
                pipeline.PipelineElements[0] is CommandExpressionAst { Expression: { } expression })
            {
                return ProcessExpressionAst(expression, includeCimInstanceInfo);
            }

            ReportNonConstantValue(text, first.Extent.StartLineNumber);
            return text;
        }

        private static object ProcessSumExpressionAst(BinaryExpressionAst sum)
        {
            if (TryFoldConstant(sum, out object? value))
            {
                return value!;
            }

            ReportNonConstantValue(sum.Extent.Text, sum.Extent.StartLineNumber);
            return sum.Extent.Text;
        }

        private static void ReportNonConstantValue(string text, int line)
        {
            ReportWarning($"The value '{text}' (line {line}) is not a constant expression and was kept as text.");
        }

        private static bool TryFoldConstant(ExpressionAst expression, out object? value)
        {
            value = null;

            switch (expression)
            {
                case ConstantExpressionAst constant:
                    value = constant.Value;
                    return true;

                case ParenExpressionAst { Pipeline: PipelineAst { PipelineElements.Count: 1 } pipeline }
                    when pipeline.PipelineElements[0] is CommandExpressionAst inner:
                    return TryFoldConstant(inner.Expression, out value);

                case BinaryExpressionAst { Operator: TokenKind.Plus } sum:
                    return TryFoldConstant(sum.Left, out object? left) &&
                           TryFoldConstant(sum.Right, out object? right) &&
                           TryAddConstants(left, right, out value);

                default:
                    return false;
            }
        }

        private static bool TryAddConstants(object? left, object? right, out object? value)
        {
            value = null;

            if (left is string text)
            {
                value = text + Convert.ToString(right, CultureInfo.InvariantCulture);
                return true;
            }

            if (left is int or long && right is int or long)
            {
                long total = Convert.ToInt64(left, CultureInfo.InvariantCulture) + Convert.ToInt64(right, CultureInfo.InvariantCulture);
                value = total is >= int.MinValue and <= int.MaxValue ? (int)total : (object)total;
                return true;
            }

            if (left is int or long or double && right is int or long or double)
            {
                value = Convert.ToDouble(left, CultureInfo.InvariantCulture) + Convert.ToDouble(right, CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        private static List<object> ProcessArrayExpressionAst(ArrayExpressionAst arrayAst, bool includeCimInstanceInfo)
        {
            List<object> values = [];

            foreach (StatementAst statement in arrayAst.SubExpression.Statements)
            {
                switch (statement)
                {
                    // A CIM instance whose class is a registered keyword
                    case DynamicKeywordStatementAst cimInstance:
                        values.Add(ProcessDynamicKeywordStatementAst(cimInstance, includeCimInstanceInfo));
                        break;

                    // A CIM instance whose class is not a keyword, or a command
                    case PipelineAst { PipelineElements.Count: 1 } pipeline when pipeline.PipelineElements[0] is CommandAst:
                        values.Add(ProcessPipelineValue(pipeline, includeCimInstanceInfo));
                        break;

                    // Values separated by commas, such as 'value1', 'value2', are one statement
                    case PipelineAst { PipelineElements.Count: 1 } pipeline
                        when pipeline.PipelineElements[0] is CommandExpressionAst { Expression: ArrayLiteralAst arrayLiteral }:
                        foreach (ExpressionAst element in arrayLiteral.Elements)
                        {
                            values.Add(ProcessExpressionAst(element, includeCimInstanceInfo));
                        }
                        break;

                    case PipelineAst { PipelineElements.Count: 1 } pipeline
                        when pipeline.PipelineElements[0] is CommandExpressionAst { Expression: { } expression }:
                        values.Add(ProcessExpressionAst(expression, includeCimInstanceInfo));
                        break;

                    default:
                        ReportWarning($"Skipped an unrecognized array element in the DSC configuration: {statement.Extent.Text}");
                        break;
                }
            }

            return values;
        }

        private static Dictionary<string, object?> ProcessDynamicKeywordStatementAst(
            DynamicKeywordStatementAst commandAst,
            bool includeCimInstanceInfo)
        {
            ReadOnlyCollection<CommandElementAst>? elements = commandAst.CommandElements;

            // Process in groups of 3: CIMInstanceName, dash, Hashtable
            Dictionary<string, object?> currentResult = [];

            if (elements[0] is StringConstantExpressionAst cimInstanceNameAst &&
                elements[2] is HashtableAst hashtableAst)
            {
                string cimInstanceName = cimInstanceNameAst.Value;

                if (includeCimInstanceInfo)
                {
                    currentResult["CIMInstance"] = cimInstanceName;
                }

                foreach (Tuple<ExpressionAst, StatementAst> kvp in hashtableAst.KeyValuePairs)
                {
                    currentResult[kvp.Item1.ToString().Trim('"', '\'')] = ProcessStatementValue(kvp.Item2, includeCimInstanceInfo);
                }
            }

            return currentResult;
        }

        private static object ProcessVariableExpressionAst(VariableExpressionAst variableAst)
        {
            string text = variableAst.ToString();

            return text.Equals("$true", StringComparison.OrdinalIgnoreCase) || text.Equals("$false", StringComparison.OrdinalIgnoreCase)
                ? bool.Parse(text.TrimStart('$'))
                : text;
        }

        private static object ProcessConstantExpressionAst(ConstantExpressionAst constantAst) => constantAst.Value;

        private static string ProcessMemberExpressionAst(MemberExpressionAst memberAst) => memberAst.ToString();

        private static Hashtable ProcessHashtableExpressionAst(HashtableAst hashtableAst, bool includeCimInstanceInfo)
        {
            Hashtable result = [];
            foreach (Tuple<ExpressionAst, StatementAst> kvp in hashtableAst.KeyValuePairs)
            {
                result[kvp.Item1.ToString()] = ProcessStatementValue(kvp.Item2, includeCimInstanceInfo);
            }
            return result;
        }

        private static List<DscResourceInstance> UpdateWithMetadata(Token[] tokens, List<DscResourceInstance> parsedObjects)
        {
            Dictionary<string, List<DscResourceInstance>> objectsByResourceName = new(StringComparer.OrdinalIgnoreCase);
            foreach (DscResourceInstance parsedObject in parsedObjects)
            {
                if (!objectsByResourceName.TryGetValue(parsedObject.ResourceName, out List<DscResourceInstance>? group))
                {
                    group = [];
                    objectsByResourceName[parsedObject.ResourceName] = group;
                }

                group.Add(parsedObject);
            }

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
                if (tokens[i].Kind is not TokenKind.Comment)
                {
                    continue;
                }

                int keywordIndex = i - 1;
                while (keywordIndex >= 0 && tokens[keywordIndex].Kind is not TokenKind.DynamicKeyword)
                {
                    keywordIndex--;
                }

                // A comment with no enclosing resource declaration has nothing to attach to
                if (keywordIndex < 0 || keywordIndex + 1 >= tokens.Length ||
                    tokens[keywordIndex + 1] is not StringExpandableToken resourceInstanceName)
                {
                    continue;
                }

                string commentResourceType = tokens[keywordIndex].Text;
                string commentResourceInstanceName = resourceInstanceName.Value;

                // Backtrack to find associated property
                int propertyIndex = i;
                while (propertyIndex >= 0 && tokens[propertyIndex].Kind is not TokenKind.Identifier and not TokenKind.NewLine)
                {
                    propertyIndex--;
                }

                if (propertyIndex < 0 || tokens[propertyIndex].Kind is not TokenKind.Identifier)
                {
                    continue;
                }

                string commentAssociatedProperty = tokens[propertyIndex].Text;

                if (!objectsByResourceName.TryGetValue(commentResourceType, out List<DscResourceInstance>? candidates))
                {
                    continue;
                }

                foreach (DscResourceInstance parsedObject in candidates)
                {
                    if (parsedObject.ResourceInstanceName.Equals(commentResourceInstanceName, StringComparison.Ordinal) &&
                        parsedObject.Properties.ContainsKey(commentAssociatedProperty))
                    {
                        parsedObject.AddProperty($"_metadata_{commentAssociatedProperty}", tokens[i].Text);
                    }
                }
            }

            return parsedObjects;
        }

        private static void AppendProperty(StringBuilder result, string property, object? value, int additionalSpaces, string childSpacer, int childLevel)
        {
            switch (value)
            {
                case string strValue:
                    AppendPropertyPrefix(result, property, additionalSpaces, childSpacer);
                    // Only a string that is entirely a variable reference is emitted bare
                    if (strValue.Length > 1 && strValue[0] == '$' && BareVariableReferenceRegex.IsMatch(strValue))
                    {
                        _ = result.AppendLine(strValue);
                    }
                    else if (strValue.StartsWith("New-Object", StringComparison.Ordinal))
                    {
                        _ = result.AppendLine(strValue.TrimStart('"').TrimEnd('"'));
                    }
                    else
                    {
                        _ = AppendQuoted(result, strValue).AppendLine();
                    }
                    break;

                case int intValue:
                    AppendPropertyPrefix(result, property, additionalSpaces, childSpacer);
                    _ = result.Append(intValue).AppendLine();
                    break;

                case bool boolValue:
                    AppendPropertyPrefix(result, property, additionalSpaces, childSpacer);
                    _ = result.Append('$').Append(boolValue).AppendLine();
                    break;

                // Covers Hashtable and the Dictionary instances the parser produces for CIM instances
                case IDictionary dictionary:
                    AppendPropertyPrefix(result, property, additionalSpaces, childSpacer);
                    int contentStart = result.Length;
                    AppendDscObjects(result, [dictionary], childLevel + 1);
                    StripIndentOfOpeningLine(result, contentStart);
                    break;

                case IEnumerable sequence:
                    AppendPropertyPrefix(result, property, additionalSpaces, childSpacer);
                    AppendArray(result, sequence, childSpacer, childLevel);
                    break;

                default:
                    if (value != null)
                    {
                        AppendPropertyPrefix(result, property, additionalSpaces, childSpacer);
                        _ = result.Append(value).AppendLine();
                    }
                    break;
            }
        }

        private static void AppendPropertyPrefix(StringBuilder result, string property, int additionalSpaces, string childSpacer)
        {
            _ = result.Append(childSpacer).Append("    ").Append(property).Append(' ', additionalSpaces).Append("= ");
        }

        private static readonly char[] QuoteEscapeCharacters = ['`', '"'];

        private static StringBuilder AppendQuoted(StringBuilder result, string value)
        {
            if (value.IndexOfAny(QuoteEscapeCharacters) < 0)
            {
                return result.Append('"').Append(value).Append('"');
            }

            _ = result.Append('"');
            foreach (char character in value)
            {
                if (character is '`' or '"')
                {
                    _ = result.Append('`');
                }
                _ = result.Append(character);
            }
            return result.Append('"');
        }

        private static void AppendArray(StringBuilder result, IEnumerable sequence, string childSpacer, int childLevel)
        {
            _ = result.Append("@(");

            List<object?> items = [];
            bool isSimpleArray = true;
            foreach (object? item in sequence)
            {
                items.Add(item);
                if (item is IDictionary)
                {
                    isSimpleArray = false;
                }
            }

            if (items.Count == 0)
            {
                _ = result.AppendLine(")");
                return;
            }

            if (isSimpleArray && items.Count == 1)
            {
                AppendArrayItem(result, items[0]);
                _ = result.AppendLine(")");
                return;
            }

            _ = result.AppendLine();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    _ = result.Append(Environment.NewLine);
                }

                if (items[i] is IDictionary nested)
                {
                    AppendDscObjects(result, [nested], childLevel + 2);
                    result.Length -= Environment.NewLine.Length;
                }
                else
                {
                    _ = result.Append(' ', childSpacer.Length + 8);
                    AppendArrayItem(result, items[i]);
                }
            }
            _ = result.AppendLine();

            _ = result.Append(childSpacer).AppendLine("    )");
        }

        private static void AppendArrayItem(StringBuilder result, object? item)
        {
            if (item is string text)
            {
                _ = AppendQuoted(result, text);
            }
            else
            {
                _ = result.Append(item);
            }
        }

        /// <summary>
        /// Removes the leading indentation of the first rendered line when that line opens a block, so
        /// the nested object starts directly after the "= " of its property assignment.
        /// </summary>
        private static void StripIndentOfOpeningLine(StringBuilder result, int contentStart)
        {
            int lineEnd = contentStart;
            while (lineEnd < result.Length && result[lineEnd] is not '\r' and not '\n')
            {
                lineEnd++;
            }

            int indent = contentStart;
            while (indent < lineEnd && result[indent] == ' ')
            {
                indent++;
            }

            if (indent > contentStart && lineEnd > contentStart && result[lineEnd - 1] == '{')
            {
                _ = result.Remove(contentStart, indent - contentStart);
            }
        }
    }
}
