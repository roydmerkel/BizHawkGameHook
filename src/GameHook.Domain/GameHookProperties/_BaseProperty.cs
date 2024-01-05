﻿using GameHook.Domain.Interfaces;

namespace GameHook.Domain.GameHookProperties
{
    public abstract class BaseProperty : IGameHookProperty
    {
        public BaseProperty(IGameHookInstance instance, GameHookMapperVariables mapperVariables)
        {
            Instance = instance;
            MapperVariables = mapperVariables;
            Path = MapperVariables.Path;
            Type = MapperVariables.Type;
            Length = MapperVariables.Length;
            SetAddress(MapperVariables.Address);
            Position = MapperVariables.Position;
            Nibble = MapperVariables.Nibble;
            Reference = MapperVariables.Reference;
            Value = MapperVariables.StaticValue;
            Description = MapperVariables.Description;
        }

        private MemoryAddress? _address { get; set; }
        private object? _value { get; set; }
        private byte[]? _bytes { get; set; }
        private byte[]? _bytesFrozen { get; set; }

        private bool IsAddressMathSolved { get; set; }
        private bool ShouldRunReferenceTransformer
        {
            get { return (Type == "bit" || Type == "bool" || Type == "int" || Type == "uint") && Glossary != null; }
        }

        protected abstract object? ToValue(byte[] bytes);
        protected abstract byte[] FromValue(string value);

        protected IGameHookInstance Instance { get; }

        public GameHookMapperVariables MapperVariables { get; }

        public GlossaryList? Glossary
        {
            get
            {
                if (Instance.Mapper == null) throw new Exception("Instance.Mapper is NULL.");

                if (string.IsNullOrEmpty(MapperVariables.Reference) == false)
                {
                    return Instance.Mapper.Glossary[MapperVariables.Reference] ??
                           throw new MapperInitException($"Unable to load reference map '{MapperVariables.Reference}'. It was not found in the references section.");
                }

                return null;
            }
        }

        public HashSet<string> FieldsChanged { get; } = new();

        public string Path { get; }
        public string? Description { get; }
        public string Type { get; }
        public int? Length { get; }
        public int? Position { get; }
        public string? Nibble { get; }
        public string? Reference { get; }

        public bool IsReadOnly => Address == null;

        public string? AddressExpression { get; private set; }

        public MemoryAddress? Address
        {
            get => _address;
            set
            {
                if (_address == value) return;

                FieldsChanged.Add("address");
                _address = value;
            }
        }

        public object? Value
        {
            get => _value;
            set
            {
                if (_value == value) return;

                FieldsChanged.Add("value");
                _value = value;
            }
        }

        public byte[]? Bytes
        {
            get => _bytes;
            private set
            {
                if (_bytes != null && value != null && _bytes.SequenceEqual(value)) return;

                FieldsChanged.Add("bytes");
                _bytes = value;
            }
        }

        public byte[]? BytesFrozen
        {
            get => _bytesFrozen;
            private set
            {
                if (_bytesFrozen != null && value != null && _bytesFrozen.SequenceEqual(value)) return;

                FieldsChanged.Add("frozen");
                _bytesFrozen = value;
            }
        }

        public void ProcessLoop(IMemoryManager memoryContainer)
        {
            if (Instance == null) { throw new Exception("Instance is NULL."); }
            if (Instance.Mapper == null) { throw new Exception("Instance.Mapper is NULL."); }
            if (Instance.Driver == null) { throw new Exception("Instance.Driver is NULL."); }

            if (string.IsNullOrEmpty(MapperVariables.ReadFunction) == false)
            {
                // They want to do it themselves entirely in Javascript.
                Instance.Evalulate(MapperVariables.ReadFunction, this, null);

                return;
            }

            MemoryAddress? address = Address;
            byte[]? previousBytes = Bytes;
            byte[]? bytes = null;
            object? value;

            if (MapperVariables.StaticValue != null)
            {
                Value = MapperVariables.StaticValue;

                return;
            }

            if (Length == null)
            {
                throw new Exception("Length is NULL.");
            }

            if (string.IsNullOrEmpty(AddressExpression) == false && IsAddressMathSolved == false)
            {
                if (AddressMath.TrySolve(AddressExpression, Instance.Variables, out var solvedAddress))
                {
                    address = solvedAddress;
                }
                else
                {
                    // TODO: Write a log entry here.
                }
            }

            if (address == null && bytes == null)
            {
                // There is nothing to do for this property, as it does not have an address or bytes.
                // Hopefully a postprocessor will pick it up and set it's value!
                return;
            }

            if (bytes == null)
            {
                if (string.IsNullOrEmpty(MapperVariables.MemoryContainer))
                {
                    bytes = memoryContainer.DefaultNamespace.get_bytes(address ?? 0x00, Length ?? 1).Data;
                }
                else
                {
                    bytes = memoryContainer.Namespaces[MapperVariables.MemoryContainer].get_bytes(address ?? 0x00, Length ?? 0).Data;
                }
            }

            if (previousBytes != null && bytes != null && previousBytes.SequenceEqual(bytes))
            {
                // Fast path - if the bytes match, then we can assume the property has not been
                // updated since last poll.

                // Do nothing, we don't need to calculate the new value as
                // the bytes are the same.

                return;
            }

            if (bytes == null)
            {
                throw new Exception(
                    $"Unable to retrieve bytes for property '{Path}' at address {Address?.ToHexdecimalString()}. Is the address within the drivers' memory address block ranges?");
            }

            if (bytes.Length == 0)
            {
                throw new Exception(
                  $"Unable to retrieve bytes for property '{Path}' at address {Address?.ToHexdecimalString()}. A byte array length of zero was returned?");
            }

            if (string.IsNullOrEmpty(Nibble) == false)
            {
                if (Nibble == "high")
                {
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        bytes[i] &= 0xF0;
                    }
                }
                else if (Nibble == "low")
                {
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        bytes[i] &= 0x0F;
                    }
                }
                else
                {
                    throw new Exception($"Invalid nibble option provided: {Nibble}.");
                }
            }

