//using System;
//using System.Collections.Generic;
//using System.Globalization;
//using System.IO;
//using System.Linq;
//using System.Net;
//using System.Net.Http;
//using System.Threading;
//using System.Threading.Tasks;
//using NuGet.Common;
//using NuGet.Configuration;
//using NuGet.Protocol.Core.Types;
//using NuGet.Logging;
//using NuGet.Packaging.Core;

//namespace NuGet.Commands
//{
//     public class PushCommand
//    {
//        private PushCommandResource _pushCommandResource;
//        private ISourceRepositoryProvider _sourceRepositoryProvider;
//        private string _userAgent;

//        //TODO: convert them into privates
//        public string Source { get; set; }
//        public string _sourceDisplayName;

//        public string ApiKey { get; set; }

//        public int Timeout { get; set; }

//        public bool DisableBuffering { get; set; }
        
//        public string PackagePath { get; set; }


//        ILogger _logger;

//        //TODO: re-order
//        public PushCommand(ILogger logger, 
//            ISourceRepositoryProvider sourceRepositoryProvider,
//            string packagePath, 
//            string source, 
//            string sourceDisplayname,
//            string apiKey,
//            string userAgent,
//            int timeout)
//        {
//            _logger = logger;
//            _sourceRepositoryProvider = sourceRepositoryProvider;
//            Source = source;
//            _sourceDisplayName = sourceDisplayname;
//            ApiKey = apiKey;
//            Timeout = timeout;
//            _userAgent = userAgent;
//            PackagePath = packagePath;
//        }

//        public async Task ExecuteCommandAsync()
//        {
//            // First argument should be the package
//            string packagePath = PackagePath;

//            //TODO: do it from command line
//            //string source = ResolveSource(packagePath, ConfigurationDefaults.Instance.DefaultPushSource);
//            await GetPushCommandResource(Source);

//            string pushEndpoint = string.Empty;
//            if (_pushCommandResource != null)
//            {
//                pushEndpoint = _pushCommandResource.GetPushEndpoint();
//            }
//            if (string.IsNullOrEmpty(pushEndpoint))
//            {
//                var message = string.Format(
//                    "PushCommand_PushNotSupported",
//                    Source);

//                _logger.LogWarning(message);
//                return;
//            }

//            if (string.IsNullOrEmpty(ApiKey) && !IsFileSource(pushEndpoint))
//            {
//                _logger.LogWarning("TODO");
//                    //LocalizedResourceManager.GetString("NoApiKeyFound"),
//                    //CommandLineUtility.GetSourceDisplayName(pushEndpoint));
//            }

//            var timeout = TimeSpan.FromSeconds(Math.Abs(Timeout));
//            if (timeout.TotalSeconds == 0)
//            {
//                timeout = TimeSpan.FromMinutes(5); // Default to 5 minutes
//            }
//            var tokenSource = new CancellationTokenSource();
//            tokenSource.CancelAfter(timeout);

//            try
//            {
//                await PushPackage(packagePath, pushEndpoint, ApiKey, tokenSource.Token);

//                if (pushEndpoint.Equals(NuGetConstants.DefaultGalleryServerUrl, StringComparison.OrdinalIgnoreCase))
//                {
//                    await PushSymbols(packagePath, tokenSource.Token);
//                }
//            }
//            catch (HttpRequestException exception)
//            {
//                //WebException contains more detail, so surface it.
//                if (exception.InnerException is WebException)
//                {
//                    throw exception.InnerException;
//                }
//                else
//                {
//                    throw exception;
//                }
//            }
//        }

//        private async Task GetPushCommandResource(string source)
//        {
//            var packageSource = new Configuration.PackageSource(source);
//            var sourceRepository = _sourceRepositoryProvider.CreateRepository(packageSource);
//            _pushCommandResource = await sourceRepository.GetResourceAsync<PushCommandResource>();
//        }

//        //private string ResolveSource(string packagePath, string configurationDefaultPushSource = null)
//        //{
//        //    string source = Source;

//        //    if (String.IsNullOrEmpty(source))
//        //    {
//        //        source = SettingsUtility.GetConfigValue(Settings, "DefaultPushSource");
//        //    }

//        //    if (String.IsNullOrEmpty(source))
//        //    {
//        //        source = configurationDefaultPushSource;
//        //    }

//        //    if (!String.IsNullOrEmpty(source))
//        //    {
//        //        source = SourceProvider.ResolveAndValidateSource(source);
//        //    }
//        //    else
//        //    {
//        //        source = packagePath.EndsWith(PackCommand.SymbolsExtension, StringComparison.OrdinalIgnoreCase)
//        //            ? NuGetConstants.DefaultSymbolServerUrl
//        //            : NuGetConstants.DefaultGalleryServerUrl;
//        //    }
//        //    return source;
//        //}

//        private async Task PushSymbols(string packagePath, CancellationToken token)
//        {
//            // Get the symbol package for this package
//            string symbolPackagePath = GetSymbolsPath(packagePath);

//            // Push the symbols package if it exists
//            if (File.Exists(symbolPackagePath))
//            {
//                string source = NuGetConstants.DefaultSymbolServerUrl;

