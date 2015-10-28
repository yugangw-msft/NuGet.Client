using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.VisualStudio;
using Mvs = Microsoft.VisualStudio.Shell;

namespace NuGet.PackageManagement.UI
{
    public class PackageMetadataLoader
    {
        private readonly string LogEntrySource = "NuGet Package Manager";
        public static readonly int MaxDegreeOfParallelism = 16;

        NuGetPackageManager _packageManager;
        SourceRepository _sourceRepository;
        bool _includePrerelease;

        public PackageMetadataLoader(
            NuGetPackageManager packageManager,
            SourceRepository sourceRepository,
            bool includePrerelease)
        {
            _packageManager = packageManager;
            _sourceRepository = sourceRepository;
            _includePrerelease = includePrerelease;
        }

        public async Task<List<UISearchMetadata>> GetPackagesWithMetadataAsync(
            InstalledPackages installedPackages,
            CancellationToken cancellationToken)
        {
            var packagesWithMetadata = new List<UISearchMetadata>();
            var localResource = await _packageManager.PackagesFolderSourceRepository
                .GetResourceAsync<UIMetadataResource>(cancellationToken);

            // UIMetadataResource may not be available
            // Given that this is the 'Installed' filter, we ignore failures in reaching the remote server
            // Instead, we will use the local UIMetadataResource
            UIMetadataResource metadataResource;
            try
            {
                metadataResource =
                _sourceRepository == null ?
                    null :
                    await _sourceRepository.GetResourceAsync<UIMetadataResource>(cancellationToken);
            }
            catch (Exception ex)
            {
                metadataResource = null;
                // Write stack to activity log
                Mvs.ActivityLog.LogError(LogEntrySource, ex.ToString());
            }

            // group installed packages
            var groupedPackages = installedPackages.Select(
                p => new PackageIdentity(p.Key, p.Value.Max()));

            // create tasks to get metadata in parallel
            var bag = new ConcurrentBag<PackageIdentity>(groupedPackages);
            var tasks = new List<Task>();
            var metadataList = new ConcurrentQueue<UISearchMetadata>();
            for (int i = 0; i < MaxDegreeOfParallelism; ++i)
            {
                tasks.Add(Task.Run(async () =>
                {
                    PackageIdentity packageIdentity;
                    while (bag.TryTake(out packageIdentity))
                    {
                        var metadata = await GetPackageMetadataAsync(
                            localResource,
                            metadataResource,
                            packageIdentity,
                            cancellationToken);
                        metadataList.Enqueue(metadata);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            packagesWithMetadata = metadataList.ToList();
            return packagesWithMetadata;
        }

        /// <summary>
        /// Get the metadata of an installed package.
        /// </summary>
        /// <param name="localResource">The local resource, i.e. the package folder of the solution.</param>
        /// <param name="metadataResource">The remote metadata resource.</param>
        /// <param name="identity">The installed package.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The metadata of the package.</returns>
        private async Task<UISearchMetadata> GetPackageMetadataAsync(
            UIMetadataResource localResource,
            UIMetadataResource metadataResource,
            PackageIdentity identity,
            CancellationToken cancellationToken)
        {
            if (metadataResource == null)
            {
                return await GetPackageMetadataWhenRemoteSourceUnavailable(
                    localResource,
                    identity,
                    cancellationToken);
            }

            try
            {
                var metadata = await GetPackageMetadataFromMetadataResourceAsync(
                    metadataResource,
                    identity,
                    cancellationToken);

                // if the package does not exist in the remote source, NuGet should
                // try getting metadata from the local resource.
                if (String.IsNullOrEmpty(metadata.Summary) && localResource != null)
                {
                    return await GetPackageMetadataWhenRemoteSourceUnavailable(
                        localResource,
                        identity,
                        cancellationToken);
                }
                else
                {
                    return metadata;
                }
            }
            catch
            {
                // When a v2 package source throws, it throws an InvalidOperationException or WebException
                // When a v3 package source throws, it throws an HttpRequestException

                // The remote source is not available. NuGet should not fail but
                // should use the local resource instead.
                if (localResource != null)
                {
                    return await GetPackageMetadataWhenRemoteSourceUnavailable(
                        localResource,
                        identity,
                        cancellationToken);
                }
                else
                {
                    throw;
                }
            }
        }

        // Gets the package metadata from the local resource when the remote source
        // is not available.
        private static async Task<UISearchMetadata> GetPackageMetadataWhenRemoteSourceUnavailable(
            UIMetadataResource localResource,
            PackageIdentity identity,
            CancellationToken cancellationToken)
        {
            UIPackageMetadata packageMetadata = null;
            if (localResource != null)
            {
                var localMetadata = await localResource.GetMetadata(
                    identity.Id,
                    includePrerelease: true,
                    includeUnlisted: true,
                    token: cancellationToken);
                packageMetadata = localMetadata.FirstOrDefault(p => p.Identity.Version == identity.Version);
            }

            string summary = string.Empty;
            string title = identity.Id;
            string author = string.Empty;
            if (packageMetadata != null)
            {
                summary = packageMetadata.Summary;
                if (string.IsNullOrEmpty(summary))
                {
                    summary = packageMetadata.Description;
                }
                if (!string.IsNullOrEmpty(packageMetadata.Title))
                {
                    title = packageMetadata.Title;
                }

                author = string.Join(", ", packageMetadata.Authors);
            }

            var versions = new List<VersionInfo>
            {
                new VersionInfo(identity.Version, downloadCount: null)
            };

            return new UISearchMetadata(
                identity,
                title: title,
                summary: summary,
                author: author,
                downloadCount: packageMetadata?.DownloadCount,
                iconUrl: packageMetadata?.IconUrl,
                versions: ToLazyTask(versions),
                latestPackageMetadata: null);
        }

        private async Task<UISearchMetadata> GetPackageMetadataFromMetadataResourceAsync(
            UIMetadataResource metadataResource,
            PackageIdentity identity,
            CancellationToken cancellationToken)
        {
            var uiPackageMetadatas = await metadataResource.GetMetadata(
                identity.Id,
                _includePrerelease,
                includeUnlisted: false,
                token: cancellationToken);
            var packageMetadata = uiPackageMetadatas.FirstOrDefault(p => p.Identity.Version == identity.Version);

            string summary = string.Empty;
            string title = identity.Id;
            string author = string.Empty;
            if (packageMetadata != null)
            {
                summary = packageMetadata.Summary;
                if (string.IsNullOrEmpty(summary))
                {
                    summary = packageMetadata.Description;
                }
                if (!string.IsNullOrEmpty(packageMetadata.Title))
                {
                    title = packageMetadata.Title;
                }

                author = string.Join(", ", packageMetadata.Authors);
            }

            var versions = uiPackageMetadatas.OrderByDescending(m => m.Identity.Version)
                .Select(m => new VersionInfo(m.Identity.Version, m.DownloadCount));
            return new UISearchMetadata(
                identity,
                title: title,
                summary: summary,
                author: author,
                downloadCount: packageMetadata?.DownloadCount,
                iconUrl: packageMetadata?.IconUrl,
                versions: ToLazyTask(versions),
                latestPackageMetadata: packageMetadata);
        }

        private static Lazy<Task<IEnumerable<VersionInfo>>> ToLazyTask(IEnumerable<VersionInfo> versions)
        {
            return new Lazy<Task<IEnumerable<VersionInfo>>>(() => Task.FromResult(versions));
        }
    }
}
