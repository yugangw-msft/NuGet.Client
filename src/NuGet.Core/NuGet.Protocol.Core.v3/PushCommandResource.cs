using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Logging;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NuGet.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NuGet.Protocol.Core.v3;
using System.Globalization;

namespace NuGet.Protocol.Core.Types
{
    //TODO, consider creating DeleteCommandResource for delete specific resorce.
    public class PushCommandResource : INuGetResource
    {
        private const string ServiceEndpoint = "/api/v2/package";
        private const string ApiKeyHeader = "X-NuGet-ApiKey";

        private HttpSource _httpSource;
        private Uri _source;

        public PushCommandResource(string pushEndpoint,
            HttpSource httpSource)
        {
            PushEndpoint = pushEndpoint;
            if (!string.IsNullOrEmpty(pushEndpoint))
            {
                _source = new Uri(pushEndpoint);
            }
            _httpSource = httpSource;
        }

        public string PushEndpoint { get; private set; }

        public async Task Push(string packagePath,
            string source,
            int timeoutInSecond,
            Func<string, string> getApiKey,
            ILogger log)
        {
            if (string.IsNullOrEmpty(PushEndpoint))
            {
                var message = string.Format(CultureInfo.CurrentCulture,
                    Strings.PushCommand_PushNotSupported,
                    source);

                log.LogWarning(message);
                return;
            }

            var timeout = TimeSpan.FromSeconds(Math.Abs(timeoutInSecond));
            if (timeout.TotalSeconds == 0)
            {
                timeout = TimeSpan.FromMinutes(5); // Default to 5 minutes
            }
            var tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(timeout);

            string apiKey = getApiKey(PushEndpoint);

            await PushPackage(packagePath, PushEndpoint, apiKey, log, tokenSource.Token);
            if (PushEndpoint.Equals(NuGetConstants.DefaultGalleryServerUrl, StringComparison.OrdinalIgnoreCase))
            {
                await PushSymbols(packagePath, apiKey, log, tokenSource.Token);
            }
        }

        public async Task Delete(string packageId,
            string packageVersion,
            string source,
            Func<string, string> getApiKey,
            Func<string, bool> confirm, 
            ILogger log)
        {
            string sourceDisplayName = GetSourceDisplayName(source);
            string apiKey = getApiKey(source);
            if (String.IsNullOrEmpty(apiKey))
            {
                log.LogWarning(string.Format(CultureInfo.CurrentCulture,
                    Strings.NoApiKeyFound, 
                    sourceDisplayName));
            }

            if (confirm(string.Format(CultureInfo.CurrentCulture, Strings.DeleteCommandConfirm, packageId, packageVersion, sourceDisplayName)))
            {
                log.LogWarning(string.Format(
                    Strings.DeleteCommandDeletingPackage,
                    packageId,
                    packageVersion,
                    sourceDisplayName
                    ));
                await DeletePackage(apiKey, packageId, packageVersion, log, CancellationToken.None);
                log.LogInformation(string.Format(CultureInfo.CurrentCulture,
                    Strings.DeleteCommandDeletedPackage, 
                    packageId, 
                    packageVersion));
            }
            else
            {
                log.LogInformation(Strings.DeleteCommandCanceled);
            }
        }

        private async Task PushSymbols(string packagePath, string apiKey, ILogger log, CancellationToken token)
        {
            // Get the symbol package for this package
            string symbolPackagePath = GetSymbolsPath(packagePath);

            // Push the symbols package if it exists
            if (File.Exists(symbolPackagePath))
            {
                string source = NuGetConstants.DefaultSymbolServerUrl;

                // See if the api key exists

                if (String.IsNullOrEmpty(apiKey))
                {
                    log.LogWarning(string.Format(CultureInfo.CurrentCulture,
                        Strings.Warning_SymbolServerNotConfigured,
                        Path.GetFileName(symbolPackagePath),
                        Strings.DefaultSymbolServer));
                }

                await PushPackage(symbolPackagePath, source, apiKey, log, token);
            }
        }

