// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using NuGet.Packaging;

namespace NuGet.Protocol.Core.Types
{
    /// <summary>
    /// The result of <see cref="DownloadResource.DownloadResource"/>.
    /// </summary>
    public class DownloadResourceResult : IDisposable
    {
        private readonly Stream _stream;
        private readonly PackageReaderBase _packageReader;

        public DownloadResourceResult()
        {
            _stream = Stream.Null;
            _packageReader = null;
        }

        public DownloadResourceResult(Stream stream, PackageReaderBase packageReader)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            _stream = stream;
            _packageReader = packageReader;
        }

        /// <summary>
        /// Builder method to create new instance
        /// </summary>
        /// <param name="stream">File package stream</param>
        /// <returns>New instance</returns>
        public static DownloadResourceResult FromStream(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            var packageReader = new PackageReader(stream);
            return new DownloadResourceResult(stream, packageReader);
        }

        /// <summary>
        /// Builder method to create new instance out of a package file
        /// </summary>
        /// <param name="packagePath">Path to a package file</param>
        /// <returns>New instance</returns>
        public static DownloadResourceResult FromPackageFile(string packagePath)
        {
            if (packagePath == null)
            {
                throw new ArgumentNullException(nameof(packagePath));
            }

            var fileStream = File.OpenRead(packagePath);
            var packageReader = new PackageReader(fileStream);
            return new DownloadResourceResult(fileStream, packageReader);
        }

        /// <summary>
        /// Gets the package <see cref="PackageStream"/>.
        /// </summary>
        public Stream PackageStream => _stream;

        /// <summary>
        /// Gets the <see cref="PackageReaderBase"/> for the package.
        /// </summary>
        /// <remarks>This property can be null.</remarks>
        public PackageReaderBase PackageReader => _packageReader;

        public void Dispose()
        {
            _stream.Dispose();
            _packageReader?.Dispose();
        }
    }
}
