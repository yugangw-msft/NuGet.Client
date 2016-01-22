using NuGet.Protocol.Core.Types;
using System.Collections.Generic;

namespace NuGet.Protocol.VisualStudio.Services
{
    public interface ISearchResultsIndexer
    {
        IDictionary<string, int> Search(string queryString, IEnumerable<PackageSearchMetadata> entries);
    }
}
