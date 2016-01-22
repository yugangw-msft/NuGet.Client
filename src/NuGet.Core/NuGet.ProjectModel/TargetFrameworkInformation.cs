// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using NuGet.Frameworks;
using NuGet.LibraryModel;

namespace NuGet.ProjectModel
{
    public class TargetFrameworkInformation
    {
        public NuGetFramework FrameworkName { get; set; }

        public IList<LibraryDependency> Dependencies { get; set; }

        /// <summary>
        /// Fallback frameworks (in order) to use when no compatible items
        /// were found for <see cref="FrameworkName"/>.
        /// </summary>
        public IList<NuGetFramework> Imports { get; set; }

        /// <summary>
        /// Display warnings when the Imports framework is used.
        /// </summary>
        public bool Warn { get; set; }

        public TargetFrameworkInformation()
        {
            Dependencies = new List<LibraryDependency>();
            Imports = new List<NuGetFramework>();
        }
    }
}
