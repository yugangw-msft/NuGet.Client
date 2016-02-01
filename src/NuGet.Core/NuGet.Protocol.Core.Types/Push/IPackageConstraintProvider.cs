namespace NuGet.Protocol.Core.Types.Push
{
    public interface IPackageConstraintProvider
    {
        string Source { get; }
        IVersionSpec GetConstraint(string packageId);
    }
}
