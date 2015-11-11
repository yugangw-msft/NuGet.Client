// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.VisualStudio;
using NuGet.Versioning;
using NuGet.VisualStudio;
using Mvs = Microsoft.VisualStudio.Shell;

namespace NuGet.PackageManagement.UI
{
    public class InstalledPackages : IEnumerable<KeyValuePair<string, HashSet<NuGetVersion>>>
    {
        // the key is the package id, the value is the installed versions.
        private Dictionary<string, HashSet<NuGetVersion>> _installedPackages;

        public InstalledPackages()
        {
            _installedPackages = new Dictionary<string, HashSet<NuGetVersion>>();
        }

        public void Clear()
        {
            _installedPackages.Clear();
        }

        public IEnumerator<KeyValuePair<string, HashSet<NuGetVersion>>> GetEnumerator()
        {
            return _installedPackages.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _installedPackages.GetEnumerator();
        }

        public bool TryGetValue(string packageId, out HashSet<NuGetVersion> versions)
        {
            return _installedPackages.TryGetValue(packageId, out versions);
        }

        public void Add(string packageId, HashSet<NuGetVersion> versions)
        {
            _installedPackages.Add(packageId, versions);
        }
    }

    [Flags]
    internal enum DataToRefresh
    {
        None = 0,
        Metadata = 1,

        IncludePrerelease = 2,

        // InstalledVersion, Latest Available Version and Status
        Status = 4
    };

    internal class InstalledTabLoader : ILoader
    {
        // Indicates whether the loader is created by solution package manager.
        private readonly bool _isSolution;

        private readonly IEnumerable<IVsPackageManagerProvider> _packageManagerProviders;
        private readonly NuGetPackageManager _packageManager;
        private readonly IList<NuGetProject> _projects;

        private SourceRepository _sourceRepository;

        private bool _includePrerelease;
        private string _searchText;

        // data
        private InstalledPackages _installedPackages;

        private List<PackageItemListViewModel> _packages;
        private List<PackageItemListViewModel> _searchResult;

        // flags used to decide what data should be refreshed
        private DataToRefresh _dataToRefresh;

        public InstalledTabLoader(
            SourceRepository sourceRepository,
            IEnumerable<IVsPackageManagerProvider> providers,
            IList<NuGetProject> projects,
            NuGetPackageManager packageManager,
            string searchText,
            bool isSolution,
            bool includePrerelease)
        {
            _sourceRepository = sourceRepository;
            _packageManagerProviders = providers;
            _projects = projects;
            _packageManager = packageManager;
            _searchText = searchText;
            _isSolution = isSolution;
            _includePrerelease = includePrerelease;
            _dataToRefresh = DataToRefresh.None;

            LoadingMessage = string.IsNullOrWhiteSpace(searchText) ?
                Resources.Text_Loading :
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.Text_Searching,
                    searchText);
        }

        // called after source repository is changed.
        public void SetSourceRepository(
            SourceRepository sourceRepository)
        {
            _sourceRepository = sourceRepository;

            _dataToRefresh |= DataToRefresh.Metadata | DataToRefresh.Status;
        }

        // called after include prerelease checkbox is checked/unchecked.
        public void SetIncludePrerelease(
            bool includePrerelease)
        {
            if (_includePrerelease == includePrerelease)
            {
                return;
            }

            _includePrerelease = includePrerelease;
            _dataToRefresh |= DataToRefresh.IncludePrerelease;
        }

        public void SetSearchText(string searchText)
        {
            if (_searchText == searchText)
            {
                return;
            }

            _searchText = searchText;
            LoadingMessage = string.IsNullOrWhiteSpace(searchText) ?
                Resources.Text_Loading :
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.Text_Searching,
                    searchText);

            _searchResult = null;
        }

        // called after user action is executed
        public void Refresh()
        {
            _installedPackages = null;
            _packages = null;
            _searchResult = null;
        }

        public string LoadingMessage { get; set; }

        public async Task<LoadResult> LoadItemsAsync(int startIndex, CancellationToken cancellationToken)
        {
            if (_installedPackages == null)
            {
                var installedPackagesLoader = new InstalledPackagesLoader(_projects);
                _installedPackages = await installedPackagesLoader.GetInstalledPackagesAsync(cancellationToken);
            }

            if (_packages == null)
            {
                var packageListLoader = new PackageListLoader(
                    _installedPackages,
                    _projects,
                    _packageManagerProviders,
                    isSolution: _isSolution,
                    includePrerelease: _includePrerelease);
                _packages = await packageListLoader.GetPackagesAsync(
                    _installedPackages,
                    _packageManager,
                    _sourceRepository,
                    cancellationToken);
            }

            if (_searchResult == null)
            {
                _searchResult = _packages.Where(package => package.Id.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) != -1).ToList();
            }

            // process refresh
            if (_dataToRefresh.HasFlag(DataToRefresh.Metadata))
            {
                await RefreshMetadataAsync(cancellationToken);
                _dataToRefresh -= DataToRefresh.Metadata;
            }

            if (_dataToRefresh.HasFlag(DataToRefresh.IncludePrerelease))
            {
                await RefreshIncludePrereleaseAsync(cancellationToken);
                _dataToRefresh -= DataToRefresh.IncludePrerelease;
            }

            return new LoadResult()
            {
                Items = _searchResult.Skip(startIndex),
                HasMoreItems = false,
                NextStartIndex = _packages.Count
            };
        }

        private async Task RefreshMetadataAsync(CancellationToken cancellationToken)
        {
            var localResource = await _packageManager.PackagesFolderSourceRepository
                .GetResourceAsync<UIMetadataResource>(cancellationToken);

            // UIMetadataResource may not be available
            // Given that this is the 'Installed' filter, we ignore failures in reaching the remote server
            // Instead, we will use the local UIMetadataResource
            UIMetadataResource metadataResource;
            try
            {
                if (_sourceRepository == null)
                {
                    metadataResource = null;
                }
                else
                {
                    metadataResource = await _sourceRepository.GetResourceAsync<UIMetadataResource>(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                metadataResource = null;
                // Write stack to activity log
                Mvs.ActivityLog.LogError(Constants.LogEntrySource, ex.ToString());
            }

            foreach (var package in _searchResult)
            {
                package.MetadataLoader = new BackgroundLoader<MetadataLoaderResult>(
                    new Lazy<Task<MetadataLoaderResult>>(() =>
                    {
                        return InstalledTabMetadataLoader.GetPackageMetadataAsync(
                            localResource,
                            metadataResource,
                            new PackageIdentity(package.Id, package.Version),
                            CancellationToken.None);
                    }));
            }
        }

        private async Task RefreshIncludePrereleaseAsync(CancellationToken cancellationToken)
        {
            var packageListLoader = new PackageListLoader(
                _installedPackages,
                _projects,
                _packageManagerProviders,
                isSolution: _isSolution,
                includePrerelease: _includePrerelease);

            foreach (var package in _searchResult)
            {
                await package.SetIncludePrerelease(_includePrerelease);
                package.BackgroundLoader = new BackgroundLoader<BackgroundLoaderResult>(
                    new Lazy<Task<BackgroundLoaderResult>>(
                        () => packageListLoader.BackgroundLoad(package)));
            }
        }
    }
}