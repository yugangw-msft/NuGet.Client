// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Protocol.Core.Types;

namespace NuGet.Protocol.Core.v3
{
    public class PushResourceV2Provider : ResourceProvider
    {
        public PushResourceV2Provider()
            : base(typeof(PushResourceV2),
                  nameof(PushResourceV2),
                  NuGetResourceProviderPositions.Last)
        {
        }

        public override async Task<Tuple<bool, INuGetResource>> TryCreate(SourceRepository source, 
            CancellationToken token)
        {
            INuGetResource resource = null;

            //TODO: figure out the search index stuff.
            if (source.PackageSource.IsHttp
                &&
                !source.PackageSource.Source.EndsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                var messageHandlerResource = await source.GetResourceAsync<HttpHandlerResource>(token);

                resource = new PushResourceV2(async () => (await source.GetResourceAsync<HttpHandlerResource>(token)), 
                                              source?.PackageSource?.Source);//TODO: re-ordder the parameter
            }

            return new Tuple<bool, INuGetResource>(resource != null, resource);
        }
    }
}