        /// <summary>
        /// Get the symbols package from the original package. Removes the .nupkg and adds .symbols.nupkg
        /// </summary>
        private static string GetSymbolsPath(string packagePath)
        {
            string symbolPath = Path.GetFileNameWithoutExtension(packagePath) + NuGetConstants.SymbolsExtension;
            string packageDir = Path.GetDirectoryName(packagePath);
            return Path.Combine(packageDir, symbolPath);
        }

        private async Task PushPackage(string packagePath, string source, string apiKey, ILogger log, CancellationToken token)
        {
            if (string.IsNullOrEmpty(apiKey) && !IsFileSource(PushEndpoint))
            {
                log.LogWarning(string.Format(CultureInfo.CurrentCulture,
                    Strings.NoApiKeyFound,
                    GetSourceDisplayName(PushEndpoint)));
            }

            IEnumerable<string> packagesToPush = GetPackagesToPush(packagePath);

            EnsurePackageFileExists(packagePath, packagesToPush);

            foreach (string packageToPush in packagesToPush)
            {
                await PushPackageCore(source, apiKey, packageToPush, log, token);
            }
        }

        private async Task PushPackageCore(string source,
            string apiKey,
            string packageToPush,
            ILogger log,
            CancellationToken token)
        {
            // Push the package to the server
            var sourceUri = new Uri(source);
            string sourceName = GetSourceDisplayName(source);
            log.LogInformation(string.Format(CultureInfo.CurrentCulture,
                Strings.PushCommandPushingPackage,
                Path.GetFileName(packageToPush),
                sourceName));

            await PushPackage(
                apiKey,
                packageToPush,
                new FileInfo(packageToPush).Length,
                log,
                token);

            log.LogInformation(string.Format(CultureInfo.CurrentCulture, Strings.PushCommandPackagePushed));
        }

