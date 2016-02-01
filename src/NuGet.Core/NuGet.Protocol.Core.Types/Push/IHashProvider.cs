using System.IO;

namespace NuGet.Protocol.Core.Types.Push
{
    public interface IHashProvider
    {
        byte[] CalculateHash(Stream stream);

        byte[] CalculateHash(byte[] data);

        bool VerifyHash(byte[] data, byte[] hash);
    }
}
