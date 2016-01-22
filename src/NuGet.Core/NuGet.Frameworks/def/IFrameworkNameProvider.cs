﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace NuGet.Frameworks
{
    public interface IFrameworkNameProvider
    {
        /// <summary>
        /// Returns the official framework identifier for an alias or short name.
        /// </summary>
        bool TryGetIdentifier(string identifierShortName, out string identifier);

        /// <summary>
        /// Gives the short name used for folders in NuGet
        /// </summary>
        bool TryGetShortIdentifier(string identifier, out string identifierShortName);

        /// <summary>
        /// Get the official profile name from the short name.
        /// </summary>
        bool TryGetProfile(string frameworkIdentifier, string profileShortName, out string profile);

        /// <summary>
        /// Returns the shortened version of the profile name.
        /// </summary>
        bool TryGetShortProfile(string frameworkIdentifier, string profile, out string profileShortName);

        /// <summary>
        /// Parses a version string using single digit rules if no dots exist
        /// </summary>
        bool TryGetVersion(string versionString, out Version version);

        /// <summary>
        /// Returns a shortened version. If all digits are single digits no dots will be used.
        /// </summary>
        string GetVersionString(string framework, Version version);

        /// <summary>
        /// Tries to parse the portable profile number out of a profile.
        /// </summary>
        bool TryGetPortableProfileNumber(string profile, out int profileNumber);

        /// <summary>
        /// Looks up the portable profile number based on the framework list.
        /// </summary>
        bool TryGetPortableProfile(IEnumerable<NuGetFramework> supportedFrameworks, out int profileNumber);

        /// <summary>
        /// Returns the frameworks based on a portable profile number.
        /// </summary>
        bool TryGetPortableFrameworks(int profile, out IEnumerable<NuGetFramework> frameworks);

        /// <summary>
        /// Returns the frameworks based on a portable profile number.
        /// </summary>
        bool TryGetPortableFrameworks(int profile, bool includeOptional, out IEnumerable<NuGetFramework> frameworks);

        /// <summary>
        /// Returns the frameworks based on a profile string.
        /// Profile can be either the number in format: Profile=7, or the shortened NuGet version: net45+win8
        /// </summary>
        bool TryGetPortableFrameworks(string profile, bool includeOptional, out IEnumerable<NuGetFramework> frameworks);

        /// <summary>
        /// Parses a shortened portable framework profile list.
        /// Ex: net45+win8
        /// </summary>
        bool TryGetPortableFrameworks(string shortPortableProfiles, out IEnumerable<NuGetFramework> frameworks);

        /// <summary>
        /// Returns ranges of frameworks that are known to be supported by the given portable profile number.
        /// Ex: Profile7 -> netstandard1.1
        /// </summary>
        bool TryGetPortableCompatibilityMappings(int profile, out IEnumerable<FrameworkRange> supportedFrameworkRanges);

        /// <summary>
        /// Returns a list of all possible substitutions where the framework name
        /// have equivalents.
        /// Ex: sl3 -> wp8
        /// </summary>
        bool TryGetEquivalentFrameworks(NuGetFramework framework, out IEnumerable<NuGetFramework> frameworks);

        /// <summary>
        /// Gives all substitutions for a framework range.
        /// </summary>
        bool TryGetEquivalentFrameworks(FrameworkRange range, out IEnumerable<NuGetFramework> frameworks);

        /// <summary>
        /// Returns ranges of frameworks that are known to be supported by the given framework.
        /// Ex: net45 -> native
        /// </summary>
        bool TryGetCompatibilityMappings(NuGetFramework framework, out IEnumerable<FrameworkRange> supportedFrameworkRanges);

        /// <summary>
        /// Returns all sub sets of the given framework.
        /// Ex: .NETFramework -> .NETCore
        /// These will have the same version, but a different framework
        /// </summary>
        bool TryGetSubSetFrameworks(string frameworkIdentifier, out IEnumerable<string> subSetFrameworkIdentifiers);

        /// <summary>
        /// The ascending order of frameworks should be based on the following ordered groups:
        /// 
        /// 1. Non-package-based frameworks in <see cref="IFrameworkMappings.NonPackageBasedFrameworkPrecedence"/>.
        /// 2. Other non-package-based frameworks.
        /// 3. Package-based frameworks in <see cref="IFrameworkMappings.PackageBasedFrameworkPrecedence"/>.
        /// 4. Other package-based frameworks.
        /// 
        /// For group #1 and #3, the order within the group is based on the order of the respective precedence list.
        /// For group #2 and #4, the order is the original order in the incoming list. This should later be made
        /// consistent between different input orderings by using the <see cref="NuGetFrameworkSorter"/>.
        /// </summary>
        int CompareFrameworks(NuGetFramework x, NuGetFramework y);

        /// <summary>
        /// Used to pick between two equivalent frameworks. This is meant to favor the more human-readable
        /// framework. Note that this comparison does not validate that the provided frameworks are indeed
        /// equivalent (e.g. with
        /// <see cref="TryGetEquivalentFrameworks(NuGetFramework, out IEnumerable{NuGetFramework})"/>).
        /// </summary>
        int CompareEquivalentFrameworks(NuGetFramework x, NuGetFramework y);

        /// <summary>
        /// Returns folder short names rewrites.
        /// Ex: dotnet50 -> dotnet
        /// </summary>
        NuGetFramework GetShortNameReplacement(NuGetFramework framework);

        /// <summary>
        /// Returns full name rewrites.
        /// Ex: .NETPlatform,Version=v0.0 -> .NETPlatform,Version=v5.0
        /// </summary>
        NuGetFramework GetFullNameReplacement(NuGetFramework framework);

        IEnumerable<NuGetFramework> GetNetStandardVersions();

        IEnumerable<NuGetFramework> GetCompatibleCandidates();
    }
}
