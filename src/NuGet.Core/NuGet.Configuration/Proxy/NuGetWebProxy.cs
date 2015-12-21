// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;

namespace NuGet.Configuration.Proxy
{
    public class NuGetWebProxy : IWebProxy
    {
        public NuGetWebProxy(string host)
        {

        }

        public NuGetWebProxy(Uri host)
        {

        }

        public ICredentials Credentials
        {
            get;
            set;
        }

        public Uri GetProxy(Uri destination)
        {
            throw new NotImplementedException();
        }

        public bool IsBypassed(Uri host)
        {
            throw new NotImplementedException();
        }
    }
}
