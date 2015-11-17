using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

using Microsoft.DiaSymReader;

namespace NuGet.Common
{
    internal static class SymUnmanagedReaderExtensions
    {
        // Excerpt of http://source.roslyn.io/#Roslyn.Test.PdbUtilities/Shared/SymUnmanagedReaderExtensions.cs

        private const int E_FAIL = unchecked((int)0x80004005);
        private const int E_NOTIMPL = unchecked((int)0x80004001);

        private delegate int ItemsGetter<in TEntity, in TItem>(TEntity entity, int bufferLength, out int count, TItem[] buffer);

        private static string ToString(char[] buffer)
        {
            if (buffer.Length == 0)
                return string.Empty;

            Debug.Assert(buffer[buffer.Length - 1] == 0);
            return new string(buffer, 0, buffer.Length - 1);
        }

        private static void ValidateItems(int actualCount, int bufferLength)
        {
            if (actualCount != bufferLength)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Read only {0} of {1} items.", actualCount, bufferLength));
            }
        }

        private static TItem[] GetItems<TEntity, TItem>(TEntity entity, ItemsGetter<TEntity, TItem> getter)
        {
            int count;
            var hr = getter(entity, 0, out count, null);
            ThrowExceptionForHR(hr);
            if (count == 0)
                return new TItem[0];

            var result = new TItem[count];
            hr = getter(entity, count, out count, result);
            ThrowExceptionForHR(hr);
            ValidateItems(count, result.Length);
            return result;
        }

        public static ISymUnmanagedDocument[] GetDocuments(this ISymUnmanagedReader reader)
        {
            return GetItems(reader, (ISymUnmanagedReader a, int b, out int c, ISymUnmanagedDocument[] d) => a.GetDocuments(b, out c, d));
        }

        internal static string GetName(this ISymUnmanagedDocument document)
        {
            return ToString(GetItems(document, (ISymUnmanagedDocument a, int b, out int c, char[] d) => a.GetUrl(b, out c, d)));
        }

        internal static void ThrowExceptionForHR(int hr)
        {
            // E_FAIL indicates "no info".
            // E_NOTIMPL indicates a lack of ISymUnmanagedReader support (in a particular implementation).
            if (hr < 0 && hr != E_FAIL && hr != E_NOTIMPL)
            {
                Marshal.ThrowExceptionForHR(hr, new IntPtr(-1));
            }
        }
    }
}