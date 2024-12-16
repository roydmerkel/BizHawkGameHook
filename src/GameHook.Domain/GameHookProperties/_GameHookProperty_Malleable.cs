using GameHook.Domain.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace GameHook.Domain.GameHookProperties
{
    public abstract partial class GameHookProperty : IGameHookProperty
    {
        public delegate void LockDelegate();
        public delegate void UnlockDelegate();

        private class InstantWriteBytes<T> : IList<T>
        {
            private class InstantWriteBytesEnumerator : IEnumerator<T>
            {
                private LockDelegate lockFunction;
                private UnlockDelegate unlockFunction;

                private LockDelegate lockFieldsFunction;
                private UnlockDelegate unlockFieldsFunction;

                private GameHookProperty property;
                private IEnumerator<T> enumerator;
                private string fieldName;

                public InstantWriteBytesEnumerator(GameHookProperty _property, IEnumerator<T> _enumerator, LockDelegate _lockFunction, UnlockDelegate _unlockFunction, LockDelegate _lockFieldsFunction, UnlockDelegate _unlockFieldsFunction, string _fieldName)
                {
                    property = _property;
                    lockFunction = _lockFunction;
                    unlockFunction = _unlockFunction;
                    lockFieldsFunction = _lockFieldsFunction;
                    unlockFieldsFunction = _unlockFieldsFunction;
                    enumerator = _enumerator;
                    fieldName = _fieldName;
                }

                public T Current
                {
                    get
                    {
                        try
                        {
                            lockFunction.Invoke();
                            return enumerator.Current;
                        }
                        finally
                        {
                            unlockFunction.Invoke();
                        }
                    }
                }

                object IEnumerator.Current
                {
                    get
                    {
                        try
                        {
                            lockFunction.Invoke();
                            return ((IEnumerator)enumerator).Current;
                        }
                        finally
                        {
                            unlockFunction.Invoke();
                        }
                    }
                }

                public void Dispose()
                {
                    try
                    {
                        lockFunction.Invoke();
                        lockFieldsFunction.Invoke();
                        property.FieldsChanged.Add(fieldName);
                        enumerator.Dispose();
                    }
                    finally
                    {
                        unlockFieldsFunction.Invoke();
                        unlockFunction.Invoke();
                    }
                }

                public bool MoveNext()
                {
                    try
                    {
                        lockFunction.Invoke();
                        return enumerator.MoveNext();
                    }
                    finally
                    {
                        unlockFunction.Invoke();
                    }
                }

                public void Reset()
                {
                    try
                    {
                        lockFunction.Invoke();
                        lockFieldsFunction.Invoke();
                        property.FieldsChanged.Add(fieldName);
                        enumerator.Reset();
                    }
                    finally
                    {
                        unlockFieldsFunction.Invoke();
                        unlockFunction.Invoke();
                    }
                }
            }

            private GameHookProperty property;
            private object lockObject;
            private List<T> list;
            private string fieldName;

            private LockDelegate lockFunction;
            private UnlockDelegate unlockFunction;

            private LockDelegate lockFieldsFunction;
            private UnlockDelegate unlockFieldsFunction;

            public InstantWriteBytes(GameHookProperty _property, LockDelegate _lockFunction, UnlockDelegate _unlockFunction, LockDelegate _lockFieldsFunction, UnlockDelegate _unlockFieldsFunction, string _fieldName)
            {
                property = _property;
                lockFunction = _lockFunction;
                unlockFunction = _unlockFunction;
                lockFieldsFunction = _lockFieldsFunction;
                unlockFieldsFunction = _unlockFieldsFunction;
                list = new List<T>();
                fieldName = _fieldName;
            }

            public T this[int index] {
                get
                {
                    try
                    {
                        lockFunction.Invoke();
                        return list[index];
                    }
                    finally
                    {
                        unlockFunction.Invoke();
                    }
                }

                set
                {
                    try
                    {
                        lockFunction.Invoke();
                        list[index] = value;
                    }
                    finally
                    {
                        unlockFunction.Invoke();
                    }
                }
            }

            public int Count
            {
                get
                {
                    try
                    {
                        lockFunction.Invoke();
                        return list.Count;
                    }
                    finally
                    {
                        unlockFunction.Invoke();
                    }
                }
            }

            public bool IsReadOnly
            {
                get
                {
                    return false;
                }
            }

            public void Add(T item)
            {
                try
                {
                    lockFunction.Invoke();
                    lockFieldsFunction.Invoke();
                    property.FieldsChanged.Add(fieldName);
                    list.Add(item);
                }
                finally
                {
                    unlockFieldsFunction.Invoke();
                    unlockFunction.Invoke();
                }
            }

            public void Clear()
            {
                try
                {
                    lockFunction.Invoke();
                    lockFieldsFunction.Invoke();
                    property.FieldsChanged.Add(fieldName);
                    list.Clear();
                }
                finally
                {
                    unlockFieldsFunction.Invoke();
                    unlockFunction.Invoke();
                }
            }

            public bool Contains(T item)
            {
                try
                {
                    lockFunction.Invoke();
                    return list.Contains(item);
                }
                finally
                {
                    unlockFunction.Invoke();
                }
            }

            public void CopyTo(T[] array, int arrayIndex)
            {
                try
                {
                    lockFunction.Invoke();
                    lockFieldsFunction.Invoke();
                    property.FieldsChanged.Add(fieldName);
                    list.CopyTo(array, arrayIndex);
                }
                finally
                {
                    unlockFieldsFunction.Invoke();
                    unlockFunction.Invoke();
                }
            }

            public IEnumerator<T> GetEnumerator()
            {
                try
                {
                    lockFunction.Invoke();
                    return new InstantWriteBytesEnumerator(property, list.GetEnumerator(), lockFunction, unlockFunction, lockFieldsFunction, unlockFieldsFunction, fieldName);
                }
                finally
                {
                    unlockFunction.Invoke();
                }
            }

            public int IndexOf(T item)
            {
                try
                {
                    lockFunction.Invoke();
                    return list.IndexOf(item);
                }
                finally
                {
                    unlockFunction.Invoke();
                }
            }

            public void Insert(int index, T item)
            {
                try
                {
                    lockFunction.Invoke();
                    lockFieldsFunction.Invoke();
                    property.FieldsChanged.Add(fieldName);
                    list.Insert(index, item);
                }
                finally
                {
                    unlockFieldsFunction.Invoke();
                    unlockFunction.Invoke();
                }
            }

            public bool Remove(T item)
            {
                try
                {
                    lockFunction.Invoke();
                    lockFieldsFunction.Invoke();
                    property.FieldsChanged.Add(fieldName);
                    return list.Remove(item);
                }
                finally
                {
                    unlockFieldsFunction.Invoke();
                    unlockFunction.Invoke();
                }
            }

            public void RemoveAt(int index)
            {
                try
                {
                    lockFunction.Invoke();
                    lockFieldsFunction.Invoke();
                    property.FieldsChanged.Add(fieldName);
                    list.RemoveAt(index);
                }
                finally
                {
                    unlockFieldsFunction.Invoke();
                    unlockFunction.Invoke();
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                try
                {
                    lockFunction.Invoke();
                    return new InstantWriteBytesEnumerator(property, list.GetEnumerator(), lockFunction, unlockFunction, lockFieldsFunction, unlockFieldsFunction, fieldName);
                }
                finally
                {
                    unlockFunction.Invoke();
                }
            }
        }

        private string? _memoryContainer { get; set; }
        private uint? _address { get; set; }
        private string? _addressString { get; set; }
        private int? _length { get; set; }
        private int? _size { get; set; }
        private string? _bits { get; set; }
        private string? _reference { get; set; }
        private string? _description { get; set; }
        private object? _value { get; set; }
        private byte[]? _bytes { get; set; }
        private byte[]? _bytesFrozen { get; set; }
        private string? _readFunction { get; set; }
        private string? _writeFunction { get; set; }
        private string? _afterReadValueExpression { get; set; }
        private string? _afterReadValueFunction { get; set; }
        private string? _beforeWriteValueFunction { get; set; }
        private IList<byte[]>? _immediateWriteBytes { get; set; }
        private object _immediateWriteBytesLock { get; set; }
        private IList<object?>? _immediateWriteValues { get; set;  }
        private object _fieldsChangedLock { get; set; }

        public string? MemoryContainer
        {
            get { return _memoryContainer; }
            set
            {
                if (value == _memoryContainer) { return; }

                FieldsChangedLock();
                FieldsChanged.Add("memoryContainer");
                FieldsChangedUnlock();
                _memoryContainer = value;
            }
        }

        public uint? Address
        {
            get { return _address; }
            set
            {
                if (value == _address) { return; }

                _address = value;
                _addressString = value.ToString();

                IsMemoryAddressSolved = true;
                GameHookEvent?.UpdateAddressFromProperty();

                FieldsChangedLock();
                FieldsChanged.Add("address");
                FieldsChangedUnlock();
            }
        }

        public string? AddressString
        {
            get { return _addressString; }
            set
            {
                if (value == _addressString) { return; }

                _addressString = value;

                IsMemoryAddressSolved = AddressMath.TrySolve(value, [], out var solvedAddress);

                if (IsMemoryAddressSolved == false)
                {
                    _address = null;
                }
                else
                {
                    _address = solvedAddress;
                }
                GameHookEvent?.UpdateAddressFromProperty();

                FieldsChangedLock();
                FieldsChanged.Add("address");
                FieldsChangedUnlock();
            }
        }

        public int? Length
        {
            get => _length;
            set
            {
                if (_length == value) return;

                FieldsChangedLock();
                FieldsChanged.Add("length");
                FieldsChangedUnlock();
                _length = value;
            }
        }

        public int? Size
        {
            get => _size;
            set
            {
                if (_size == value) return;

                FieldsChangedLock();
                FieldsChanged.Add("size");
                FieldsChangedUnlock();
                _size = value;
            }
        }

        public string? Bits
        {
            get => _bits;
            set
            {
                if (_bits == value) return;

                FieldsChangedLock();
                FieldsChanged.Add("bits");
                FieldsChangedUnlock();
                _bits = value;
            }
        }

        public string? Reference
        {
            get => _reference;
            set
            {
                if (_reference == value) return;

                FieldsChangedLock();
                FieldsChanged.Add("reference");
                FieldsChangedUnlock();
                _reference = value;
            }
        }

        public string? Description
        {
            get => _description;
            set
            {
                if (_description == value) return;

                FieldsChangedLock();
                FieldsChanged.Add("description");
                FieldsChangedUnlock();
                _description = value;
            }
        }

        public object? Value
        {
            get => _value;
            set
            {
                if (_value != null && _value.Equals(value)) return;

                FieldsChangedLock();
                FieldsChanged.Add("value");
                FieldsChangedUnlock();
                _value = value;
            }
        }

        public byte[]? Bytes
        {
            get => _bytes;
            set
            {
                if (_bytes != null && value != null && _bytes.SequenceEqual(value)) return;

                FieldsChangedLock();
                FieldsChanged.Add("bytes");
                FieldsChangedUnlock();
                _bytes = value;
            }
        }

        public byte[]? BytesFrozen
        {
            get => _bytesFrozen;
            set
            {
                if (_bytesFrozen != null && value != null && _bytesFrozen.SequenceEqual(value)) return;

                FieldsChangedLock();
                FieldsChanged.Add("frozen");
                FieldsChangedUnlock();
                _bytesFrozen = value;
            }
        }

        public string? ReadFunction
        {
            get => _readFunction;
            set
            {
                if (_readFunction == value) return;

                FieldsChangedLock();
                FieldsChanged.Add("readFunction");
                FieldsChangedUnlock();
                _readFunction = value;
            }
        }

        public string? WriteFunction
        {
            get => _writeFunction;
            set
            {
                if (_writeFunction == value) return;

                FieldsChangedLock();
                FieldsChanged.Add("writeFunction");
                FieldsChangedUnlock();
                _writeFunction = value;
            }
        }

        public string? AfterReadValueExpression
        {
            get => _afterReadValueExpression;
            set
            {
                if (_afterReadValueExpression == value) return;

                FieldsChangedLock();
                FieldsChanged.Add("afterReadValueExpression");
                FieldsChangedUnlock();
                _afterReadValueExpression = value;
            }
        }

        public string? AfterReadValueFunction
        {
            get => _afterReadValueFunction;
            set
            {
                if (_afterReadValueFunction == value) return;

                FieldsChangedLock();
                FieldsChanged.Add("afterReadValueFunction");
                FieldsChangedUnlock();
                _afterReadValueFunction = value;
            }
        }

        public string? BeforeWriteValueFunction
        {
            get => _beforeWriteValueFunction;
            set
            {
                if (_beforeWriteValueFunction == value) return;

                FieldsChangedLock();
                FieldsChanged.Add("beforeWriteValueFunction");
                FieldsChangedUnlock();
                _beforeWriteValueFunction = value;
            }
        }

        public IList<byte[]>? ImmediateWriteBytes
        {
            get 
            { 
                lock(_immediateWriteBytesLock) 
                    return _immediateWriteBytes; 
            }
            set
            {
                lock (_immediateWriteBytesLock)
                {
                    if (_immediateWriteBytes != null && _immediateWriteBytes.Equals(value)) return;

                    FieldsChangedLock();
                    FieldsChanged.Add("immediateWriteBytes");
                    FieldsChangedUnlock();
                    _immediateWriteBytes = value;
                }
            }
        }

        public IList<object?>? ImmediateWriteValues { 
            get
            {
                lock (_immediateWriteBytesLock)
                    return _immediateWriteValues;
            }

            set
            {
                lock (_immediateWriteBytesLock)
                {
                    if (_immediateWriteValues != null && _immediateWriteValues.Equals(value)) return;

                    FieldsChangedLock();
                    FieldsChanged.Add("immediateWriteValues");
                    FieldsChangedUnlock();
                    _immediateWriteValues = value;
                }
            }
        }

        public void ImediateWriteBytesLock()
        {
            Monitor.Enter(_immediateWriteBytesLock);
        }
        public void ImediateWriteBytesUnlock()
        {
            Monitor.Exit(_immediateWriteBytesLock);
        }

        public void FieldsChangedLock()
        {
            Monitor.Enter(_fieldsChangedLock);
        }
        public void FieldsChangedUnlock()
        {
            Monitor.Exit(_fieldsChangedLock);
        }
    }
}
