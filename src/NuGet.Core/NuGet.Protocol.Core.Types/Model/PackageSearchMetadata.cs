using NuGet.Packaging.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NuGet.Protocol.Core.Types
{
    public class PackageSearchMetadata
    {
        public PackageSearchMetadata(PackageIdentity identity,
                                string title,
                                string summary,
                                string author,
                                long? downloadCount,
                                Uri iconUrl,
                                IEnumerable<VersionInfo> versions,
                                PackageMetadata latestPackageMetadata)
        {
            Identity = identity;
            Title = title;
            Summary = summary;
            IconUrl = iconUrl;
            Versions = versions;
            Author = author;
            DownloadCount = downloadCount;
            LatestPackageMetadata = latestPackageMetadata;
        }

        public PackageIdentity Identity { get; }

        public string Summary { get; }

        public Uri IconUrl { get; }

        public IEnumerable<VersionInfo> Versions { get; set; }

        public PackageMetadata LatestPackageMetadata { get; }

        public string Title { get; }

        public string Author { get; }

        public long? DownloadCount { get; }
        public string Description { get; }
        public string[] Tags { get; set; }
    }
}
