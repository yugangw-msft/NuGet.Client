using System;
using System.Collections.Generic;
using NuGet.Protocol.VisualStudio;
using NuGet.Versioning;

namespace NuGet.PackageManagement.UI
{
    public class MetadataLoaderResult
    {
        public MetadataLoaderResult(
            string author,
            Uri iconUrl,
            long? downloadCount,
            string summary,
            IEnumerable<VersionInfo> versions)
        {
            if (versions == null)
            {
                throw new ArgumentNullException(nameof(versions));
            }

            Author = author;
            IconUrl = iconUrl;
            DownloadCount = downloadCount;
            Summary = summary;
            Versions = versions;
        }

        public string Author { get; }

        public Uri IconUrl { get; }

        public long? DownloadCount { get; }

        public string Summary { get; }

        // all available versions from the source
        public IEnumerable<VersionInfo> Versions { get; }
    }
}