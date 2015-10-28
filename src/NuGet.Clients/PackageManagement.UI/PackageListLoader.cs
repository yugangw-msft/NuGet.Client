using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.ProjectManagement;
using NuGet.Protocol.VisualStudio;
using NuGet.Versioning;
using NuGet.VisualStudio;

namespace NuGet.PackageManagement.UI
{
    public class PackageListLoader
    {
        private bool _isSolution;
        private bool _includePrerelease;
        private IList<NuGetProject> _projects;
        private readonly IEnumerable<IVsPackageManagerProvider> _packageManagerProviders;
        private InstalledPackages _installedPackages;

        public PackageListLoader(
            InstalledPackages installedPackages,
            IList<NuGetProject> projects,
            IEnumerable<IVsPackageManagerProvider> packageManagerProviders,
            bool isSolution,
            bool includePrerelease)
        {
            _installedPackages = installedPackages;
            _projects = projects;
            _packageManagerProviders = packageManagerProviders;
            _isSolution = isSolution;
            _includePrerelease = includePrerelease;
        }

        public List<PackageItemListViewModel> GetPackagesAsync(
            List<UISearchMetadata> packagesWithMetadata,
            CancellationToken cancellationToken)
        {
            List<PackageItemListViewModel> packages = new List<PackageItemListViewModel>();
            foreach (var packageWithMetadata in packagesWithMetadata)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var package = new PackageItemListViewModel();
                package.Id = packageWithMetadata.Identity.Id;
                package.Version = packageWithMetadata.Identity.Version;
                package.IconUrl = packageWithMetadata.IconUrl;
                package.Author = packageWithMetadata.Author;
                package.DownloadCount = packageWithMetadata.DownloadCount;

                HashSet<NuGetVersion> installedVersions;
                if (!_installedPackages.TryGetValue(
                    package.Id,
                    out installedVersions))
                {
                    installedVersions = new HashSet<NuGetVersion>();
                }

                // filter out prerelease version when needed.
                if (package.Version.IsPrerelease && !_includePrerelease)
                {
                    // but it should be kept if it is installed
                    if (installedVersions.Contains(package.Version))
                    {
                        // keep this prerelease package
                    }
                    else
                    {
                        // skip this prerelease package
                        continue;
                    }
                }

                if (!_isSolution)
                {
                    if (installedVersions.Count == 1)
                    {
                        package.InstalledVersion = installedVersions.First();
                    }
                }

                var versionList = new Lazy<Task<IEnumerable<VersionInfo>>>(async () =>
                {
                    var versions = await packageWithMetadata.Versions.Value;
                    var filteredVersions = versions
                            .Where(v => !v.Version.IsPrerelease || _includePrerelease)
                            .ToList();

                    if (!filteredVersions.Any(v => v.Version == package.Version))
                    {
                        filteredVersions.Add(new VersionInfo(package.Version, downloadCount: null));
                    }

                    return filteredVersions;
                });

                package.Versions = versionList;

                package.BackgroundLoader = new Lazy<Task<BackgroundLoaderResult>>(
                    () => BackgroundLoad(package, versionList));

                if (!_isSolution && _packageManagerProviders.Any())
                {
                    package.ProvidersLoader = new Lazy<Task<AlternativePackageManagerProviders>>(
                        () => AlternativePackageManagerProviders.CalculateAlternativePackageManagersAsync(
                            _packageManagerProviders,
                            package.Id,
                            _projects[0]));
                }

                package.Summary = packageWithMetadata.Summary;
                packages.Add(package);
            }

            return packages;
        }

        // Load info in the background
        private async Task<BackgroundLoaderResult> BackgroundLoad(
            PackageItemListViewModel package, Lazy<Task<IEnumerable<VersionInfo>>> versions)
        {
            HashSet<NuGetVersion> installedVersions;
            if (_installedPackages.TryGetValue(package.Id, out installedVersions))
            {
                var versionsUnwrapped = await versions.Value;
                var highestAvailableVersion = versionsUnwrapped
                    .Select(v => v.Version)
                    .Max();

                var lowestInstalledVersion = installedVersions.Min();

                if (VersionComparer.VersionRelease.Compare(lowestInstalledVersion, highestAvailableVersion) < 0)
                {
                    return new BackgroundLoaderResult()
                    {
                        LatestVersion = highestAvailableVersion,
                        InstalledVersion = lowestInstalledVersion,
                        Status = PackageStatus.UpdateAvailable
                    };
                }

                return new BackgroundLoaderResult()
                {
                    LatestVersion = null,
                    InstalledVersion = lowestInstalledVersion,
                    Status = PackageStatus.Installed
                };
            }

            // the package is not installed. In this case, the latest version is the version
            // of the search result.
            return new BackgroundLoaderResult()
            {
                LatestVersion = package.Version,
                InstalledVersion = null,
                Status = PackageStatus.NotInstalled
            };
        }
    }
}