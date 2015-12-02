using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Threading;

using Microsoft.DiaSymReader;
using System.Reflection;
using System.Threading.Tasks;

namespace NuGet.Common
{
    internal static class PdbReader
    {
        public static IEnumerable<string> GetSourceFileNames(IPackageFile pdbFile)
        {
            using (var stream = new ComStreamWrapper(pdbFile.GetStream()))
            {
                var reader = CreateNativeSymReader(stream).GetAwaiter().GetResult();

                return reader.GetDocuments()
                    .Select(doc => doc.GetName())
                    .Where(IsValidSourceFileName);
            }
        }

        private static async Task<ISymUnmanagedReader3> CreateNativeSymReader(IStream pdbStream)
        {
            object symReader = null;
            var guid = default(Guid);

            //Loading the Native DLL of SymReader
            await LoadNativeLibrary();

            if (IntPtr.Size == 4)
            {
                NativeMethods.CreateSymReader32(ref guid, out symReader);
            }
            else
            {
                NativeMethods.CreateSymReader64(ref guid, out symReader);
            }

            var reader = (ISymUnmanagedReader3)symReader;
            var hr = reader.Initialize(new DummyMetadataImport(), null, null, pdbStream);
            Marshal.ThrowExceptionForHR(hr);
            return reader;
        }

        private static bool IsValidSourceFileName(string sourceFileName)
        {
            return !string.IsNullOrEmpty(sourceFileName) && !IsTemporaryCompilerFile(sourceFileName);
        }

        private static bool IsTemporaryCompilerFile(string sourceFileName)
        {
            //the VB compiler will include temporary files in its pdb files.
            //the source file name will be similar to 17d14f5c-a337-4978-8281-53493378c1071.vb.
            return sourceFileName.EndsWith("17d14f5c-a337-4978-8281-53493378c1071.vb", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task LoadNativeLibrary()
        {
            // Extracting the embeded resource to NuGet cache folder
#if NET45
            var localAppDataFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#else
            var localAppDataFolderPath = Environment.GetEnvironmentVariable("LocalAppData");
#endif
            string resourceName = "NuGet.CommandLine.Microsoft.DiaSymReader.Native.x86.dll";
            var path = Path.Combine(localAppDataFolderPath, "NuGet", "ExtractedResources", "1.3.3");
            var filePath = Path.Combine(path, resourceName);

            await ConcurrencyUtilities.ExecuteWithFileLocked(
                filePath,
                action: cancellationToken =>
                {
                    if(!File.Exists(filePath))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (Directory.Exists(path))
                        {
                            // If we had a broken extraction, clean out the files first
                            var info = new DirectoryInfo(path);

                            foreach (var file in info.GetFiles())
                            {
                                file.Delete();
                            }

                            foreach (var dir in info.GetDirectories())
                            {
                                dir.Delete(true);
                            }
                        }
                        else
                        {
                            Directory.CreateDirectory(path);
                        }

                        // Only the first will extract the resource
                        var assembly = Assembly.GetExecutingAssembly();
                        var tempFile = Path.Combine(path, Guid.NewGuid().ToString() + ".dll");
                        try
                        {
                            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                            {
                                var binaryReader = new BinaryReader(stream);
                                byte[] data = binaryReader.ReadBytes((int)stream.Length);
                                File.WriteAllBytes(tempFile, data);
                            }
                        }
                        catch(Exception exception)
                        {
                            System.Console.WriteLine(exception.Message);
                            throw;
                        }

                        File.Move(tempFile, filePath);
                    }
                    return Task.FromResult(true);
                },
                token: new CancellationToken());

            //Loading the Library
            if(File.Exists(filePath))
            {
                NativeMethods.LoadLibrary(filePath);
            }
        }

        private class DummyMetadataImport : IMetadataImport { }

        [SuppressUnmanagedCodeSecurity]
        private static class NativeMethods
        {
            [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
            [DllImport("Microsoft.DiaSymReader.Native.x86.dll", EntryPoint = "CreateSymReader")]
            internal extern static void CreateSymReader32(ref Guid id, [MarshalAs(UnmanagedType.IUnknown)]out object symReader);

            [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
            [DllImport("Microsoft.DiaSymReader.Native.amd64.dll", EntryPoint = "CreateSymReader")]
            internal extern static void CreateSymReader64(ref Guid id, [MarshalAs(UnmanagedType.IUnknown)]out object symReader);

            [DllImport("kernel32.dll")]
            internal static extern IntPtr LoadLibrary(string dllToLoad);
        }        
    }

    [SuppressMessage("Microsoft.Design", "CA1040:AvoidEmptyInterfaces", Justification = "need to validate that's ok to suppress.")]
    [ComImport, Guid("7DAC8207-D3AE-4c75-9B67-92801A497D44"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), TypeIdentifier]
    public interface IMetadataImport { }
}
