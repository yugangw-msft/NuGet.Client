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

namespace NuGet.Common
{
    internal static class PdbReader
    {
        public static IEnumerable<string> GetSourceFileNames(IPackageFile pdbFile)
        {
            using (var stream = new ComStreamWrapper(pdbFile.GetStream()))
            {
                var reader = CreateNativeSymReader(stream);

                return reader.GetDocuments()
                    .Select(doc => doc.GetName())
                    .Where(IsValidSourceFileName);
            }
        }

        private static ISymUnmanagedReader3 CreateNativeSymReader(IStream pdbStream)
        {
            object symReader = null;
            var guid = default(Guid);

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
        }        
    }

    [SuppressMessage("Microsoft.Design", "CA1040:AvoidEmptyInterfaces", Justification = "need to validate that's ok to suppress.")]
    [ComImport, Guid("7DAC8207-D3AE-4c75-9B67-92801A497D44"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), TypeIdentifier]
    public interface IMetadataImport { }
}
