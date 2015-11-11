// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.VisualStudio;
using NuGet.Versioning;
using NuGet.VisualStudio;
using Mvs = Microsoft.VisualStudio.Shell;

namespace NuGet.PackageManagement.UI
{
    public class BrowseTabLoader : ILoader
    {
        private const int PageSize = 25;

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

        public BrowseTabLoader(
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

        public string LoadingMessage
        {
            get;
            set;
        }

        public async Task<LoadResult> LoadItemsAsync(int startIndex, CancellationToken cancellationToken)
        {
            var searchResource = await _sourceRepository.GetResourceAsync<UISearchResource>();

            if (searchResource == null)
            {
                return new LoadResult()
                {
                    Items = Enumerable.Empty<PackageItemListViewModel>(),
                    HasMoreItems = false,
                    NextStartIndex = _packages.Count
                };
            }

            var searchFilter = new SearchFilter();
            searchFilter.IncludePrerelease = _includePrerelease;
            searchFilter.SupportedFrameworks = GetSupportedFrameworks();
            var searchResults = await searchResource.Search(
                _searchText,
                searchFilter,
                startIndex,
                PageSize + 1,
                cancellationToken);

            var items = searchResults.ToList();
            var hasMoreItems = items.Count > PageSize;
            if (hasMoreItems)
            {
                items.RemoveAt(items.Count - 1);
            }



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

        private IEnumerable<string> GetSupportedFrameworks()
        {
            var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var project in _projects)
            {
                NuGetFramework framework;
                if (project.TryGetMetadata(NuGetProjectMetadataKeys.TargetFramework,
                    out framework))
                {
                    if (framework != null
                        && framework.IsAny)
                    {
                        // One of the project's target framework is AnyFramework. In this case,
                        // we don't need to pass the framework filter to the server.
                        return Enumerable.Empty<string>();
                    }

                    if (framework != null
                        && framework.IsSpecificFramework)
                    {
                        frameworks.Add(framework.DotNetFrameworkName);
                    }
                }
                else
                {
                    // we also need to process SupportedFrameworks
                    IEnumerable<NuGetFramework> supportedFrameworks;
                    if (project.TryGetMetadata(
                        NuGetProjectMetadataKeys.SupportedFrameworks,
                        out supportedFrameworks))
                    {
                        foreach (var f in supportedFrameworks)
                        {
                            if (f.IsAny)
                            {
                                return Enumerable.Empty<string>();
                            }

                            frameworks.Add(f.DotNetFrameworkName);
                        }
                    }
                }
            }

            return frameworks;
        }
    }
}
