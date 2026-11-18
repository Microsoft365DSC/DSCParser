using System.Collections.Generic;

namespace DSCParser.CSharp
{
    /// <summary>
    /// Offsets index the parsed content.
    /// </summary>
    public sealed class DscResourceExtent
    {
        /// <summary>
        /// Creates an extent from its offsets and lines.
        /// </summary>
        public DscResourceExtent(int startOffset, int endOffset, int startLine, int endLine)
        {
            StartOffset = startOffset;
            EndOffset = endOffset;
            StartLine = startLine;
            EndLine = endLine;
        }

        /// <summary>
        /// Counts from zero.
        /// </summary>
        public int StartOffset { get; }

        /// <summary>
        /// One past the last character.
        /// </summary>
        public int EndOffset { get; }

        /// <summary>
        /// Counts from one.
        /// </summary>
        public int StartLine { get; }

        /// <summary>
        /// Counts from one.
        /// </summary>
        public int EndLine { get; }
    }

    /// <summary>
    /// Why a statement inside a Node block was not read.
    /// </summary>
    public enum DscSkipReason
    {
        /// <summary>
        /// The statement is no resource block.
        /// </summary>
        UnrecognizedStatement,

        /// <summary>
        /// The resource is no keyword of the imported modules.
        /// </summary>
        UndefinedResource,

        /// <summary>
        /// The resource is a keyword missing from the supplied resources.
        /// </summary>
        UnloadedResource
    }

    /// <summary>
    /// A statement inside a Node block the conversion passed over.
    /// </summary>
    public sealed class DscSkippedStatement
    {
        /// <summary>
        /// Creates a skipped statement.
        /// </summary>
        public DscSkippedStatement(DscSkipReason reason, string text, DscResourceExtent extent, string? resourceName, string? instanceName)
        {
            Reason = reason;
            Text = text;
            Extent = extent;
            ResourceName = resourceName;
            InstanceName = instanceName;
        }

        /// <summary>
        /// The kind of statement.
        /// </summary>
        public DscSkipReason Reason { get; }

        /// <summary>
        /// A detached body is included.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// A detached body is included.
        /// </summary>
        public DscResourceExtent Extent { get; }

        /// <summary>
        /// Null for an unrecognized statement.
        /// </summary>
        public string? ResourceName { get; }

        /// <summary>
        /// Null for an unrecognized statement.
        /// </summary>
        public string? InstanceName { get; }
    }

    /// <summary>
    /// The resources of a configuration and the Node statements passed over.
    /// </summary>
    public sealed class DscParseResult
    {
        /// <summary>
        /// Creates a parse result.
        /// </summary>
        public DscParseResult(IReadOnlyList<DscResourceInstance> resources, IReadOnlyList<DscSkippedStatement> skippedStatements)
        {
            Resources = resources;
            SkippedStatements = skippedStatements;
        }

        /// <summary>
        /// In document order.
        /// </summary>
        public IReadOnlyList<DscResourceInstance> Resources { get; }

        /// <summary>
        /// In document order.
        /// </summary>
        public IReadOnlyList<DscSkippedStatement> SkippedStatements { get; }
    }
}
