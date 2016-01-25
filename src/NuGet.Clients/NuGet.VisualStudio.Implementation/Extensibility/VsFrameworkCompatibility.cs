// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.Versioning;
using NuGet.Frameworks;
using NuGet.VisualStudio.Implementation.Resources;

namespace NuGet.VisualStudio
{
    [Export(typeof(IVsFrameworkCompatibility))]
    public class VsFrameworkCompatibility : IVsFrameworkCompatibility
    {
        private readonly IFrameworkNameProvider _frameworkNameProvider;
        private readonly IFrameworkCompatibilityListProvider _compatibilityListProvider;

        public VsFrameworkCompatibility()
        {
            _frameworkNameProvider = DefaultFrameworkNameProvider.Instance;
            _compatibilityListProvider = DefaultCompatibilityListProvider.Instance;
        }

        public IEnumerable<FrameworkName> GetNetStandardFrameworks()
        {
            return _frameworkNameProvider
                .GetNetStandardVersions()
                .OrderBy(f => f.Version)
                .Select(GetFrameworkName);
        }

        public IEnumerable<FrameworkName> GetFrameworksSupportingNetStandard(FrameworkName frameworkName)
        {
            var nuGetFramework = GetNuGetFramework(frameworkName);

            if (!StringComparer.OrdinalIgnoreCase.Equals(
                nuGetFramework.Framework,
                FrameworkConstants.FrameworkIdentifiers.NetStandard))
            {
                throw new ArgumentException(string.Format(
                    VsResources.InvalidNetStandardFramework,
                    frameworkName));
            }

            return _compatibilityListProvider
                .GetFrameworksSupporting(nuGetFramework)
                .OrderBy(f => f, new NuGetFrameworkSorter())
                .Select(GetFrameworkName);
        }

        private NuGetFramework GetNuGetFramework(FrameworkName frameworkName)
        {
            if (frameworkName == null)
            {
                throw new ArgumentNullException(nameof(frameworkName));
            }

            return NuGetFramework.ParseFrameworkName(frameworkName.ToString(), _frameworkNameProvider);
        }

        private FrameworkName GetFrameworkName(NuGetFramework nuGetFramework)
        {
            if (nuGetFramework == null)
            {
                throw new ArgumentNullException(nameof(nuGetFramework));
            }

            return new FrameworkName(nuGetFramework.DotNetFrameworkName);
        }
    }
}