            if (address != null && BytesFrozen != null && bytes.SequenceEqual(BytesFrozen) == false)
            {
                // Bytes have changed, but property is frozen, so force the bytes back to the original value.
                // Pretend nothing has changed. :)

                _ = Instance.Driver.WriteBytes((MemoryAddress)address, BytesFrozen);

                return;
            }

            value = ToValue(bytes);

            if (string.IsNullOrEmpty(MapperVariables.AfterReadValueExpression) == false)
            {
                value = Instance.Evalulate(MapperVariables.AfterReadValueExpression, value, null);
            }

            // Reference lookup
            if (ShouldRunReferenceTransformer)
            {
                if (Glossary == null)
                {
                    throw new Exception("Glossary is NULL.");
                }

                value = Glossary.GetSingleOrDefaultByKey(Convert.ToUInt64(value))?.Value;
            }

            Address = address;
            Bytes = bytes;
            Value = value;
        }

        public void SetAddress(string? addressExpression)
        {
            if (string.IsNullOrEmpty(addressExpression))
            {
                return;
            }

            AddressExpression = addressExpression;

            IsAddressMathSolved = AddressMath.TrySolve(addressExpression, new Dictionary<string, object?>(), out var address);

            if (IsAddressMathSolved)
            {
                Address = address;
            }
        }

        public async Task WriteValue(string value, bool? freeze)
        {
            if (string.IsNullOrEmpty(MapperVariables.WriteFunction) == false)
            {
                // They want to do it themselves entirely in Javascript.
                Instance.Evalulate(MapperVariables.WriteFunction, this, null);

                return;
            }

            byte[] bytes;
            if (ShouldRunReferenceTransformer)
            {
                if (Glossary == null)
                {
                    throw new Exception("Glossary is NULL.");
                }

                bytes = BitConverter.GetBytes(Glossary.GetSingleByValue(value).Key);
            }
            else
            {
                bytes = FromValue(value);
            }

            await WriteBytes(bytes, freeze);
        }

        public async Task WriteBytes(byte[] bytes, bool? freeze)
        {
            if (Instance == null) throw new Exception("Instance is NULL.");
            if (Instance.Driver == null) throw new Exception("Driver is NULL.");
            if (Address == null) throw new Exception($"{Path} does not have an address. Cannot write data to an empty address.");
            if (Length == null) throw new Exception($"{Path}'s length is NULL, so we can't write bytes.");

            var buffer = new byte[Length ?? 1];

            // Overlay the bytes onto the buffer.
            // This ensures that we can't overflow the property.
            // It also ensures it can't underflow the property, it copies the remaining from Bytes.
            for (int i = 0; i < buffer.Length; i++)
            {
                if (i < bytes.Length) buffer[i] = bytes[i];
                else if (Bytes != null) buffer[i] = Bytes[i];
            }

            if (BytesFrozen != null)
            {
                // The property is frozen, but we want to write bytes anyway.
                // So this should replace the existing frozen bytes.

                BytesFrozen = buffer;
            }

            await Instance.Driver.WriteBytes((MemoryAddress)Address, buffer);

            if (freeze == true) await FreezeProperty(buffer);
            else if (freeze == false) await UnfreezeProperty();
        }

        public async Task FreezeProperty(byte[] bytesFrozen)
        {
            BytesFrozen = bytesFrozen;

            FieldsChanged.Add("frozen");
            var propertyArray = new IGameHookProperty[] { this };
            foreach (var notifier in Instance.ClientNotifiers)
            {
                await notifier.SendPropertiesChanged(propertyArray);
            }
        }

        public async Task UnfreezeProperty()
        {
            FieldsChanged.Add("frozen");

            BytesFrozen = null;

            var propertyArray = new IGameHookProperty[] { this };
            foreach (var notifier in Instance.ClientNotifiers)
            {
                await notifier.SendPropertiesChanged(propertyArray);
            }
        }
    }
}