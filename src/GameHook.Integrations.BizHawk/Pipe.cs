using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace GameHook.Integrations.BizHawk
{
    public interface ISerializable
    {
        public byte[] Serialize();
    }

    public abstract class PipeBase<T> where T : ISerializable
    {
        protected readonly Object _lock;
        protected EventHandler<PipeConnectedArgs>? _pipeConnectedEvent;
        protected EventHandler<PipeReadArgs>? _pipeReadEvent;
        protected bool _isStopping;
        protected bool _isStopped;
        protected bool _isConnected;
        protected Func<byte[], T> _objDeserializer;
        protected Queue<T> _writeQueue;
        protected bool _isWriting;
        protected PipeStream? _stream;

        public PipeBase(Func<byte[], T> objDeserialize)
        {
            _lock = new();
            _pipeConnectedEvent = null;
            _isStopping = false;
            _isStopped = false;
            _isConnected = false;
            _isWriting = false;
            _objDeserializer = objDeserialize;
            _writeQueue = new();
            _stream = null;
        }

        public event EventHandler<PipeConnectedArgs>? PipeConnectedEvent
        {
            add
            {
                lock (_lock)
                {
                    if (_pipeConnectedEvent != null)
                    {
                        _pipeConnectedEvent += value;
                    }
                    else
                    {
                        _pipeConnectedEvent = value;
                    }
                }
            }

            remove
            {
                lock (_lock)
                {
                    if (_pipeConnectedEvent != null)
                    {
                        _pipeConnectedEvent -= value;
                    }
                    else
                    {
                        _pipeConnectedEvent = null;
                    }
                }
            }
        }

        public event EventHandler<PipeReadArgs>? PipeReadEvent
        {
            add
            {
                lock (_lock)
                {
                    if (_pipeReadEvent != null)
                    {
                        _pipeReadEvent += value;
                    }
                    else
                    {
                        _pipeReadEvent = value;
                    }
                }
            }

            remove
            {
                lock (_lock)
                {
                    if (_pipeReadEvent != null)
                    {
                        _pipeReadEvent -= value;
                    }
                    else
                    {
                        _pipeReadEvent = null;
                    }
                }
            }
        }

        public class PipeConnectedArgs : EventArgs
        {
            public PipeConnectedArgs()
            {

            }
        }

        public class PipeReadArgs : EventArgs
        {
            private readonly T _arg;

            public PipeReadArgs(T arg)
            {
                _arg = arg;
            }

            public T Arg
            {
                get
                {
                    return _arg;
                }
            }
        }

        protected void Connected()
        {
            lock (_lock)
            {
                _isConnected = true;
            }
            _pipeConnectedEvent?.Invoke(this, new PipeConnectedArgs());
            BeginRead();
            DoWriteImpl();
        }

        public void Write(T toWrite)
        {
            if (_stream == null)
            {
                throw new NullReferenceException(nameof(_stream));
            }

            lock (_lock)
            {
                _writeQueue.Enqueue(toWrite);

                if (!_isWriting && _isConnected)
                {
                    _isWriting = true;
                    DoWriteImpl();
                }
            }
        }

        private void DoWrite(IAsyncResult result)
        {
            lock (_lock)
            {
                _stream!.EndWrite(result);
                DoWriteImpl();
            }
        }

        private void DoWriteImpl()
        {
            if (_writeQueue.Count > 0)
            {
                T obj = _writeQueue.Dequeue();
                byte[] bytes = obj.Serialize();
                byte[] lengthBytes = BitConverter.GetBytes(bytes.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(lengthBytes);
                byte[] writeBytes = lengthBytes.Concat(new byte[1] { 0 }).Concat(bytes).ToArray();
                _stream!.BeginWrite(writeBytes, 0, writeBytes.Length, DoWrite, null);
            }
            else
            {
                _isWriting = false;
            }
        }

        protected void BeginRead()
        {
            lock (_lock)
            {
                BeginReadImpl();
            }
        }

        private void BeginReadImpl()
        {
            if (_stream == null)
                throw new NullReferenceException(nameof(_stream));

            byte[] length = new byte[sizeof(int)];
            int? read = 0;
            int? toRead = length.Length;
            _stream!.BeginRead(length, read.Value, toRead.Value, ReadPrefix, new object[] { length, read, toRead });
        }

        private void ReadPrefix(IAsyncResult result)
        {
            lock (_lock)
            {
                object[] state = result.AsyncState as object[] ?? throw new NullReferenceException("state");
                byte[] length = state[0] as byte[] ?? throw new NullReferenceException("state");
                int? read = state[1] as int? ?? throw new NullReferenceException("state");
                int? toRead = state[2] as int? ?? throw new NullReferenceException("state");

                int lengthRead = _stream!.EndRead(result);
                if (lengthRead < toRead.Value)
                {
                    read = read.Value + lengthRead;
                    toRead = toRead.Value - lengthRead;
                    _stream!.BeginRead(length, read.Value, toRead.Value, ReadPrefix, new object[] { length, read, toRead });
                }
                else
                {
                    byte[] sepBuf = new byte[1];
                    _stream!.BeginRead(sepBuf, 0, 1, ReadSep, new object[] { sepBuf, length });
                }
            }
        }

        private void ReadSep(IAsyncResult result)
        {
            lock (_lock)
            {
                object[] state = result.AsyncState as object[] ?? throw new NullReferenceException("state");
                byte[] sepBuf = state[0] as byte[] ?? throw new NullReferenceException("state");
                byte[] lengthBytes = state[1] as byte[] ?? throw new NullReferenceException("state");

                int lengthRead = _stream!.EndRead(result);

                if (lengthRead < 1)
                {
                    _stream!.BeginRead(sepBuf, 0, 1, ReadSep, new object[] { sepBuf, lengthBytes });
                }
                else
                {
                    if (sepBuf[0] != '\0')
                        throw new FormatException();
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(lengthBytes);

                    int length = BitConverter.ToInt32(lengthBytes, 0);
                    byte[] dataBuf = new byte[length];

                    int? read = 0;
                    int? toRead = length;
                    _stream!.BeginRead(dataBuf, read.Value, toRead.Value, ReadData, new object[] { dataBuf, read, toRead });
                }
            }
        }

        private void ReadData(IAsyncResult result)
        {

            lock (_lock)
            {
                object[] state = result.AsyncState as object[] ?? throw new NullReferenceException("state");
                byte[] dataBuf = state[0] as byte[] ?? throw new NullReferenceException("state");
                int? read = state[1] as int? ?? throw new NullReferenceException("state");
                int? toRead = state[2] as int? ?? throw new NullReferenceException("state");

                int lengthRead = _stream!.EndRead(result);
                if (lengthRead < toRead.Value)
                {
                    read = read.Value + lengthRead;
                    toRead = toRead.Value - lengthRead;
                    _stream!.BeginRead(dataBuf, read.Value, toRead.Value, ReadData, new object[] { dataBuf, read, toRead });
                }
                else
                {
                    T readData = _objDeserializer(dataBuf);
                    _pipeReadEvent?.Invoke(this, new PipeReadArgs(readData));
                    BeginReadImpl();
                }
            }
        }
    }

    public class PipeServer<T> : PipeBase<T> where T : ISerializable
    {
        private readonly NamedPipeServerStream? _pipe = null;

        public PipeServer(string pipeName, Func<byte[], T> objDeserialize, int inputBufferSize = 0, int outputBufferSize = 0)
            : base(objDeserialize)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Message, PipeOptions.Asynchronous, inputBufferSize, outputBufferSize);
            }
            else
            {
                _pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, inputBufferSize, outputBufferSize);
            }
            _stream = _pipe;

            _pipe.BeginWaitForConnection(PipeConnected, null);
        }

        private void PipeConnected(IAsyncResult result)
        {
            if (!_isStopped)
            {
                lock (_lock)
                {
                    if (!_isStopping)
                    {
                        _pipe!.EndWaitForConnection(result);
                        Connected();
                    }
                }
            }
        }
    }

    public class PipeClient<T> : PipeBase<T> where T : ISerializable
    {
        private readonly NamedPipeClientStream? _pipe = null;

        public class PipeConnectAsyncResult : IAsyncResult, IDisposable
        {
            readonly object? _asyncState;
            bool _isCompleted;
            WaitHandle? _waitHandle;

            public PipeConnectAsyncResult()
            {
                _asyncState = null;
                _isCompleted = false;
                _waitHandle = new ManualResetEvent(false);
            }

            public object? AsyncState
            {
                get
                {
                    return _asyncState;
                }
            }

            public WaitHandle AsyncWaitHandle
            {
                get
                {
                    if (_waitHandle == null)
                        throw new NullReferenceException(nameof(_waitHandle));
                    return _waitHandle;
                }
            }

            public bool CompletedSynchronously
            {
                get
                {
                    return false;
                }
            }

            public bool IsCompleted
            {
                get
                {
                    return _isCompleted;
                }
                set
                {
                    _isCompleted = value;
                }
            }

            public void Dispose()
            {
                GC.SuppressFinalize(this);
                if (_waitHandle != null)
                {
                    _waitHandle.Close();
                    _waitHandle.Dispose();
                    _waitHandle = null;
                }
            }
        }

        public PipeClient(string pipeName, Func<byte[], T> objDeserialize)
            : base(objDeserialize)
        {
            _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            _stream = _pipe;

            BeginConnect(PipeConnected);
        }

        private void BeginConnect(AsyncCallback callback)
        {
            lock (_lock)
            {
                ParameterizedThreadStart parameterizedThreadStart = new(BeginConnectImpl);
                Thread connectThread = new(parameterizedThreadStart);
                connectThread.Start(callback);
            }
        }

        private void BeginConnectImpl(object? obj)
        {
            PipeConnectAsyncResult result = new();
            AsyncCallback callback = obj as AsyncCallback ?? throw new NullReferenceException(nameof(obj));
            lock (_lock)
            {
                _isConnected = false;
            }
            _pipe!.Connect();
            ManualResetEvent waitHandle = result.AsyncWaitHandle as ManualResetEvent ?? throw new NullReferenceException(nameof(result.AsyncWaitHandle));
            waitHandle.Set();
            lock (_lock)
            {
                _isConnected = true;
            }
            callback.Invoke(result);
            waitHandle.Close();
            waitHandle.Dispose();
        }
        private void PipeConnected(IAsyncResult result)
        {
            if (!_isStopped)
            {
                lock (_lock)
                {
                    if (!_isStopping)
                    {
                        Connected();
                    }
                }
            }
        }
    }
}
