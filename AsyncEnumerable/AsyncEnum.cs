﻿/*
    Copyright (c) 2012, iD Commerce + Logistics
    All rights reserved.

    Redistribution and use in source and binary forms, with or without modification, are permitted
    provided that the following conditions are met:

    Redistributions of source code must retain the above copyright notice, this list of conditions
    and the following disclaimer. Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in the documentation and/or other
    materials provided with the distribution.
 
    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
    IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
    FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR
    CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
    CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
    SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
    THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR
    OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
    POSSIBILITY OF SUCH DAMAGE.
*/
//#define CODE_COVERAGE
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace System.Threading.Tasks
{

#if !CODE_COVERAGE
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
#endif
    static class AsyncEnum
    {
        public static IAsyncEnumerable<T> Create<T>(Func<AsyncYielder<T>, Task> func)
        {
            Debug.Assert(func != null);
            return new YieldAsyncEnumerable<T>(func);
        }

        sealed class YieldAsyncEnumerable<T> : IAsyncEnumerable<T>
        {
            readonly Func<AsyncYielder<T>, Task> func;

            public YieldAsyncEnumerable(Func<AsyncYielder<T>, Task> func)
            {
                Debug.Assert(func != null);
                this.func = func;
            }

            public IAsyncEnumerator<T> GetEnumerator()
            {
                return new YieldAsyncEnumerator<T>(func);
            }
        }

        sealed class YieldAsyncEnumerator<T> : IAsyncEnumerator<T>
        {
            Func<AsyncYielder<T>, Task> func;
            AsyncYielder<T> yielder;
            Task task;

            public YieldAsyncEnumerator(Func<AsyncYielder<T>, Task> func)
            {
                Debug.Assert(func != null);
                this.func = func;
            }

            ~YieldAsyncEnumerator()
            {
                DisposeImpl();
            }

            public T Current { get; private set; }

            public async Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                if (task != null)
                {
                    // Second MoveNext() call. Tell Yielder to let the function continue.
                    yielder.CancellationToken = cancellationToken;
                    yielder.Continue();
                }
                else
                {
                    if (func == null)
                    {
                        throw new AsyncYielderDisposedException();
                    }

                    // First MoveNext() call. Start the task.

                    yielder = new AsyncYielder<T>();
                    yielder.CancellationToken = cancellationToken;

                    task = func(yielder);
                    func = null;
                }

                // Wait for yield or return.

                Task finished = await Task.WhenAny(task, yielder.YieldTask);

                if (finished != task)
                {
                    // the function returned a result.

#pragma warning disable AsyncFixer02 // Long running or blocking operations under an async method
                    Current = yielder.YieldTask.Result;
#pragma warning restore AsyncFixer02 // Long running or blocking operations under an async method
                    return true;
                }

                // The operation is finished.

                Task t = task;

                yielder = null;
                task = null;

                if (t.IsFaulted)
                {
                    throw t.Exception;
                }

                return false;
            }

            public void Dispose()
            {
                DisposeImpl();
                GC.SuppressFinalize(this);
            }

            void DisposeImpl()
            {
                if (task != null)
                {
                    Task t = task;
                    yielder.Break();
                    yielder = null;
                    func = null;
                    try
                    {
                        t.Wait();
                    }
                    catch (AggregateException ex)
                    {
                        if (!(ex.GetBaseException() is AsyncYielderDisposedException))
                        {
                            throw;
                        }
                    }
                    finally
                    {
                        t.Dispose();
                        task = null;
                    }
                }
            }
        }
    }

#if !CODE_COVERAGE
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
#endif
    sealed class AsyncYielder<T>
    {
        TaskCompletionSource<T> setTcs = new TaskCompletionSource<T>();
        TaskCompletionSource<int> getTcs;

        internal Task<T> YieldTask { get { return setTcs.Task; } }
        public CancellationToken CancellationToken { get; internal set; }

        public Task YieldReturn(T value)
        {
            getTcs = new TaskCompletionSource<int>();
            Task t = getTcs.Task;
            setTcs.SetResult(value);
            return t;
        }

        public void ThrowIfCancellationRequested()
        {
            CancellationToken.ThrowIfCancellationRequested();
        }

        internal void Continue()
        {
            setTcs = new TaskCompletionSource<T>();
            getTcs.SetResult(0);
        }

        internal void Break()
        {
            AsyncYielderDisposedException ex = new AsyncYielderDisposedException();
            getTcs.TrySetException(ex);
            setTcs.TrySetException(ex);
        }
    }

#if !CODE_COVERAGE
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
#endif
    sealed class AsyncYielderDisposedException : ObjectDisposedException
    {
        internal AsyncYielderDisposedException()
            : base("AsyncYielder")
        {
        }
    }
}