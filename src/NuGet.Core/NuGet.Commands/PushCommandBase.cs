using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Logging;
using NuGet.Packaging.Core;

namespace NuGet.Commands
{
    public class PushCommandBase
    {
        private PushCommandResource _pushCommandResource;
        private string _userAgent;

        private string _apiKey;
        private int _timeout;
        private string _packagePath;

        ILogger _logger;

        public PushCommandBase(
            string packagePath,
            PushCommandResource pushCommandResource,
            string apiKey,
            string userAgent,
            int timeout,
            ILogger logger)
        {
            _logger = logger;
            _pushCommandResource = pushCommandResource;
            _apiKey = apiKey;
            _timeout = timeout;
            _userAgent = userAgent;
            _packagePath = packagePath;
        }

        public async Task ExecuteCommandAsync()
        {
            // First argument should be the package
            string packagePath = _packagePath;

            string pushEndpoint = _pushCommandResource.GetPushEndpoint();

            if (string.IsNullOrEmpty(_apiKey) && !IsFileSource(pushEndpoint))
            {
                string warning = string.Format(Strings.NoApiKeyFound,
                         CommandLineUtility.GetSourceDisplayName(pushEndpoint));
                _logger.LogWarning(warning);
            }

            var timeout = TimeSpan.FromSeconds(Math.Abs(_timeout));
            if (timeout.TotalSeconds == 0)
            {
                timeout = TimeSpan.FromMinutes(5); // Default to 5 minutes
            }
            var tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(timeout);

            try
            {
                await PushPackage(packagePath, pushEndpoint, _apiKey, tokenSource.Token);

                if (pushEndpoint.Equals(NuGetConstants.DefaultGalleryServerUrl, StringComparison.OrdinalIgnoreCase))
                {
                    await PushSymbols(packagePath, tokenSource.Token);
                }
            }
            catch (HttpRequestException exception)
            {
                //inner exception is more accurate, so surface it.
                if (exception.InnerException != null)
                {
                    throw exception.InnerException;
                }
                else
                {
                    throw exception;
                }
            }
        }

        private async Task PushSymbols(string packagePath, CancellationToken token)
        {
            // Get the symbol package for this package
            string symbolPackagePath = GetSymbolsPath(packagePath);

            // Push the symbols package if it exists
            if (File.Exists(symbolPackagePath))
            {
                string source = NuGetConstants.DefaultSymbolServerUrl;

                if (String.IsNullOrEmpty(_apiKey))
                {
                    _logger.LogWarning(
                        string.Format(Strings.Warning_SymbolServerNotConfigured, Path.GetFileName(symbolPackagePath)));
                }

                await PushPackage(symbolPackagePath, source, _apiKey, token);
            }
        }

        /// <summary>
        /// Get the symbols package from the original package. Removes the .nupkg and adds .symbols.nupkg
        /// </summary>
        private static string GetSymbolsPath(string packagePath)
        {
            string symbolPath = Path.GetFileNameWithoutExtension(packagePath) + ".symbols.nupkg";//TODO: use constant
            string packageDir = Path.GetDirectoryName(packagePath);
            return Path.Combine(packageDir, symbolPath);
        }

        private async Task PushPackage(string packagePath, string pushEndpoint, string apiKey, CancellationToken token)
        {
            var packageUpdater = _pushCommandResource.GetPackageUpdater();

            IEnumerable<string> packagesToPush = GetPackagesToPush(packagePath);

            EnsurePackageFileExists(packagePath, packagesToPush);

            foreach (string packageToPush in packagesToPush)
            {
                await PushPackageCore(pushEndpoint, apiKey, packageUpdater, packageToPush, token);
            }
        }

        private async Task PushPackageCore(string pushEndpoint,
            string apiKey,
            PackageUpdater packageUpdater,
            string packageToPush,
            CancellationToken token)
        {
            // Push the package to the server
            var sourceUri = new Uri(pushEndpoint);
            string sourceName = CommandLineUtility.GetSourceDisplayName(pushEndpoint);
            _logger.LogInformation(
                string.Format(Strings.PushCommandPushingPackage, Path.GetFileName(packageToPush), sourceName));

            await packageUpdater.PushPackage(
                apiKey,
                packageToPush,
                new FileInfo(packageToPush).Length,
                _userAgent,
                _logger,
                token);

            _logger.LogInformation(Strings.PushCommandPackagePushed);
        }

        private static IEnumerable<string> GetPackagesToPush(string packagePath)
        {
            // Ensure packagePath ends with *.nupkg
            packagePath = EnsurePackageExtension(packagePath);
            return PathUtility.PerformWildcardSearch(Directory.GetCurrentDirectory(), packagePath);
        }

        internal static string EnsurePackageExtension(string packagePath)
        {
            if (packagePath.IndexOf('*') == -1)
            {
                // If there's no wildcard in the path to begin with, assume that it's an absolute path.
                return packagePath;
            }
            // If the path does not contain wildcards, we need to add *.nupkg to it.
            if (!packagePath.EndsWith(PackagingCoreConstants.NupkgExtension, StringComparison.OrdinalIgnoreCase))
            {
                if (packagePath.EndsWith("**", StringComparison.OrdinalIgnoreCase))
                {
                    packagePath = packagePath + Path.DirectorySeparatorChar + '*';
                }
                else if (!packagePath.EndsWith("*", StringComparison.OrdinalIgnoreCase))
                {
                    packagePath = packagePath + '*';
                }
                packagePath = packagePath + PackagingCoreConstants.NupkgExtension;
            }
            return packagePath;
        }

        private static void EnsurePackageFileExists(string packagePath, IEnumerable<string> packagesToPush)
        {
            if (!packagesToPush.Any())
            {
                throw new ArgumentException(string.Format(Strings.UnableToFindFile, packagePath));
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
    }
}