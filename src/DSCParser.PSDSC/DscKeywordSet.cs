using System;
using System.Collections.Generic;
using System.Management.Automation.Language;

namespace DSCParser.PSDSC
{
    /// <summary>
    /// The keywords of one schema cache, independent of the keywords DscKeywordRegistry holds
    /// </summary>
    public sealed class DscKeywordSet
    {
        private readonly Dictionary<string, DynamicKeyword> _keywords;

        private DscKeywordSet(Dictionary<string, DynamicKeyword> keywords)
        {
            _keywords = keywords;
        }

        /// <summary>
        /// The number of keywords in the set
        /// </summary>
        public int Count => _keywords.Count;

        internal IEnumerable<string> Names => _keywords.Keys;

        /// <param name="keywords">
        /// The entries of the cache's keywords array, each a map of the shape
        /// ConvertTo-DscKeywordSchemaObject produces.
        /// </param>
        public static DscKeywordSet FromSchemaCache(IEnumerable<object> keywords)
        {
            if (keywords is null)
            {
                throw new ArgumentNullException(nameof(keywords));
            }

            Dictionary<string, DynamicKeyword> built = new(StringComparer.OrdinalIgnoreCase);
            foreach (object entry in keywords)
            {
                DynamicKeyword keyword = DscSchemaCacheKeywords.Build(entry);
                built[keyword.Keyword] = keyword;
            }

            return new DscKeywordSet(built);
        }

        // The engine's DynamicKeyword table is thread-static.
        internal void AddToKeywordTable()
        {
            foreach (DynamicKeyword keyword in _keywords.Values)
            {
                if (!DynamicKeyword.ContainsKeyword(keyword.Keyword))
                {
                    DynamicKeyword.AddKeyword(keyword);
                }
            }
        }
    }
}
