using System;

namespace DSCParser.CSharp
{
    /// <summary>
    /// Options for writing DSC objects as configuration text
    /// </summary>
    public sealed class DscWriteOptions
    {
        /// <summary>
        /// Gets or sets whether code in a string becomes a single-quoted literal and a value no literal holds fails the write
        /// </summary>
        public bool IsLiteralOnly { get; set; }

        /// <summary>
        /// Gets or sets the line break written after each line
        /// </summary>
        public string NewLine { get; set; } = Environment.NewLine;
    }

    /// <summary>
    /// Configuration text, or the error that stopped the write
    /// </summary>
    public sealed class DscWriteResult
    {
        private DscWriteResult(string configuration, DscWriteError? error)
        {
            Configuration = configuration;
            Error = error;
        }

        /// <summary>
        /// Empty when the write failed
        /// </summary>
        public string Configuration { get; }

        /// <summary>
        /// Null when the write succeeded
        /// </summary>
        public DscWriteError? Error { get; }

        internal static DscWriteResult Success(string configuration) => new(configuration, null);

        internal static DscWriteResult Failed(DscWriteError error) => new(string.Empty, error);
    }

    /// <summary>
    /// The resource and setting no literal can hold
    /// </summary>
    public sealed class DscWriteError
    {
        internal DscWriteError(string? resourceName, string? instanceName, string? setting)
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
        /// The dotted path of the setting, null when the type or instance name cannot be written
        /// </summary>
        public string? Setting { get; }
    }
}
