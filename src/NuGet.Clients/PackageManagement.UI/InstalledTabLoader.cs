// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.ProjectManagement;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.VisualStudio;
using NuGet.Versioning;
using NuGet.VisualStudio;

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

        private List<UISearchMetadata> _packagesWithMetadata;
        private List<PackageItemListViewModel> _packages;
        private List<PackageItemListViewModel> _searchResult;

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

            LoadingMessage = string.IsNullOrWhiteSpace(searchText) ?
                Resources.Text_Loading :
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.Text_Searching,
                    searchText);
        }

        // callsed after s
        public void SetSourceRepository(
            SourceRepository sourceRepository)
        {
            _sourceRepository = sourceRepository;

            _packagesWithMetadata = null;
            _packages = null;
            _searchResult = null;
        }

        public void SetIncludePrerelease(
            bool includePrerelease)
        {
            if (_includePrerelease == includePrerelease)
            {
                return;
            }

            _includePrerelease = includePrerelease;

            _packagesWithMetadata = null;
            _packages = null;
            _searchResult = null;
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
            _packagesWithMetadata = null;
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

            if (_packagesWithMetadata == null)
            {
                var packageMetadataLoader = new PackageMetadataLoader(
                    _packageManager,
                    _sourceRepository,
                    _includePrerelease);
                _packagesWithMetadata = await packageMetadataLoader.GetPackagesWithMetadataAsync(
                    _installedPackages,
                    cancellationToken);
            }

            if (_packages == null)
            {
                var packageListLoader = new PackageListLoader(
                    _installedPackages,
                    _projects,
                    _packageManagerProviders,
                    isSolution: _isSolution,
                    includePrerelease: _includePrerelease);
                _packages = packageListLoader.GetPackagesAsync(_packagesWithMetadata, cancellationToken);
            }

            if (_searchResult == null)
            {
                _searchResult = _packages.Where(package => package.Id.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) != -1).ToList();
            }

            return new LoadResult()
            {
                Items = _searchResult.Skip(startIndex),
                HasMoreItems = false,
                NextStartIndex = _packages.Count
            };
        }
    }
}