using System;

namespace NuGet.PackageManagement.UI
{
    public class MetadataLoaderResult
    {
        public MetadataLoaderResult(
            string author,
            Uri iconUrl,
            long? downloadCount,
            string summary)
        {
            Author = author;
            IconUrl = iconUrl;
            DownloadCount = downloadCount;
            Summary = summary;
        }

        public string Author { get; }

        public Uri IconUrl { get; }

        public long? DownloadCount { get; }

        public string Summary { get; }
    }
}