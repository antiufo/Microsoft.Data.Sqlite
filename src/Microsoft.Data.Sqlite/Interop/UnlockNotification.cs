// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.Data.Sqlite.Interop
{
    internal class UnlockNotification : IDisposable
    {
        private AutoResetEvent _signal;
        private GCHandle _gch;

        public UnlockNotification()
        {
            _gch = GCHandle.Alloc(this);
            _signal = new AutoResetEvent(false);
        }

        public IntPtr Handle => GCHandle.ToIntPtr(_gch);
        public static UnlockNotification FromIntPtr(IntPtr ptr)
        {
            var gch = GCHandle.FromIntPtr(ptr);
            return (UnlockNotification)gch.Target;
        }

        public void Signal()
        {
            _signal.Set();
        }

        public void Wait(int timeout)
        {
            _signal.WaitOne(timeout);
        }

        public void Dispose()
        {
            _gch.Free();
        }
    }
}