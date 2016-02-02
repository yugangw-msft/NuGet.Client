﻿using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Logging;
using NuGet.Packaging;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NuGet.Configuration;
using NuGet.Protocol.Core;
using NuGet.Packaging.Core;
using System.Linq;

namespace NuGet.Protocol.Core.Types
{
    public class PackageUpdater
    {
        private const string ServiceEndpoint = "/api/v2/package";
        private const string ApiKeyHeader = "X-NuGet-ApiKey";
        private const int MaxRediretionCount = 20;
        private readonly Func<Task<HttpHandlerResource>> _messageHandlerFactory;
        private readonly Func<Uri, CancellationToken, Task<ICredentials>> _promptForCredentials;
        private readonly Action<Uri, ICredentials> _credentialsSuccessfullyUsed;

        private Uri _baseUri;
        private Uri _source;

        public PackageUpdater(string source,
            Func<Task<HttpHandlerResource>> messageHandlerFactory,
            Func<Uri, CancellationToken, Task<ICredentials>> promptForCredentials,
            Action<Uri, ICredentials> credentialsSuccessfullyUsed)
        {
            if (String.IsNullOrEmpty(source))
            {
                throw new ArgumentNullException(nameof(source));
            }

            _source = new Uri(source);
            _baseUri = EnsureTrailingSlash(source);
            _messageHandlerFactory = messageHandlerFactory;
            _promptForCredentials = promptForCredentials;
            _credentialsSuccessfullyUsed = credentialsSuccessfullyUsed;
        }

        /// <summary>
        /// Pushes a package to the Source.
        /// </summary>
        /// <param name="apiKey">API key to be used to push the package.</param>
        /// <param name="package">The package to be pushed.</param>
        /// <param name="timeout">Time in milliseconds to timeout the server request.</param>
        /// <param name="disableBuffering">Indicates if HttpWebRequest buffering should be disabled.</param>
        public async Task PushPackage(string apiKey,
            string pathToPackage,
            long packageSize,
            string userAgent,
            ILogger logger,
            CancellationToken token)
        {
            if (_source.IsFile)
            {
                PushPackageToFileSystem(pathToPackage);
            }
            else
            {
                await PushPackageToServer(apiKey, pathToPackage, packageSize, userAgent, logger, token);
            }
        }

