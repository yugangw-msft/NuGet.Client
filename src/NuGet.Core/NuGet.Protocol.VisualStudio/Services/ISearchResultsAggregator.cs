using NuGet.Protocol.Core.Types;
using System.Collections.Generic;

namespace NuGet.Protocol.VisualStudio.Services
{
    public interface ISearchResultsAggregator
    {
        void Aggregate(string queryString, params IEnumerable<PackageSearchMetadata>[] results);
    }
}
