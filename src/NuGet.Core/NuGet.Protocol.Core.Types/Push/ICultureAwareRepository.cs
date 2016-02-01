using System.Globalization;

namespace NuGet.Protocol.Core.Types.Push
{
    public interface ICultureAwareRepository
    {
        CultureInfo Culture { get; }
    }
}
