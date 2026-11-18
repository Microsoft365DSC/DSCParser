using System;

namespace DSCParser.CSharp
{
    /// <summary>
    /// Options for writing DSC objects as configuration text
    /// </summary>
    public sealed class DscEmitOptions
    {
        /// <summary>
        /// Gets or sets whether code in a string becomes a single-quoted literal and a value no literal holds refuses the emit
        /// </summary>
        public bool IsLiteralOnly { get; set; }

        /// <summary>
        /// Gets or sets the line break written after each line
        /// </summary>
        public string NewLine { get; set; } = Environment.NewLine;
    }

    /// <summary>
    /// Configuration text, or the refusal that stopped the emit
    /// </summary>
    public sealed class DscEmitResult
    {
        private DscEmitResult(string configuration, DscEmitRefusal? refusal)
        {
            Configuration = configuration;
            Refusal = refusal;
        }

        /// <summary>
        /// Empty when refused
        /// </summary>
        public string Configuration { get; }

        /// <summary>
        /// Null when the emit succeeded
        /// </summary>
        public DscEmitRefusal? Refusal { get; }

        internal static DscEmitResult Success(string configuration) => new(configuration, null);

        internal static DscEmitResult Refused(DscEmitRefusal refusal) => new(string.Empty, refusal);
    }

    /// <summary>
    /// The resource and setting no literal can hold
    /// </summary>
    public sealed class DscEmitRefusal
    {
        internal DscEmitRefusal(string? resourceName, string? instanceName, string? setting)
        {
            ResourceName = resourceName;
            InstanceName = instanceName;
            Setting = setting;
        }

        /// <summary>
        /// Null when the resource has no type name
        /// </summary>
        public string? ResourceName { get; }

        /// <summary>
        /// Null when the resource has no instance name
        /// </summary>
        public string? InstanceName { get; }

        /// <summary>
        /// The dotted path of the setting, null when the type or instance name is refused
        /// </summary>
        public string? Setting { get; }
    }
}