        private static string GetSourceDisplayName(string source)
        {
            if (String.IsNullOrEmpty(source) || source.Equals(NuGetConstants.DefaultGalleryServerUrl, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.LiveFeed + " (" + NuGetConstants.DefaultGalleryServerUrl + ")";
            }
            if (source.Equals(NuGetConstants.DefaultSymbolServerUrl, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.DefaultSymbolServer + " (" + NuGetConstants.DefaultSymbolServerUrl + ")";
            }
            return "'" + source + "'";
        }

        private static IEnumerable<string> GetPackagesToPush(string packagePath)
        {
            // Ensure packagePath ends with *.nupkg
            packagePath = EnsurePackageExtension(packagePath);
            return PerformWildcardSearch(Directory.GetCurrentDirectory(), packagePath);
        }

        private static string EnsurePackageExtension(string packagePath)
        {
            if (packagePath.IndexOf('*') == -1)
            {
                // If there's no wildcard in the path to begin with, assume that it's an absolute path.
                return packagePath;
            }
            // If the path does not contain wildcards, we need to add *.nupkg to it.
            if (!packagePath.EndsWith(NuGetConstants.PackageExtension, StringComparison.OrdinalIgnoreCase))
            {
                if (packagePath.EndsWith("**", StringComparison.OrdinalIgnoreCase))
                {
                    packagePath = packagePath + Path.DirectorySeparatorChar + '*';
                }
                else if (!packagePath.EndsWith("*", StringComparison.OrdinalIgnoreCase))
                {
                    packagePath = packagePath + '*';
                }
                packagePath = packagePath + NuGetConstants.PackageExtension;
            }
            return packagePath;
        }

        private static void EnsurePackageFileExists(string packagePath, IEnumerable<string> packagesToPush)
        {
            if (!packagesToPush.Any())
            {
                throw new ArgumentException(String.Format(CultureInfo.CurrentCulture,
                    Strings.UnableToFindFile,
                    packagePath));
            }
        }

        /// <summary>
        /// Indicates whether the specified source is a file source, such as: \\a\b, c:\temp, etc.
        /// </summary>
        /// <param name="source">The source to test.</param>
        /// <returns>true if the source is a file source; otherwise, false.</returns>
        private static bool IsFileSource(string source)
        {
            Uri uri;
            if (Uri.TryCreate(source, UriKind.RelativeOrAbsolute, out uri))
            {
                return uri.IsFile;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Pushes a package to the Source.
        /// </summary>
        /// <param name="apiKey">API key to be used to push the package.</param>
        /// <param name="pathToPackage">The path of package to be pushed.</param>
        /// <param name="packageSize">The size of package to be pushed.</param>
        /// <param name="logger">The logger</param>
        /// <param name="token">The cancellation token</param>
        private async Task PushPackage(string apiKey,
            string pathToPackage,
            long packageSize,
            ILogger logger,
            CancellationToken token)
        {
            if (_source.IsFile)
            {
                PushPackageToFileSystem(pathToPackage);
            }
            else
            {
                await PushPackageToServer(apiKey, pathToPackage, packageSize, logger, token);
            }
        }

        /// <summary>
        /// Pushes a package to the Http server.
        /// </summary>
        /// <param name="apiKey">API key to be used to push the package.</param>
        /// <param name="pathToPackage">The path of the package to be pushed </param>
        /// <param name="packageSzie">Size of the package to be pushed.</param>
        /// <param name="timeout">Time in milliseconds to timeout the server request.</param>
        /// <param name="logger">The logger</param>
        /// <param name="token">The cancellation token</param>
        private async Task PushPackageToServer(
            string apiKey,
            string pathToPackage,
            long packageSzie,
            ILogger logger,
            CancellationToken token)
        {
            HttpRequestMessage request = null;
            HttpResponseMessage response = null;

            try
            {
                request = CreateRequest(null, pathToPackage, apiKey);
                response = await _httpSource.SendAsync(request,
                    currentRequest => { return request = CreateRequest(currentRequest, pathToPackage, apiKey); },
                    logger,
                    token);
            }
            finally
            {
                if (request != null)
                {
                    request.Dispose();
                }
            };

            response.EnsureSuccessStatusCode();
        }

        private HttpRequestMessage CreateRequest(HttpRequestMessage currentRequest,
            string pathToPackage,
            string apiKey)
        {
            if (currentRequest != null)
            {
                //this should dispose the content, the file stream underneath, and everything.
                currentRequest.Dispose();
            }
            var fileStream = new FileStream(pathToPackage, FileMode.Open, FileAccess.Read, FileShare.Read);
            var request = new HttpRequestMessage(HttpMethod.Put, GetServiceEndpointUrl(string.Empty));
            var content = new MultipartFormDataContent();
            var packageContent = new StreamContent(fileStream);
            packageContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            content.Add(packageContent, "package", "package.nupkg");
            request.Content = content;
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Add(ApiKeyHeader, apiKey);
            }
            return request;
        }

        /// <summary>
        /// Pushes a package to a FileSystem.
        /// </summary>
        /// <param name="pathToPackage">The path of package to be pushed.</param>
        private void PushPackageToFileSystem(string pathToPackage)
        {
            string root = _source.LocalPath;
            PackageArchiveReader reader = new PackageArchiveReader(pathToPackage);
            PackageIdentity packageIdentity = reader.GetIdentity();

            //TODD: support V3 repo style if detect it is
            var pathResolver = new PackagePathResolver(root, useSideBySidePaths: true);
            var packageFileName = pathResolver.GetPackageFileName(packageIdentity);

            string fullPath = Path.Combine(root, packageFileName);
            File.Copy(pathToPackage, fullPath, true);
        }

        /// <summary>
        /// Deletes a package from a Http server.
        /// </summary>
        /// <param name="apiKey">API key to be used to delete the package.</param>
        /// <param name="packageId">The package Id.</param>
        /// <param name="packageVersion">The package version.</param>
        /// <param name="logger">The logger</param>
        /// <param name="token">The cancellation token</param>
        private async Task DeletePackage(string apiKey,
            string packageId,
            string packageVersion,
            ILogger logger,
            CancellationToken token)
        {
            var sourceUri = GetServiceEndpointUrl(string.Empty);
            if (sourceUri.IsFile)
            {
                DeletePackageFromFileSystem(packageId, packageVersion, logger);
            }
            else
            {
                await DeletePackageFromServer(apiKey, packageId, packageVersion, logger, token);
            }
        }

        /// <summary>
        /// Deletes a package from a Http server
        /// </summary>
        /// <param name="apiKey">API key to be used to delete the package.</param>
        /// <param name="packageId">The package Id.</param>
        /// <param name="packageVersion">The package Id.</param>
        /// <param name="logger">The logger</param>
        /// <param name="token">The cancellation token</param>
        private async Task DeletePackageFromServer(string apiKey,
            string packageId,
            string packageVersion,
            ILogger logger,
            CancellationToken token)
        {
            // Review: Do these values need to be encoded in any way?
            var url = String.Join("/", packageId, packageVersion);
            using (var request = new HttpRequestMessage(HttpMethod.Delete, GetServiceEndpointUrl(url)))
            {
                if (!string.IsNullOrEmpty(apiKey))
                {
                    request.Headers.Add(ApiKeyHeader, apiKey);
                }
                var response = await _httpSource.SendAsync(request, null, logger, token);
                response.EnsureSuccessStatusCode();
            }
        }

        /// <summary>
        /// Deletes a package from a FileSystem.
        /// </summary>
        /// <param name="packageId">The package Id.</param>
        /// <param name="packageVersion">The package Id.</param>
        /// <param name="logger">The logger</param>
        private void DeletePackageFromFileSystem(string packageId, string packageVersion, ILogger logger)
        {
            string root = _source.LocalPath;
            var resolver = new PackagePathResolver(_source.AbsolutePath, useSideBySidePaths: true);
            resolver.GetPackageFileName(new Packaging.Core.PackageIdentity(packageId, new NuGetVersion(packageVersion)));
            var packageIdentity = new PackageIdentity(packageId, new NuGetVersion(packageVersion));
            var packageFileName = resolver.GetPackageFileName(packageIdentity);

            var fullPath = Path.Combine(root, packageFileName);
            MakeFileWritable(fullPath);
            File.Delete(fullPath);
        }

        /// <summary>
        /// Remove the read-only flag.
        /// </summary>
        /// <param name="fullPath">The file path</param>
        private void MakeFileWritable(string fullPath)
        {
            FileAttributes attributes = File.GetAttributes(fullPath);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(fullPath, attributes & ~FileAttributes.ReadOnly);
            }
        }

        /// <summary>
        /// Calculates the URL to the package
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private Uri GetServiceEndpointUrl(string path)
        {
            var baseUri = EnsureTrailingSlash(PushEndpoint);
            Uri requestUri;
            if (String.IsNullOrEmpty(baseUri.AbsolutePath.TrimStart('/')))
            {
                // If there's no host portion specified, append the url to the client.
                requestUri = new Uri(baseUri, ServiceEndpoint + '/' + path);
            }
            else
            {
                requestUri = new Uri(baseUri, path);
            }
            return requestUri;
        }

        /// <summary>
        /// Ensure a trailing slash at the end
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static Uri EnsureTrailingSlash(string value)
        {
            if (!value.EndsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                value += "/";
            }

            return new Uri(value);
        }

        //Port from NuGet2\src\Core\Authoring\PathResolver.cs
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

        private static IEnumerable<string> PerformWildcardSearch(string basePath, string searchPath)
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
                matchedDirectories = matchedDirectories.Concat(new[] { new SearchPathResult(normalizedBasePath, isFile: false) });
            }

            return matchedFiles.Concat(matchedDirectories);
        }

        private static string GetPathToEnumerateFrom(string basePath, string searchPath)
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

        private static string NormalizeBasePath(string basePath, ref string searchPath)
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

        private static bool IsDirectoryPath(string path)
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