        /// <summary>
        /// Pushes a package to the server that is represented by the stream.
        /// </summary>
        /// <param name="apiKey">API key to be used to push the package.</param>
        /// <param name="packageStreamFactory">A delegate which can be used to open a stream for the package file.</param>
        /// <param name="contentLength">Size of the package to be pushed.</param>
        /// <param name="timeout">Time in milliseconds to timeout the server request.</param>
        /// <param name="disableBuffering">Disable buffering.</param>
        private async Task PushPackageToServer(
            string apiKey,
            string pathToPackage,
            long packageSzie,
            string userAgent,
            ILogger logger,
            CancellationToken token)
        {
            ICredentials credentials = CredentialStore.Instance.GetCredentials(_baseUri);
            while (true)
            {
                HttpHandlerResource handlerResource = await _messageHandlerFactory();
                using (var client = new HttpClient(handlerResource.ClientHandler))
                using (var fileStream = new FileStream(pathToPackage, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var request = new HttpRequestMessage(HttpMethod.Put, GetServiceEndpointUrl(string.Empty)))
                {
                    UserAgent.SetUserAgent(client, userAgent);
                    if (credentials != null)
                    {
                        handlerResource.ClientHandler.Credentials = credentials;
                    }
                    else
                    {
                        handlerResource.ClientHandler.UseDefaultCredentials = true;
                    }
                    STSAuthHelper.PrepareSTSRequest(_baseUri, CredentialStore.Instance, request);
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        request.Headers.Add(ApiKeyHeader, apiKey);
                    }

                    using (var content = new MultipartFormDataContent())
                    using (var packageContent = new StreamContent(fileStream))
                    {
                        packageContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");

                        content.Add(packageContent, "package", "package.nupkg");

                        logger.LogDebug(string.Format(CultureInfo.CurrentCulture, "PUT: {0}", request.RequestUri));
                        request.Content = content;
                        using (var response = await client.SendAsync(request))
                        {
                            if (response.StatusCode == HttpStatusCode.Unauthorized)
                            {
                                if (STSAuthHelper.TryRetrieveSTSToken(_baseUri, CredentialStore.Instance, response))
                                {
                                    continue;
                                }

                                if (_promptForCredentials != null)
                                {
                                    credentials = await _promptForCredentials(_baseUri, token);
                                }

                                if (credentials != null)
                                {
                                    continue;
                                }
                            }

                            if (_credentialsSuccessfullyUsed != null && credentials != null)
                            {
                                _credentialsSuccessfullyUsed(_baseUri, credentials);

                            }
                            response.EnsureSuccessStatusCode();
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Pushes a package to a FileSystem.
        /// </summary>
        /// <param name="fileSystem">The FileSystem that the package is pushed to.</param>
        /// <param name="package">The package to be pushed.</param>
        private void PushPackageToFileSystem(string pathToPackage)
        {
            string root = _source.LocalPath;
            PackageArchiveReader reader = new PackageArchiveReader(pathToPackage);
            PackageIdentity packageIdentity = reader.GetIdentity();

            //TODD: disucss with nuget team for whether or not support V3 repo style
            var pathResolver = new PackagePathResolver(root, useSideBySidePaths: true);
            var packageFileName = pathResolver.GetPackageFileName(packageIdentity);

            string fullPath = Path.Combine(root, packageFileName);
           
            File.Copy(pathToPackage, fullPath, true);
        }

        /// <summary>
        /// Deletes a package from the Source.
        /// </summary>
        /// <param name="apiKey">API key to be used to delete the package.</param>
        /// <param name="packageId">The package Id.</param>
        /// <param name="packageVersion">The package version.</param>
        public async Task DeletePackage(string apiKey,
            string packageId,
            string packageVersion,
            string userAgent,
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
                await DeletePackageFromServer(apiKey, packageId, packageVersion, userAgent, logger, token);
            }
        }

        /// <summary>
        /// Deletes a package from the server represented by the Source.
        /// </summary>
        /// <param name="apiKey">API key to be used to delete the package.</param>
        /// <param name="packageId">The package Id.</param>
        /// <param name="packageVersion">The package Id.</param>
        private async Task DeletePackageFromServer(string apiKey,
            string packageId,
            string packageVersion,
            string userAgent,
            ILogger logger,
            CancellationToken token)
        {
            // Review: Do these values need to be encoded in any way?
            var url = String.Join("/", packageId, packageVersion);
            ICredentials credentials = CredentialStore.Instance.GetCredentials(_baseUri);
            while (true)
            {
                HttpHandlerResource handlerResource = await _messageHandlerFactory();
                using (var client = new HttpClient(handlerResource.ClientHandler))
                using (var request = new HttpRequestMessage(HttpMethod.Delete, GetServiceEndpointUrl(url)))
                {
                    UserAgent.SetUserAgent(client, userAgent);
                    if (credentials != null)
                    {
                        handlerResource.ClientHandler.Credentials = credentials;
                    }
                    else
                    {
                        handlerResource.ClientHandler.UseDefaultCredentials = true;
                    }
                    STSAuthHelper.PrepareSTSRequest(_baseUri, CredentialStore.Instance, request);
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        request.Headers.Add(ApiKeyHeader, apiKey);
                    }

                    using (var response = await client.SendAsync(request))
                    {
                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            if (STSAuthHelper.TryRetrieveSTSToken(_baseUri, CredentialStore.Instance, response))
                            {
                                continue;
                            }

                            if (_promptForCredentials != null)
                            {
                                credentials = await _promptForCredentials(_baseUri, token);
                            }

                            if (credentials != null)
                            {
                                continue;
                            }
                        }

                        if (_credentialsSuccessfullyUsed != null && credentials != null)
                        {
                            _credentialsSuccessfullyUsed(_baseUri, credentials);

                        }
                        response.EnsureSuccessStatusCode();
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Deletes a package from a FileSystem.
        /// </summary>
        /// <param name="fileSystem">The FileSystem where the specified package is deleted.</param>
        /// <param name="packageId">The package Id.</param>
        /// <param name="packageVersion">The package Id.</param>
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
            Uri requestUri;
            if (String.IsNullOrEmpty(_baseUri.AbsolutePath.TrimStart('/')))
            {
                // If there's no host portion specified, append the url to the client.
                requestUri = new Uri(_baseUri, ServiceEndpoint + '/' + path);
            }
            else
            {
                requestUri = new Uri(_baseUri, path);
            }
            return requestUri;
        }

        private static Uri EnsureTrailingSlash(string value)
        {
            if (!value.EndsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                value += "/";
            }

            return new Uri(value);
        }

        private bool IsV2StyleRepository(string root)
        {
            //ported from https://github.com/NuGet/NuGet2/blob/2.11/
            // src/Core/Repositories/LazyLocalPackageRepository.cs
            if (!Directory.Exists(root) ||
                Directory.GetFiles(root, "*.nupkg").Any())
            {
                // If the repository does not exist or if there are .nupkg in the path, this is a v2-style repository.
                return true;
            }

            foreach (var idDirectory in Directory.GetDirectories(path: string.Empty))
            {
                if (Directory.GetFiles(idDirectory, "*.nupkg").Any() ||
                    Directory.GetFiles(idDirectory, "*.nuspec").Any())
                {
                    // ~/Foo/Foo.1.0.0.nupkg (LocalPackageRepository with PackageSaveModes.Nupkg) or 
                    // ~/Foo/Foo.1.0.0.nuspec (LocalPackageRepository with PackageSaveMode.Nuspec)
                    return true;
                }

                foreach (var versionDirectoryPath in Directory.GetDirectories(idDirectory))
                {
                    if (Directory.GetFiles(versionDirectoryPath, idDirectory + ".nuspec").Any())
                    {
                        // If we have files in the format {packageId}/{version}/{packageId}.nuspec, 
                        // assume it's an expanded package repository.
                        return false;
                    }
                }
            }

            return true;
        }

    }
}
