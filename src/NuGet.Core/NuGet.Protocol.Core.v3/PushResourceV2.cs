// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.Core.v3.Data;
using NuGet.Logging;
using System.IO;
using System.Net.Http.Headers;
using System.Globalization;
using System.Net;
using NuGet.Configuration;

namespace NuGet.Protocol.Core.v3
{
    public class PushResourceV2 : INuGetResource
    {
        private const string ServiceEndpoint = "/api/v2/package";
        private const string ApiKeyHeader = "X-NuGet-ApiKey";

        private Uri _pushUri;
        private string _source;
        private bool _pushToFileSystem;
        private Func<Task<HttpHandlerResource>> _handlerResourceFactory;

        public PushResourceV2(Func<Task<HttpHandlerResource>> handlerResourceFactory, string source)
            : base()
        {
            if (handlerResourceFactory == null)
            {
                throw new ArgumentNullException("messageHandlerFactory");
            }

            _handlerResourceFactory = handlerResourceFactory;
            _pushToFileSystem = (new Uri(source)).IsFile;//TODO: redundency?
            _source = source;
            _pushUri = EnsureTrailingSlash(source);
        }

        public string GetPushEndpoint()
        {
            return _source;
        }

        public async Task PushPackage(string apiKey,
            string pathToPackage,
            long packageSize,
            ILogger logger,
            CancellationToken token)
        {
            if (_pushToFileSystem)
            {
                //PushPackageToFileSystem(Source.LocalPath, pathToPackage);
                throw new NotImplementedException();
            }

            ICredentials credentials = CredentialStore.Instance.GetCredentials(_pushUri);
            bool retry = true;
            while (retry)
            {
                var handlerResource = await _handlerResourceFactory();
                using (var client = new DataClient(handlerResource))
                {
                    if (credentials != null)
                    {
                        handlerResource.ClientHandler.Credentials = credentials;
                    }
                    else
                    {
                        handlerResource.ClientHandler.UseDefaultCredentials = true;
                    }
                    using (var fileStream = new FileStream(pathToPackage, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var request = new HttpRequestMessage(HttpMethod.Put, GetServiceEndpointUrl(string.Empty)))
                    {
                        STSAuthHelper.PrepareSTSRequest(_pushUri, CredentialStore.Instance, request);
                        if (!string.IsNullOrEmpty(apiKey)) //TODO: confirm if apikey is set, no credentail manipulatings
                        {
                            request.Headers.Add(ApiKeyHeader, apiKey);
                        }

                        using (var content = new MultipartFormDataContent())
                        using (var packageContent = new StreamContent(fileStream))
                        {
                            packageContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");

                            content.Add(packageContent, "package", "package.nupkg");

                            //TODO: add logging
                            //Logger.LogDebug(string.Format(CultureInfo.CurrentCulture, "PUT: {0}", request.RequestUri));
                            request.Content = content;
                            using (var response = await client.SendAsync(request))
                            {
                                bool useV3HandlerResource = handlerResource is HttpHandlerResourceV3;
                                if (response.StatusCode == HttpStatusCode.Unauthorized)
                                {
                                    if (STSAuthHelper.TryRetrieveSTSToken(_pushUri, CredentialStore.Instance, response))
                                    {
                                        continue;
                                    }

                                    //TODO: confirm, this badness is OK
                                    if (useV3HandlerResource && HttpHandlerResourceV3.PromptForCredentials != null)
                                    {
                                        credentials = await HttpHandlerResourceV3.PromptForCredentials(_pushUri, token);
                                    }

                                    if (credentials == null)
                                    {
                                        response.EnsureSuccessStatusCode();
                                    }
                                    else
                                    {
                                        continue;
                                    }
                                }

                                retry = false;
                                response.EnsureSuccessStatusCode();

                                if (useV3HandlerResource && HttpHandlerResourceV3.CredentialsSuccessfullyUsed != null && credentials != null)
                                {
                                    HttpHandlerResourceV3.CredentialsSuccessfullyUsed(_pushUri, credentials);
                                }

                            }
                        }
                    }

                }
            }
        }

        ///// <summary>
        ///// Pushes a package to a FileSystem.
        ///// </summary>
        ///// <param name="fileSystem">The FileSystem that the package is pushed to.</param>
        ///// <param name="package">The package to be pushed.</param>
        //private static void PushPackageToFileSystem(IFileSystem fileSystem, IPackage package)
        //{
        //    var pathResolver = new DefaultPackagePathResolver(fileSystem);
        //    var packageFileName = pathResolver.GetPackageFileName(package);
        //    using (var stream = package.GetStream())
        //    {
        //        fileSystem.AddFile(packageFileName, stream);
        //    }
        //}

        /// <summary>
        /// Deletes a package from the Source.
        /// </summary>
        /// <param name="apiKey">API key to be used to delete the package.</param>
        /// <param name="packageId">The package Id.</param>
        /// <param name="packageVersion">The package version.</param>
        public async Task DeletePackage(string apiKey, string packageId, string packageVersion)
        {
            var sourceUri = GetServiceEndpointUrl(string.Empty);
            if (sourceUri.IsFile)
            {
                //DeletePackageFromFileSystem(
                //    new PhysicalFileSystem(sourceUri.LocalPath),
                //    packageId,
                //    packageVersion);
            }
            else
            {
                await DeletePackageFromServer(apiKey, packageId, packageVersion);
            }
        }

        /// <summary>
        /// Deletes a package from the server represented by the Source.
        /// </summary>
        /// <param name="apiKey">API key to be used to delete the package.</param>
        /// <param name="packageId">The package Id.</param>
        /// <param name="packageVersion">The package Id.</param>
        private async Task DeletePackageFromServer(string apiKey, string packageId, /*TODO: add logger*/ string packageVersion)
        {
            // Review: Do these values need to be encoded in any way?
            var url = String.Join("/", packageId, packageVersion);
            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage(HttpMethod.Delete, GetServiceEndpointUrl(url)))
            {
                //request.Headers.Add("Content-Type", "text/html");
                if (!string.IsNullOrEmpty(apiKey))
                {
                    request.Headers.Add(ApiKeyHeader, apiKey);
                }

                //Logger.LogDebug(string.Format(CultureInfo.CurrentCulture, "Delete: {0}", request.RequestUri));
                using (var response = await client.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();
                }
            }
        }

        ///// <summary>
        ///// Deletes a package from a FileSystem.
        ///// </summary>
        ///// <param name="fileSystem">The FileSystem where the specified package is deleted.</param>
        ///// <param name="packageId">The package Id.</param>
        ///// <param name="packageVersion">The package Id.</param>
        //private static void DeletePackageFromFileSystem(IFileSystem fileSystem, string packageId, string packageVersion)
        //{
        //    var resolver = new PackagePathResolver(string.Empty, false);
        //    resolver.GetPackageFileName(new Packaging.Core.PackageIdentity(packageId, new NuGetVersion(packageVersion)));

        //    var pathResolver = new DefaultPackagePathResolver(fileSystem);
        //    var packageFileName = pathResolver.GetPackageFileName(packageId, new NuGetVersion(packageVersion));
        //    fileSystem.DeleteFile(packageFileName);
        //}


        /// <summary>
        /// Calculates the URL to the package
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private Uri GetServiceEndpointUrl(string path)
        {
            Uri requestUri;
            if (String.IsNullOrEmpty(_pushUri.AbsolutePath.TrimStart('/')))
            {
                // If there's no host portion specified, append the url to the client.
                requestUri = new Uri(_pushUri, ServiceEndpoint + '/' + path);
            }
            else
            {
                requestUri = new Uri(_pushUri, path);
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
    }
}