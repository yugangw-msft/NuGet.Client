namespace NuGet.Protocol.Core.Types.Push
{
    public interface ILatestPackageLookup
    {
        bool TryFindLatestPackageById(string id, out SemanticVersion latestVersion);
        bool TryFindLatestPackageById(string id, bool includePrerelease, out IPackage package);
    }
}