// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using NuGet.Common;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NuGet.Commands
{
    public static class PathUtility
    {
        public static bool IsChildOfDirectory(string dir, string candidate)
        {
            if (dir == null)
            {
                throw new ArgumentNullException(nameof(dir));
            }
            if (candidate == null)
            {
                throw new ArgumentNullException(nameof(candidate));
            }
            dir = Path.GetFullPath(dir);
            dir = EnsureTrailingSlash(dir);
            candidate = Path.GetFullPath(candidate);
            return candidate.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
        }

        public static string EnsureTrailingSlash(string path)
        {
            return EnsureTrailingCharacter(path, Path.DirectorySeparatorChar);
        }

        public static string EnsureTrailingForwardSlash(string path)
        {
            return EnsureTrailingCharacter(path, '/');
        }

        private static string EnsureTrailingCharacter(string path, char trailingCharacter)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            // if the path is empty, we want to return the original string instead of a single trailing character.
            if (path.Length == 0 || path[path.Length - 1] == trailingCharacter)
            {
                return path;
            }

            return path + trailingCharacter;
        }

        public static void EnsureParentDirectory(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// Returns path2 relative to path1, with Path.DirectorySeparatorChar as separator
        /// </summary>
        public static string GetRelativePath(string path1, string path2)
        {
            return GetRelativePath(path1, path2, Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Returns path2 relative to path1, with given path separator
        /// </summary>
        public static string GetRelativePath(string path1, string path2, char separator)
        {
            if (string.IsNullOrEmpty(path1))
            {
                throw new ArgumentException("Path must have a value", nameof(path1));
            }

            if (string.IsNullOrEmpty(path2))
            {
                throw new ArgumentException("Path must have a value", nameof(path2));
            }

            StringComparison compare;
            if (RuntimeEnvironmentHelper.IsWindows)
            {
                compare = StringComparison.OrdinalIgnoreCase;
                // check if paths are on the same volume
                if (!string.Equals(Path.GetPathRoot(path1), Path.GetPathRoot(path2)))
                {
                    // on different volumes, "relative" path is just path2
                    return path2;
                }
            }
            else
            {
                compare = StringComparison.Ordinal;
            }

            var index = 0;
            var path1Segments = path1.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var path2Segments = path2.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // if path1 does not end with / it is assumed the end is not a directory
            // we will assume that is isn't a directory by ignoring the last split
            var len1 = path1Segments.Length - 1;
            var len2 = path2Segments.Length;

            // find largest common absolute path between both paths
            var min = Math.Min(len1, len2);
            while (min > index)
            {
                if (!string.Equals(path1Segments[index], path2Segments[index], compare))
                {
                    break;
                }
                // Handle scenarios where folder and file have same name (only if os supports same name for file and directory)
                // e.g. /file/name /file/name/app
                else if ((len1 == index && len2 > index + 1) || (len1 > index && len2 == index + 1))
                {
                    break;
                }
                ++index;
            }

            var path = "";

            // check if path2 ends with a non-directory separator and if path1 has the same non-directory at the end
            if (len1 + 1 == len2 && !string.IsNullOrEmpty(path1Segments[index]) &&
                string.Equals(path1Segments[index], path2Segments[index], compare))
            {
                return path;
            }

            for (var i = index; len1 > i; ++i)
            {
                path += ".." + separator;
            }
            for (var i = index; len2 - 1 > i; ++i)
            {
                path += path2Segments[i] + separator;
            }
            // if path2 doesn't end with an empty string it means it ended with a non-directory name, so we add it back
            if (!string.IsNullOrEmpty(path2Segments[len2 - 1]))
            {
                path += path2Segments[len2 - 1];
            }

            return path;
        }

        public static string GetAbsolutePath(string basePath, string relativePath)
        {
            if (basePath == null)
            {
                throw new ArgumentNullException(nameof(basePath));
            }

            if (relativePath == null)
            {
                throw new ArgumentNullException(nameof(relativePath));
            }

            Uri resultUri = new Uri(new Uri(basePath), new Uri(relativePath, UriKind.Relative));
            return resultUri.LocalPath;
        }

        public static string GetDirectoryName(string path)
        {
            path = path.TrimEnd(Path.DirectorySeparatorChar);
            return path.Substring(Path.GetDirectoryName(path).Length).Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static string GetPathWithForwardSlashes(string path)
        {
            return path.Replace('\\', '/');
        }

        public static string GetPathWithBackSlashes(string path)
        {
            return path.Replace('/', '\\');
        }

        public static string GetPathWithDirectorySeparator(string path)
        {
            if (Path.DirectorySeparatorChar == '/')
            {
                return GetPathWithForwardSlashes(path);
            }
            else
            {
                return GetPathWithBackSlashes(path);
            }
        }

        
        private static Regex WildcardToRegex(string wildcard)
        {
            var pattern = Regex.Escape(wildcard);
            if (Path.DirectorySeparatorChar == '/')
            {
                // regex wildcard adjustments for *nix-style file systems
                pattern = pattern
                    .Replace(@"\*\*/", ".*") //For recursive wildcards /**/, include the current directory.
                    .Replace(@"\*\*", ".*") // For recursive wildcards that don't end in a slash e.g. **.txt would be treated as a .txt file at any depth
                    .Replace(@"\*", @"[^/]*(/)?") // For non recursive searches, limit it any character that is not a directory separator
                    .Replace(@"\?", "."); // ? translates to a single any character
            }
            else
            {
                // regex wildcard adjustments for Windows-style file systems
                pattern = pattern
                    .Replace("/", @"\\") // On Windows, / is treated the same as \.
                    .Replace(@"\*\*\\", ".*") //For recursive wildcards \**\, include the current directory.
                    .Replace(@"\*\*", ".*") // For recursive wildcards that don't end in a slash e.g. **.txt would be treated as a .txt file at any depth
                    .Replace(@"\*", @"[^\\]*(\\)?") // For non recursive searches, limit it any character that is not a directory separator
                    .Replace(@"\?", "."); // ? translates to a single any character
            }

            return new Regex('^' + pattern + '$', RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);
        }


        public static IEnumerable<string> PerformWildcardSearch(string basePath, string searchPath)
        {
            string normalizedBasePath;
            var searchResults = PerformWildcardSearchInternal(basePath, searchPath, includeEmptyDirectories: false, normalizedBasePath: out normalizedBasePath);
            return searchResults.Select(s => s.Path);
        }

        private static IEnumerable<SearchPathResult> PerformWildcardSearchInternal(string basePath, string searchPath, bool includeEmptyDirectories, out string normalizedBasePath)
        {
            bool searchDirectory = false;
            
            // If the searchPath ends with \ or /, we treat searchPath as a directory,
            // and will include everything under it, recursively
            if (IsDirectoryPath(searchPath))
            {
                searchPath = searchPath + "**" + Path.DirectorySeparatorChar + "*";
                searchDirectory = true;
            }

            basePath = NormalizeBasePath(basePath, ref searchPath);
            normalizedBasePath = GetPathToEnumerateFrom(basePath, searchPath);

            // Append the basePath to searchPattern and get the search regex. We need to do this because the search regex is matched from line start.
            Regex searchRegex = WildcardToRegex(Path.Combine(basePath, searchPath));

            // This is a hack to prevent enumerating over the entire directory tree if the only wildcard characters are the ones in the file name. 
            // If the path portion of the search path does not contain any wildcard characters only iterate over the TopDirectory.
            SearchOption searchOption = SearchOption.AllDirectories;
            // (a) Path is not recursive search
            bool isRecursiveSearch = searchPath.IndexOf("**", StringComparison.OrdinalIgnoreCase) != -1;
            // (b) Path does not have any wildcards.
            bool isWildcardPath = Path.GetDirectoryName(searchPath).Contains('*');
            if (!isRecursiveSearch && !isWildcardPath)
            {
                searchOption = SearchOption.TopDirectoryOnly;
            }

            // Starting from the base path, enumerate over all files and match it using the wildcard expression provided by the user.
            // Note: We use Directory.GetFiles() instead of Directory.EnumerateFiles() here to support Mono
            var matchedFiles = from file in Directory.GetFiles(normalizedBasePath, "*.*", searchOption)
                               where searchRegex.IsMatch(file)
                               select new SearchPathResult(file, isFile: true);

            if (!includeEmptyDirectories)
            {
                return matchedFiles;
            }

            // retrieve empty directories
            // Note: We use Directory.GetDirectories() instead of Directory.EnumerateDirectories() here to support Mono
            var matchedDirectories = from directory in Directory.GetDirectories(normalizedBasePath, "*.*", searchOption)
                                     where searchRegex.IsMatch(directory) && IsEmptyDirectory(directory)
                                     select new SearchPathResult(directory, isFile: false);

            if (searchDirectory && IsEmptyDirectory(normalizedBasePath))
            {
                matchedDirectories = matchedDirectories.Concat(new [] { new SearchPathResult(normalizedBasePath, isFile: false) });
            }

            return matchedFiles.Concat(matchedDirectories);
        }

        internal static string GetPathToEnumerateFrom(string basePath, string searchPath)
        {
            string basePathToEnumerate;
            int wildcardIndex = searchPath.IndexOf('*');
            if (wildcardIndex == -1)
            {
                // For paths without wildcard, we could either have base relative paths (such as lib\foo.dll) or paths outside the base path
                // (such as basePath: C:\packages and searchPath: D:\packages\foo.dll)
                // In this case, Path.Combine would pick up the right root to enumerate from.
                var searchRoot = Path.GetDirectoryName(searchPath);
                basePathToEnumerate = Path.Combine(basePath, searchRoot);
            }
            else
            {
                // If not, find the first directory separator and use the path to the left of it as the base path to enumerate from.
                int directorySeparatoryIndex = searchPath.LastIndexOf(Path.DirectorySeparatorChar, wildcardIndex);
                if (directorySeparatoryIndex == -1)
                {
                    // We're looking at a path like "NuGet*.dll", NuGet*\bin\release\*.dll
                    // In this case, the basePath would continue to be the path to begin enumeration from.
                    basePathToEnumerate = basePath;
                }
                else
                {
                    string nonWildcardPortion = searchPath.Substring(0, directorySeparatoryIndex);
                    basePathToEnumerate = Path.Combine(basePath, nonWildcardPortion);
                }
            }
            return basePathToEnumerate;
        }

        internal static string NormalizeBasePath(string basePath, ref string searchPath)
        {
            const string relativePath = @"..\";

            // If no base path is provided, use the current directory.
            basePath = String.IsNullOrEmpty(basePath) ? @".\" : basePath;

            // If the search path is relative, transfer the ..\ portion to the base path. 
            // This needs to be done because the base path determines the root for our enumeration.
            while (searchPath.StartsWith(relativePath, StringComparison.OrdinalIgnoreCase))
            {
                basePath = Path.Combine(basePath, relativePath);
                searchPath = searchPath.Substring(relativePath.Length);
            }

            return Path.GetFullPath(basePath);
        }

        internal static bool IsDirectoryPath(string path)
        {
            return path != null && path.Length > 1 &&
                (path[path.Length - 1] == Path.DirectorySeparatorChar ||
                path[path.Length - 1] == Path.AltDirectorySeparatorChar);
        }

        private static bool IsEmptyDirectory(string directory)
        {
            return !Directory.EnumerateFileSystemEntries(directory).Any();
        }

        private struct SearchPathResult
        {
            private readonly string _path;
            private readonly bool _isFile;

            public string Path
            {
                get
                {
                    return _path;
                }
            }

            public bool IsFile
            {
                get
                {
                    return _isFile;
                }
            }

            public SearchPathResult(string path, bool isFile)
            {
                _path = path;
                _isFile = isFile;
            }
        }
    }
}