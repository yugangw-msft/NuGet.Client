
namespace NuGet.Protocol.Core.Types.Push
{
    public interface IPackageName
    {
        string Id { get; }
        SemanticVersion Version { get; }
    }
}
