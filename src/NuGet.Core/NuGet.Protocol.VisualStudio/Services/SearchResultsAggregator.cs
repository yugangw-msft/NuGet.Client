using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NuGet.Protocol.VisualStudio.Services
{
    public class SearchResultsAggregator : ISearchResultsAggregator
    {
        private readonly ISearchResultsIndexer _indexer;

        public SearchResultsAggregator(ISearchResultsIndexer indexer)
        {
            if (indexer == null)
            {
                throw new ArgumentNullException(nameof(indexer));
            }

            _indexer = indexer;
        }

        public void Aggregate(string queryString, params IEnumerable<PackageSearchMetadata>[] results)
        {
            var mergedIndex = new MergedIndex();
            foreach(var result in results)
            {
                mergedIndex.MergeResult(result);
            }

            var ranking = _indexer.Search(queryString, mergedIndex.Entries);

            var queues = results.Select(result => new Queue<string>(result.Select(entry => entry.Identity.Id)));
            while (queues.Any(q => !q.IsEmpty()))
            {
                var current = queues.Select(q => q.Peek());
                current.Max(c => rankingranking[c])
            }
        }

        private class MergedIndex
        {
            private readonly IDictionary<string, PackageSearchMetadata> combined = new Dictionary<string, PackageSearchMetadata>(StringComparer.OrdinalIgnoreCase);

            public IEnumerable<PackageSearchMetadata> Entries => combined.Values;

            public void MergeResult(IEnumerable<PackageSearchMetadata> result)
            {
                foreach (var entry in result)
                {
                    PackageSearchMetadata value;
                    if (combined.TryGetValue(entry.Identity.Id, out value))
                    {
                        combined[entry.Identity.Id] = MergeEntries(value, entry);
                    }
                    else
                    {
                        combined.Add(entry.Identity.Id, entry);
                    }
                }
            }

            private static PackageSearchMetadata MergeEntries(PackageSearchMetadata lhs, PackageSearchMetadata rhs)
            {
                var newerEntry = (lhs.Identity.Version >= rhs.Identity.Version) ? lhs : rhs;
                var mergedVersions = lhs.Versions.Concat(rhs.Versions).Distinct().ToArray();
                newerEntry.Versions = mergedVersions;
                return newerEntry;
            }
        }
    }
}
