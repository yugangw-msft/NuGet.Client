using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using STATSTG = System.Runtime.InteropServices.ComTypes.STATSTG;

namespace NuGet.Common
{
    /// <summary>
    /// Wrap a Stream so it's usable where we need an IStream
    /// </summary>
    internal sealed class ComStreamWrapper : IStream, IDisposable
    {
        Stream _stream;        
        private bool _disposed = false;

        /// <summary>
        /// Create a new adapter around the given stream.
        /// </summary>
        /// <param name="wrappedStream">The stream to wrap.</param>
        public ComStreamWrapper(Stream wrappedStream)
        {
            _stream = wrappedStream;
        }

        ~ComStreamWrapper()
        {
            Dispose(false);
        }

        public void Clone(out IStream ppstm)
        {
            throw new NotSupportedException();
        }

        public void Commit(int grfCommitFlags)
        {
        }

        public void LockRegion(long libOffset, long cb, int dwLockType)
        {
            throw new NotSupportedException();
        }

        public void Revert()
        {
            throw new NotSupportedException();
        }

        public void UnlockRegion(long libOffset, long cb, int dwLockType)
        {
            throw new NotSupportedException();
        }

        public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
        {
            throw new NotSupportedException();
        }

        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            var count = _stream.Read(pv, 0, cb);
            if (pcbRead != IntPtr.Zero)
            {
                Marshal.WriteInt32(pcbRead, count);
            }
        }

        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            var origin = (SeekOrigin)dwOrigin;
            var pos = _stream.Seek(dlibMove, origin);
            if (plibNewPosition != IntPtr.Zero)
            {
                Marshal.WriteInt64(plibNewPosition, pos);
            }
        }

        public void SetSize(long libNewSize)
        {
            _stream.SetLength(libNewSize);
        }

        public void Stat(out STATSTG pstatstg, int grfStatFlag)
        {
            pstatstg = new STATSTG
            {
                type = 2,
                cbSize = _stream.Length,
                grfMode = 0
            };

            if (_stream.CanRead && _stream.CanWrite)
            {
                pstatstg.grfMode |= 2;
            }
            else if (_stream.CanWrite && !_stream.CanRead)
            {
                pstatstg.grfMode |= 1;
            }
        }

        public void Write(byte[] pv, int cb, IntPtr pcbWritten)
        {
            _stream.Write(pv, 0, cb);
            if (pcbWritten != IntPtr.Zero)
            {
                Marshal.WriteInt32(pcbWritten, cb);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                Interlocked.Exchange(ref _stream, null)?.Close();                
                _disposed = true;
            }
        }
    }
}
