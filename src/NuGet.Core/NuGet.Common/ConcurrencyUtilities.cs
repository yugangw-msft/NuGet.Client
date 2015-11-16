// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.Common
{
    internal static class ConcurrencyUtilities
    {
        public async static Task<T> ExecuteWithFileLocked<T>(string filePath, Func<CancellationToken, Task<T>> action, CancellationToken token)
        {
            bool completed = false;
            while (!completed)
            {
                var createdNew = false;
                using (var fileLock = SemaphoreWrapper.Create(initialCount: 0, maximumCount: 1, name: FilePathToLockName(filePath),
                    createdNew: out createdNew))
                {
                    try
                    {
                        // If this lock is already acquired by another process, wait until we can acquire it
                        if (!createdNew)
                        {
                            var signaled = fileLock.WaitOne(TimeSpan.FromSeconds(5));
                            if (!signaled)
                            {
                                // Timeout and retry
                                continue;
                            }
                        }

                        completed = true;
                        return await action(token);
                    }
                    finally
                    {
                        if (completed)
                        {
                            fileLock.Release();
                        }
                    }
                }
            }

            // should never get here
            throw new TaskCanceledException($"Failed to acquire semaphore for file: {filePath}");
        }
        private static void HandleMutex(string name,
            SemaphoreSlim lockStart,
            SemaphoreSlim lockEnd,
            SemaphoreSlim lockOperation,
            CancellationToken token)
        {
            try
            {
                using (var mutex = new Mutex(initiallyOwned: false, name: name))
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            if (mutex.WaitOne(1000))
                            {
                                try
                                {
                                    lockStart.Release();
                                }
                                finally
                                {
                                    try
                                    {
                                        lockEnd.Wait();
                                    }
                                    finally
                                    {
                                        mutex.ReleaseMutex();
                                    }
                                }

                                break;
                            }

                            // The mutex is not released. Loop continues
                        }
                        catch (AbandonedMutexException)
                        {
                            // The mutex was abandoned, possibly because the process holding the mutex was killed.
                        }
                    }
                }
            }
            finally
            {
                lockOperation.Release();
            }
        }

        private static string FilePathToLockName(string filePath)
        {
            // If we use a file path directly as the name of a semaphore,
            // the ctor of semaphore looks for the file and throws an IOException
            // when the file doesn't exist. So we need a conversion from a file path
            // to a unique lock name.
            return filePath.Replace(Path.DirectorySeparatorChar, '_');
        }

        private class SemaphoreWrapper : IDisposable
        {
#if DNXCORE50
            private static Dictionary<string, SemaphoreWrapper> _nameWrapper =
                new Dictionary<string, SemaphoreWrapper>();

            private readonly string _name;
            private volatile int _refCount = 0;
#endif

            private readonly Semaphore _semaphore;

            public static SemaphoreWrapper Create(int initialCount, int maximumCount, string name, out bool createdNew)
            {
#if DNXCORE50
                if (RuntimeEnvironmentHelper.IsWindows)
                {
                    return new SemaphoreWrapper(new Semaphore(initialCount, maximumCount, name, out createdNew));
                }
                else
                {
                    var createdNewLocal = false;
                    SemaphoreWrapper wrapper;

                    lock (_nameWrapper)
                    {
                        wrapper = _nameWrapper.GetOrAdd(
                            name,
                            _ =>
                            {
                                createdNewLocal = true;
                                return new SemaphoreWrapper(new Semaphore(initialCount, maximumCount), name);
                            });
                        wrapper._refCount++;
                    }

                    // C# doesn't allow assigning value to an out parameter directly in lambda expression
                    createdNew = createdNewLocal;
                    return wrapper;
                }
#else

                return new SemaphoreWrapper(new Semaphore(initialCount, maximumCount, name, out createdNew));
#endif
            }

            private SemaphoreWrapper(Semaphore semaphore, string name = null)
            {
                _semaphore = semaphore;
#if DNXCORE50
                _name = name;
#endif
            }

            public bool WaitOne(TimeSpan timeout)
            {
                return _semaphore.WaitOne(timeout);
            }

            public int Release()
            {
                return _semaphore.Release();
            }

            public void Dispose()
            {
#if DNXCORE50
                if (RuntimeEnvironmentHelper.IsWindows)
                {
                    _semaphore.Dispose();
                }
                else
                {
                    lock (_nameWrapper)
                    {
                        _refCount--;
                        if (_refCount == 0)
                        {
                            _nameWrapper.Remove(_name);
                            _semaphore.Dispose();
                        }
                    }

                }
#else
                _semaphore.Dispose();
#endif
            }
        }
    }

    internal static class RuntimeEnvironmentHelper
    {
        private static bool? _hasMutex;
        public static bool IsWindows
        {
            get
            {
                if (_hasMutex.HasValue)
                {
                    return _hasMutex.Value;
                }

                try
                {
                    using (var mutex = new Mutex(true, Guid.NewGuid().ToString()))
                    {
                    }

                    _hasMutex = true;
                }
                catch (PlatformNotSupportedException)
                {
                    _hasMutex = false;
                }

                return _hasMutex.Value;
            }
        }
    }

    internal static class DictionaryExtensions
    {
        public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, Func<TKey, TValue> factory)
        {
            lock (dictionary)
            {
                TValue value;
                if (!dictionary.TryGetValue(key, out value))
                {
                    value = factory(key);
                    dictionary[key] = value;
                }

                return value;
            }
        }
    }
}
