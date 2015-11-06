// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using NuGet.Protocol.VisualStudio;
using NuGet.Versioning;

namespace NuGet.PackageManagement.UI
{
    // This is the model class behind the package items in the infinite scroll list.
    // Some of its properties, such as Latest Version, Status, are fetched on-demand in the background.
    public class PackageItemListViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Id { get; set; }

        public NuGetVersion Version { get; set; }

        private BackgroundLoader<MetadataLoaderResult> _metadataLoader;

        public BackgroundLoader<MetadataLoaderResult> MetadataLoader
        {
            set
            {
                _metadataLoader = value;

                OnPropertyChanged(nameof(Author));
            }
        }

        private string _author;

        public string Author
        {
            get
            {
                if (_metadataLoader != null && !_metadataLoader.LoaderHasBeenRun)
                {
                    _metadataLoader.LoaderHasBeenRun = true;
                    Task.Run(async () =>
                    {
                        var result = await _metadataLoader.GetResult();

                        await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        Author = result.Author;
                        DownloadCount = result.DownloadCount;
                        IconUrl = result.IconUrl;
                        Summary = result.Summary;
                    });
                }

                return _author;
            }
            set
            {
                _author = value;
                OnPropertyChanged(nameof(Author));
            }
        }

        // The installed version of the package.
        private NuGetVersion _installedVersion;

        public NuGetVersion InstalledVersion
        {
            get
            {
                return _installedVersion;
            }
            set
            {
                if (!VersionEquals(_installedVersion, value))
                {
                    _installedVersion = value;
                    OnPropertyChanged(nameof(InstalledVersion));
                }
            }
        }

        // The version that can be installed or updated to. It is null
        // if the installed version is already the latest.
        private NuGetVersion _latestVersion;

        public NuGetVersion LatestVersion
        {
            get
            {
                return _latestVersion;
            }
            set
            {
                if (!VersionEquals(_latestVersion, value))
                {
                    _latestVersion = value;
                    OnPropertyChanged(nameof(LatestVersion));

                    // update tool tip
                    if (_latestVersion != null)
                    {
                        var displayVersion = new DisplayVersion(_latestVersion, string.Empty);
                        LatestVersionToolTip = string.Format(
                            CultureInfo.CurrentCulture,
                            Resources.ToolTip_LatestVersion,
                            displayVersion);
                    }
                    else
                    {
                        LatestVersionToolTip = null;
                    }
                }
            }
        }

        private string _latestVersionToolTip;

        public string LatestVersionToolTip
        {
            get
            {
                return _latestVersionToolTip;
            }
            set
            {
                _latestVersionToolTip = value;
                OnPropertyChanged(nameof(LatestVersionToolTip));
            }
        }

        private bool _selected;

        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected != value)
                {
                    _selected = value;
                    OnPropertyChanged(nameof(Selected));
                }
            }
        }

        private bool VersionEquals(NuGetVersion v1, NuGetVersion v2)
        {
            if (v1 == null && v2 == null)
            {
                return true;
            }

            if (v1 == null)
            {
                return false;
            }

            return v1.Equals(v2, VersionComparison.Default);
        }

        private long? _downloadCount;

        public long? DownloadCount
        {
            get
            {
                return _downloadCount;
            }
            set
            {
                _downloadCount = value;
                OnPropertyChanged(nameof(DownloadCount));
            }
        }

        private string _summary;

        public string Summary
        {
            get
            {
                return _summary;
            }
            set
            {
                if (_summary != value)
                {
                    _summary = value;
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        private PackageStatus _status;

        public PackageStatus Status
        {
            get
            {
                if (_backgroundLoader != null && !_backgroundLoader.LoaderHasBeenRun)
                {
                    _backgroundLoader.LoaderHasBeenRun = true;

                    Task.Run(async () =>
                    {
                        var result = await BackgroundLoader.GetResult();

                        await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        Status = result.Status;
                        LatestVersion = result.LatestVersion;
                        InstalledVersion = result.InstalledVersion;
                    });
                }

                return _status;
            }

            private set
            {
                bool refresh = _status != value;
                _status = value;

                if (refresh)
                {
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        private AlternativePackageManagerProviders _providers;

        public AlternativePackageManagerProviders Providers
        {
            get
            {
                if (_providersLoader != null && !_providersLoader.LoaderHasBeenRun)
                {
                    _providersLoader.LoaderHasBeenRun = true;
                    Task.Run(async () =>
                    {
                        var result = await _providersLoader.GetResult();

                        await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        Providers = result;
                    });
                }

                return _providers;
            }

            private set
            {
                _providers = value;
                OnPropertyChanged(nameof(Providers));
            }
        }

        private BackgroundLoader<AlternativePackageManagerProviders> _providersLoader;

        internal BackgroundLoader<AlternativePackageManagerProviders> ProvidersLoader
        {
            set
            {
                if (_providersLoader != value)
                {
                    _providersLoader = value;
                    OnPropertyChanged(nameof(Providers));
                }
            }
        }

        private BackgroundLoader<BackgroundLoaderResult> _backgroundLoader;

        internal BackgroundLoader<BackgroundLoaderResult> BackgroundLoader
        {
            get
            {
                return _backgroundLoader;
            }

            set
            {
                if (_backgroundLoader != value)
                {
                    _backgroundLoader = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        private Uri _iconUrl;

        public Uri IconUrl
        {
            get
            {
                return _iconUrl;
            }
            set
            {
                if (_iconUrl != value)
                {
                    _iconUrl = value;
                    OnPropertyChanged(nameof(IconUrl));
                }
            }
        }

        public Lazy<Task<IEnumerable<VersionInfo>>> Versions { get; set; }

        protected void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                var e = new PropertyChangedEventArgs(propertyName);
                PropertyChanged(this, e);
            }
        }

        public override string ToString()
        {
            return Id;
        }
    }
}