//                // See if the api key exists
//                string apiKey = GetApiKey(source);

//                if (String.IsNullOrEmpty(apiKey))
//                {
//                    _logger.LogWarning("TODO");
//                        //LocalizedResourceManager.GetString("Warning_SymbolServerNotConfigured"),
//                        //Path.GetFileName(symbolPackagePath),
//                        //LocalizedResourceManager.GetString("DefaultSymbolServer"));
//                }

//                await PushPackage(symbolPackagePath, source, apiKey, token);
//            }
//        }

//        /// <summary>
//        /// Get the symbols package from the original package. Removes the .nupkg and adds .symbols.nupkg
//        /// </summary>
//        private static string GetSymbolsPath(string packagePath)
//        {
//            string symbolPath = Path.GetFileNameWithoutExtension(packagePath) +  ".symbols.nupkg";//TODO: use constant
//            string packageDir = Path.GetDirectoryName(packagePath);
//            return Path.Combine(packageDir, symbolPath);
//        }

//        private async Task PushPackage(string packagePath, string source, string apiKey, CancellationToken token)
//        {
//            var packageServer = _pushCommandResource.GetPackageUpdater();

//            IEnumerable<string> packagesToPush = GetPackagesToPush(packagePath);

//            EnsurePackageFileExists(packagePath, packagesToPush);

//            foreach (string packageToPush in packagesToPush)
//            {
//                await PushPackageCore(source, apiKey, packageServer, packageToPush, token);
//            }
//        }

//        private async Task PushPackageCore(string source,
//            string apiKey,
//            PackageUpdater packageServer,
//            string packageToPush,
//            CancellationToken token)
//        {
//            // Push the package to the server
//            var sourceUri = new Uri(source);

//            string sourceName = _sourceDisplayName;
//            _logger.LogInformation("TODO");
//                //LocalizedResourceManager.GetString("PushCommandPushingPackage"),
//                //Path.GetFileName(packageToPush), sourceName);

//            await packageServer.PushPackage(
//                apiKey,
//                packageToPush,
//                new FileInfo(packageToPush).Length,
//                _userAgent,
//                _logger, 
//                token);

//            _logger.LogInformation("TODO");// (LocalizedResourceManager.GetString("PushCommandPackagePushed"));
//        }

//        private static IEnumerable<string> GetPackagesToPush(string packagePath)
//        {
//            // Ensure packagePath ends with *.nupkg
//            packagePath = EnsurePackageExtension(packagePath);
//            return PathResolver.PerformWildcardSearch(Environment.CurrentDirectory, packagePath);
//        }

//        internal static string EnsurePackageExtension(string packagePath)
//        {
//            if (packagePath.IndexOf('*') == -1)
//            {
//                // If there's no wildcard in the path to begin with, assume that it's an absolute path.
//                return packagePath;
//            }
//            // If the path does not contain wildcards, we need to add *.nupkg to it.
//            if (!packagePath.EndsWith(PackagingCoreConstants.NupkgExtension, StringComparison.OrdinalIgnoreCase))
//            {
//                if (packagePath.EndsWith("**", StringComparison.OrdinalIgnoreCase))
//                {
//                    packagePath = packagePath + Path.DirectorySeparatorChar + '*';
//                }
//                else if (!packagePath.EndsWith("*", StringComparison.OrdinalIgnoreCase))
//                {
//                    packagePath = packagePath + '*';
//                }
//                packagePath = packagePath + PackagingCoreConstants.NupkgExtension;
//            }
//            return packagePath;
//        }

//        private static void EnsurePackageFileExists(string packagePath, IEnumerable<string> packagesToPush)
//        {
//            if (!packagesToPush.Any())
//            {
//                throw new ArgumentException(String.Format(CultureInfo.CurrentCulture, "TODO"/*LocalizedResourceManager.GetString("UnableToFindFile")*/, packagePath));
//            }
//        }

//        //private string GetApiKey(string source)
//        //{
//        //    if (!String.IsNullOrEmpty(ApiKey))
//        //    {
//        //        return ApiKey;
//        //    }

//        //    string apiKey = null;

//        //    // Second argument, if present, should be the API Key
//        //    if (Arguments.Count > 1)
//        //    {
//        //        apiKey = Arguments[1];
//        //    }

//        //    // If the user did not pass an API Key look in the config file
//        //    if (String.IsNullOrEmpty(apiKey))
//        //    {
//        //        apiKey = SettingsUtility.GetDecryptedValue(Settings, "apikeys", source);
//        //    }

//        //    return apiKey;
//        //}

//        /// <summary>
//        /// Indicates whether the specified source is a file source, such as: \\a\b, c:\temp, etc.
//        /// </summary>
//        /// <param name="source">The source to test.</param>
//        /// <returns>true if the source is a file source; otherwise, false.</returns>
//        private static bool IsFileSource(string source)
//        {
//            Uri uri;
//            if (Uri.TryCreate(source, UriKind.RelativeOrAbsolute, out uri))
//            {
//                return uri.IsFile;
//            }
//            else
//            {
//                return false;
//            }
//        }
//    }
//}