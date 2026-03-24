using System;

namespace Microsoft.PowerShell.DesiredStateConfiguration.V2
{
    internal static class StringExtensions
    {
        public static bool Contains(this string source, string value, StringComparison comparisonType)
        {
            return source?.IndexOf(value, comparisonType) >= 0;
        }
    }
}
