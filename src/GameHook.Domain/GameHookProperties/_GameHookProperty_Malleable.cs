using GameHook.Domain.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace GameHook.Domain.GameHookProperties
{
    public abstract partial class GameHookProperty : IGameHookProperty
    {
        private class InstantWriteBytes<T> : IList<T>
        {
            private class InstantWriteBytesEnumerator : IEnumerator<T>
            {
                private GameHookProperty property;
                private object lockObject;
                private IEnumerator<T> enumerator;
                private string fieldName;

                public InstantWriteBytesEnumerator(GameHookProperty _property, IEnumerator<T> _enumerator, object _lockObj, string _fieldName)
                {
                    property = _property;
                    lockObject = _lockObj;
                    enumerator = _enumerator;
                    fieldName = _fieldName;
                }

                public T Current
                {
                    get
                    {
                        lock (lockObject)
                            return enumerator.Current;
                    }
                }

                object IEnumerator.Current
                {
                    get
                    {
                        lock (lockObject)
                            return enumerator.Current;
                    }
                }

                public void Dispose()
                {
                    lock (lockObject)
                    {
                        property.FieldsChanged.Add(fieldName);
                        enumerator.Dispose();
                    }
                }

                public bool MoveNext()
                {
                    lock (lockObject)
                    {
                        return enumerator.MoveNext();
                    }
                }

                public void Reset()
                {
                    lock (lockObject)
                    {
                        property.FieldsChanged.Add(fieldName);
                        enumerator.Reset();
                    }
                }
            }

            private GameHookProperty property;
            private object lockObject;
            private List<T> list;
            private string fieldName;

            public InstantWriteBytes(GameHookProperty _property, object _lockObj, string _fieldName)
            {
                property = _property;
                lockObject = _lockObj;
                list = new List<T>();
                fieldName = _fieldName;
            }

            public T this[int index] {
                get
                {
                    lock(lockObject)
                        return list[index];
                }

                set
                {
                    lock(lockObject)
                        list[index] = value;
                }
            }

            public int Count
            {
                get
                {
                    lock (lockObject)
                        return list.Count;
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
                lock (lockObject)
                {
                    property.FieldsChanged.Add(fieldName);
                    list.Add(item);
                }
            }

            public void Clear()
            {
                lock (lockObject)
                {
                    property.FieldsChanged.Add(fieldName);
                    list.Clear();
                }
            }

            public bool Contains(T item)
            {
                lock (lockObject)
                {
                    return list.Contains(item);
                }
            }

            public void CopyTo(T[] array, int arrayIndex)
            {
                lock (lockObject)
                {
                    property.FieldsChanged.Add(fieldName);
                    list.CopyTo(array, arrayIndex);
                }
            }

            public IEnumerator<T> GetEnumerator()
            {
                lock (lockObject)
                {
                    return new InstantWriteBytesEnumerator(property, list.GetEnumerator(), lockObject, fieldName);
                }
            }

            public int IndexOf(T item)
            {
                lock (lockObject)
                {
                    return list.IndexOf(item);
                }
            }

            public void Insert(int index, T item)
            {
                lock (lockObject)
                {
                    property.FieldsChanged.Add(fieldName);
                    list.Insert(index, item);
                }
            }

            public bool Remove(T item)
            {
                lock (lockObject)
                {
                    property.FieldsChanged.Add(fieldName);
                    return list.Remove(item);
                }
            }

            public void RemoveAt(int index)
            {
                lock (lockObject)
                {
                    property.FieldsChanged.Add(fieldName);
                    list.RemoveAt(index);
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                lock (lockObject)
                {
                    return new InstantWriteBytesEnumerator(property, list.GetEnumerator(), lockObject, fieldName);
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

        public string? MemoryContainer
        {
            get { return _memoryContainer; }
            set
            {
                if (value == _memoryContainer) { return; }

                FieldsChanged.Add("memoryContainer");
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

                FieldsChanged.Add("address");
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

                FieldsChanged.Add("address");
            }
        }

        public int? Length
        {
            get => _length;
            set
            {
                if (_length == value) return;

                FieldsChanged.Add("length");
                _length = value;
            }
        }

        public int? Size
        {
            get => _size;
            set
            {
                if (_size == value) return;

                FieldsChanged.Add("size");
                _size = value;
            }
        }

        public string? Bits
        {
            get => _bits;
            set
            {
                if (_bits == value) return;

                FieldsChanged.Add("bits");
                _bits = value;
            }
        }

        public string? Reference
        {
            get => _reference;
            set
            {
                if (_reference == value) return;

                FieldsChanged.Add("reference");
                _reference = value;
            }
        }

        public string? Description
        {
            get => _description;
            set
            {
                if (_description == value) return;

                FieldsChanged.Add("description");
                _description = value;
            }
        }

        public object? Value
        {
            get => _value;
            set
            {
                if (_value != null && _value.Equals(value)) return;

                FieldsChanged.Add("value");
                _value = value;
            }
        }

        public byte[]? Bytes
        {
            get => _bytes;
            set
            {
                if (_bytes != null && value != null && _bytes.SequenceEqual(value)) return;

                FieldsChanged.Add("bytes");
                _bytes = value;
            }
        }

        public byte[]? BytesFrozen
        {
            get => _bytesFrozen;
            set
            {
                if (_bytesFrozen != null && value != null && _bytesFrozen.SequenceEqual(value)) return;

                FieldsChanged.Add("frozen");
                _bytesFrozen = value;
            }
        }

        public string? ReadFunction
        {
            get => _readFunction;
            set
            {
                if (_readFunction == value) return;

                FieldsChanged.Add("readFunction");
                _readFunction = value;
            }
        }

        public string? WriteFunction
        {
            get => _writeFunction;
            set
            {
                if (_writeFunction == value) return;

                FieldsChanged.Add("writeFunction");
                _writeFunction = value;
            }
        }

        public string? AfterReadValueExpression
        {
            get => _afterReadValueExpression;
            set
            {
                if (_afterReadValueExpression == value) return;

                FieldsChanged.Add("afterReadValueExpression");
                _afterReadValueExpression = value;
            }
        }

        public string? AfterReadValueFunction
        {
            get => _afterReadValueFunction;
            set
            {
                if (_afterReadValueFunction == value) return;

                FieldsChanged.Add("afterReadValueFunction");
                _afterReadValueFunction = value;
            }
        }

        public string? BeforeWriteValueFunction
        {
            get => _beforeWriteValueFunction;
            set
            {
                if (_beforeWriteValueFunction == value) return;

                FieldsChanged.Add("beforeWriteValueFunction");
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

                    FieldsChanged.Add("immediateWriteBytes");
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

                    FieldsChanged.Add("immediateWriteValues");
                    _immediateWriteValues = value;
                }
            }
        }
    }
}
