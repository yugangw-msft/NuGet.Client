using System;

namespace NuGet.Protocol.Core.Types.Push
{
    public interface IServerPackageMetadata
    {
        Uri ReportAbuseUrl { get; }
        int DownloadCount { get; }
    }
}
