using BizHawk.Common;
using BizHawk.Emulation.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static BizHawk.Emulation.Cores.Nintendo.N64.N64SyncSettings;
using static GameHook.Integrations.BizHawk.BizHawkInterface;

namespace GameHook.Integrations.BizHawk
{
    public static class ByteOperators
    {
        public static T[][] Split<T>(this T[] data, T[] splitBy)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (splitBy == null)
                throw new ArgumentNullException(nameof(splitBy));

            int[] indecies = data.Select((x, idx) => new { val = x, index = idx }).Where(x => splitBy.Any(y => y != null && y.Equals(x.val))).Select(x => x.index).ToArray();
            ArraySegment<T>[] segments;
            if (indecies.Length > 0 && indecies.Last() < data.Length - 1)
            {
                segments = indecies.Select((x, idx) => (idx == 0) ? new ArraySegment<T>(data, 0, x) : new ArraySegment<T>(data, indecies[idx - 1] + 1, x - indecies[idx - 1] - 1)).Append(new ArraySegment<T>(data, indecies[indecies.Length - 1] + 1, data.Length - indecies[indecies.Length - 1] - 1)).ToArray();
            }
            else if (indecies.Length == 0)
            {
                segments = new ArraySegment<T>[1] { new(data) };
            }
            else
            {
                segments = indecies.Select((x, idx) => (idx == 0) ? new ArraySegment<T>(data, 0, x) : new ArraySegment<T>(data, indecies[idx - 1] + 1, x - indecies[idx - 1] - 1)).ToArray();
            }

            return segments.Select(x => x.ToArray() ?? throw new NullReferenceException()).ToArray();
        }
    }

    internal static class BizHawkInterface
    {
        public class EventAddressRegisterOverride : ISerializable
        {
            public string Register;
            public ulong Value;

            public EventAddressRegisterOverride() : this("", 0x00)
            {
            }

            public EventAddressRegisterOverride(string registerName, ulong value)
            {
                Register = registerName;
                Value = value;
            }

            public byte[] Serialize()
            {
                byte[] registerBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(Register)));
                byte[] valueBytesNonBase64 = BitConverter.GetBytes(Value);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(valueBytesNonBase64);
                byte[] valueBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(valueBytesNonBase64));
                byte[] res = registerBytes.Concat(new byte[1] { 0 }).Concat(valueBytes).ToArray();
                byte[] resLengthNonBase64 = BitConverter.GetBytes(res.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(resLengthNonBase64);
                byte[] resLength = Encoding.ASCII.GetBytes(Convert.ToBase64String(resLengthNonBase64));
                return resLength.Concat(new byte[1] { 0 }).Concat(res).Concat(new byte[1] { 0 }).ToArray();
            }

            public static EventAddressRegisterOverride Deserialize(byte[] bytes)
            {
                if (bytes == null)
                    throw new ArgumentNullException(nameof(bytes));
                else if (bytes.Length < sizeof(int))
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                else if (bytes.Where(b => b == 0).Count() != 3)
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                else
                {
                    byte[][] segments = bytes.Split(new byte[1] { 0 });
                    if (segments.Length != 3)
                        throw new ArgumentOutOfRangeException(nameof(bytes));
                    byte[] lengthBytes = Convert.FromBase64String(Encoding.ASCII.GetString(segments[0]));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(lengthBytes);
                    int length = BitConverter.ToInt32(lengthBytes, 0);
                    if (length != segments[1].Length + 1 + segments[2].Length)
                        throw new ArgumentOutOfRangeException(nameof(bytes));
                    string registerName = Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.ASCII.GetString(segments[1])));
                    byte[] valueBytes = Convert.FromBase64String(Encoding.ASCII.GetString(segments[2]));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(valueBytes);
                    ulong value = BitConverter.ToUInt64(valueBytes, 0);
                    return new EventAddressRegisterOverride(registerName, value);
                }
                throw new NotImplementedException();
            }
        }

        public class EventAddress : ISerializable
        {
            public bool Active;
            public long Address;
            public ushort Bank;
            public EventType EventType;
            public EventAddressRegisterOverride[] EventAddressRegisterOverrides;
            public string Bits;
            public int Length;
            public int Size;
            public string Name;
            public bool Instantaneous;

            public EventAddress() : this(null, false, 0x00, ushort.MaxValue, EventType.EventType_Undefined, new List<EventAddressRegisterOverride> { }, null, 1, 0, false)
            {
            }

            public EventAddress(string? name, bool active, long address, ushort bank, EventType eventType, IEnumerable<EventAddressRegisterOverride> eventAddressRegisterOverrides, string? bits, int length, int size, bool instantaneous)
            {
                Active = active;
                Address = address;
                Bank = bank;
                EventType = eventType;
                EventAddressRegisterOverrides = eventAddressRegisterOverrides?.ToArray() ?? new EventAddressRegisterOverride[0];
                Bits = bits ?? "";
                Length = length;
                Size = (size > 0) ? size : 1;
                Name = name ?? "";
                Instantaneous = instantaneous;
            }

            public byte[] Serialize()
            {
                byte[] activeBytes = BitConverter.GetBytes(Active);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(activeBytes);
                activeBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(activeBytes));
                byte[] addressBytes = BitConverter.GetBytes(Address);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(addressBytes);
                addressBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(addressBytes));
                byte[] bankBytes = BitConverter.GetBytes(Bank);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(bankBytes);
                bankBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(bankBytes));
                byte[] eventTypeBytes = BitConverter.GetBytes(Convert.ToInt32(EventType));
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(eventTypeBytes);
                eventTypeBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(eventTypeBytes));
                byte[] eventAddressRegisterOverridesLengthBytes = BitConverter.GetBytes(EventAddressRegisterOverrides.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(eventAddressRegisterOverridesLengthBytes);
                string eventAddressRegisterOverridesLengthString = Convert.ToBase64String(eventAddressRegisterOverridesLengthBytes);
                IEnumerable<string> eventAddressRegisterOverridesStrings = EventAddressRegisterOverrides.Select(x => Convert.ToBase64String(x.Serialize()));
                eventAddressRegisterOverridesStrings = new string[1] { eventAddressRegisterOverridesLengthString }.Concat(eventAddressRegisterOverridesStrings);
                byte[] eventAddressRegisterOverridesBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Join("\0", eventAddressRegisterOverridesStrings.ToArray()))));
                byte[] bitsBytes = Encoding.UTF8.GetBytes(Bits);
                bitsBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(bitsBytes));
                byte[] lengthBytes = BitConverter.GetBytes(Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(lengthBytes);
                lengthBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(lengthBytes));
                byte[] sizeBytes = BitConverter.GetBytes(Size);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(sizeBytes);
                sizeBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(sizeBytes));
                byte[] nameBytes = UTF8Encoding.UTF8.GetBytes(Name);
                nameBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(nameBytes));
                byte[] instantaneousBytes = BitConverter.GetBytes(Instantaneous);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(instantaneousBytes);
                instantaneousBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(instantaneousBytes));

                byte[] res = activeBytes.Concat(new byte[1] { 0 })
                    .Concat(addressBytes).Concat(new byte[1] { 0 })
                    .Concat(bankBytes).Concat(new byte[1] { 0 })
                    .Concat(eventTypeBytes).Concat(new byte[1] { 0 })
                    .Concat(eventAddressRegisterOverridesBytes).Concat(new byte[1] { 0 })
                    .Concat(bitsBytes).Concat(new byte[1] { 0 })
                    .Concat(lengthBytes).Concat(new byte[1] { 0 })
                    .Concat(sizeBytes).Concat(new byte[1] { 0 })
                    .Concat(nameBytes).Concat(new byte[1] { 0 })
                    .Concat(instantaneousBytes)
                    .ToArray();
                byte[] resLengthNonBase64 = BitConverter.GetBytes(res.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(resLengthNonBase64);
                byte[] resLength = Encoding.ASCII.GetBytes(Convert.ToBase64String(resLengthNonBase64));
                return resLength.Concat(new byte[1] { 0 }).Concat(res).Concat(new byte[1] { 0 }).ToArray();
            }

            public static EventAddress Deserialize(byte[] bytes)
            {
                if (bytes == null)
                    throw new ArgumentNullException(nameof(bytes));
                else if (bytes.Length < sizeof(int))
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                else if (bytes.Where(b => b == 0).Count() != 11)
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                else
                {
                    byte[][] segments = bytes.Split(new byte[1] { 0 });
                    if (segments.Length != 11)
                        throw new ArgumentOutOfRangeException(nameof(bytes));
                    byte[] lenBytes = Convert.FromBase64String(Encoding.ASCII.GetString(segments[0]));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(lenBytes);
                    int strLength = BitConverter.ToInt32(lenBytes, 0);
                    if (strLength != segments[1].Length + 1
                        + segments[2].Length + 1
                        + segments[3].Length + 1
                        + segments[4].Length + 1
                        + segments[5].Length + 1
                        + segments[6].Length + 1
                        + segments[7].Length + 1
                        + segments[8].Length + 1
                        + segments[9].Length + 1
                        + segments[10].Length)
                        throw new ArgumentOutOfRangeException(nameof(bytes));

                    byte[] activeBytes = segments[1];
                    byte[] addressBytes = segments[2];
                    byte[] bankBytes = segments[3];
                    byte[] eventTypeBytes = segments[4];
                    byte[] eventAddressRegisterOverridesBytes = segments[5];
                    byte[] bitsBytes = segments[6];
                    byte[] lengthBytes = segments[7];
                    byte[] sizeBytes = segments[8];
                    byte[] nameBytes = segments[9];
                    byte[] instantaneousBytes = segments[10];

                    activeBytes = Convert.FromBase64String(Encoding.ASCII.GetString(activeBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(activeBytes);
                    bool active = BitConverter.ToBoolean(activeBytes, 0);

                    addressBytes = Convert.FromBase64String(Encoding.ASCII.GetString(addressBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(addressBytes);
                    long address = BitConverter.ToInt64(addressBytes, 0);

                    bankBytes = Convert.FromBase64String(Encoding.ASCII.GetString(bankBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(bankBytes);
                    ushort bank = BitConverter.ToUInt16(bankBytes, 0);

                    eventTypeBytes = Convert.FromBase64String(Encoding.ASCII.GetString(eventTypeBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(eventTypeBytes);
                    int eventTypeInt = BitConverter.ToInt32(eventTypeBytes, 0);
                    EventType eventType = (EventType)Enum.ToObject(typeof(EventType), eventTypeInt);

                    eventAddressRegisterOverridesBytes = Convert.FromBase64String(Encoding.ASCII.GetString(eventAddressRegisterOverridesBytes));
                    string[] eventAddressRegisterOverridesStrings = Encoding.ASCII.GetString(eventAddressRegisterOverridesBytes).Split('\0');
                    if(eventAddressRegisterOverridesStrings.Length == 0)
                        throw new ArgumentOutOfRangeException(nameof(bytes));
                    byte[] eventAddressRegisterOverridesLengthBytes = Convert.FromBase64String(eventAddressRegisterOverridesStrings[0]);
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(eventAddressRegisterOverridesLengthBytes);
                    int eventAddressRegisterOverridesLength = BitConverter.ToInt32(eventAddressRegisterOverridesLengthBytes, 0);

                    EventAddressRegisterOverride[] eventAddressRegisterOverrides;
                    if (eventAddressRegisterOverridesLength == 0 && eventAddressRegisterOverridesStrings.Length != 1)
                        throw new ArgumentOutOfRangeException(nameof(bytes));
                    else
                    {
                        int compLength = eventAddressRegisterOverridesStrings.Length - 1;
                        if (compLength != eventAddressRegisterOverridesLength)
                            throw new ArgumentOutOfRangeException(nameof(bytes));
                        eventAddressRegisterOverrides = eventAddressRegisterOverridesStrings.Skip(1).Select(x => EventAddressRegisterOverride.Deserialize(Convert.FromBase64String(x))).ToArray();
                    }

                    bitsBytes = Convert.FromBase64String(Encoding.ASCII.GetString(bitsBytes));
                    string bits = Encoding.UTF8.GetString(bitsBytes);

                    lengthBytes = Convert.FromBase64String(Encoding.ASCII.GetString(lengthBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(lengthBytes);
                    int length = BitConverter.ToInt32(lengthBytes, 0);

                    sizeBytes = Convert.FromBase64String(Encoding.ASCII.GetString(sizeBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(sizeBytes);
                    int size = BitConverter.ToInt32(sizeBytes, 0);

                    nameBytes = Convert.FromBase64String(Encoding.ASCII.GetString(nameBytes));
                    string name = Encoding.UTF8.GetString(nameBytes);

                    instantaneousBytes = Convert.FromBase64String(Encoding.ASCII.GetString(instantaneousBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(instantaneousBytes);
                    bool instantaneous = BitConverter.ToBoolean(instantaneousBytes, 0);

                    return new EventAddress(name, active, address, bank, eventType, eventAddressRegisterOverrides, bits, length, size, instantaneous);
                }
                throw new NotImplementedException();
            }
        }

        [Flags]
        public enum EventType
        {
            EventType_Undefined = 0,
            EventType_Read = 1,
            EventType_Write = 2,
            EventType_Execute = 4,
            EventType_HardReset = 8,
            EventType_SoftReset = 16,
            EventType_ReadWrite = EventType_Read | EventType_Write,
            EventType_ReadExecute = EventType_Read | EventType_Execute,
            EventType_WriteExecute = EventType_Write | EventType_Execute,
            EventType_ReadWriteExecute = EventType_Read | EventType_Write | EventType_Execute,
        }

        public enum EventOperationType
        {
            EventOperationType_Undefined = 0,
            EventOperationType_Clear = 1,
            EventOperationType_Add = 2,
            EventOperationType_Remove = 3,
        }
        public class EventOperation : ISerializable
        {
            private readonly EventOperationType _opType = EventOperationType.EventOperationType_Undefined;
            private readonly ulong? _eventSerial = 0;
            private readonly EventAddress? _eventAddress = null;
            private readonly EventType _eventType = EventType.EventType_Undefined;

            public EventOperation(EventOperationType opType, EventType eventType, ulong? eventSerial, EventAddress? eventAddress)
            {
                _opType = opType;
                _eventSerial = eventSerial;
                _eventAddress = eventAddress;
                _eventType = eventType;
            }

            public byte[] Serialize()
            {
                int opTypeInt = Convert.ToInt32(_opType);
                byte[] opTypeBytes = BitConverter.GetBytes(opTypeInt);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(opTypeBytes);
                opTypeBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(opTypeBytes));

                byte[] eventTypeBytes = BitConverter.GetBytes(Convert.ToInt32(_eventType));
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(eventTypeBytes);
                eventTypeBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(eventTypeBytes));

                byte[] eventSerialBytes = BitConverter.GetBytes(_eventSerial ?? 0);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(eventSerialBytes);
                eventSerialBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(eventSerialBytes));

                byte[] eventAddressBytes = new byte[0];
                if (_eventAddress != null)
                {
                    eventAddressBytes = _eventAddress.Serialize();
                }
                eventAddressBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(eventAddressBytes));

                int eventAddressBytesLength = eventAddressBytes.Length;
                byte[] eventAddressBytesLengthByte = BitConverter.GetBytes(eventAddressBytesLength);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(eventAddressBytesLengthByte);
                eventAddressBytesLengthByte = Encoding.ASCII.GetBytes(Convert.ToBase64String(eventAddressBytesLengthByte));

                byte[] res = opTypeBytes.Concat(new byte[1] { 0 })
                    .Concat(eventTypeBytes).Concat(new byte[1] { 0 })
                    .Concat(eventSerialBytes).Concat(new byte[1] { 0 })
                    .Concat(eventAddressBytesLengthByte).Concat(new byte[1] { 0 })
                    .Concat(eventAddressBytes).ToArray();
                byte[] resLengthNonBase64 = BitConverter.GetBytes(res.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(resLengthNonBase64);
                byte[] resLength = Encoding.ASCII.GetBytes(Convert.ToBase64String(resLengthNonBase64));
                return resLength.Concat(new byte[1] { 0 }).Concat(res).Concat(new byte[1] { 0 }).ToArray();
            }

            public static EventOperation Deserialize(byte[] bytes)
            {
                if (bytes == null)
                    throw new ArgumentNullException(nameof(bytes));
                else if (bytes.Length < sizeof(int))
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                else if (bytes.Where(b => b == 0).Count() != 6)
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                else
                {
                    byte[][] segments = bytes.Split(new byte[1] { 0 });
                    if (segments.Length != 6)
                        throw new ArgumentOutOfRangeException(nameof(bytes));
                    byte[] lenBytes = Convert.FromBase64String(Encoding.ASCII.GetString(segments[0]));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(lenBytes);
                    int strLength = BitConverter.ToInt32(lenBytes, 0);
                    if (strLength != segments[1].Length + 1
                        + segments[2].Length + 1
                        + segments[3].Length + 1
                        + segments[4].Length + 1
                        + segments[5].Length)
                        throw new ArgumentOutOfRangeException(nameof(bytes));

                    byte[] opTypeBytes = segments[1];
                    byte[] eventTypeBytes = segments[2];
                    byte[] eventSerialBytes = segments[3];
                    byte[] eventAddressBytesLengthByte = segments[4];
                    byte[] eventAddressBytes = segments[5];

                    opTypeBytes = Convert.FromBase64String(Encoding.ASCII.GetString(opTypeBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(opTypeBytes);
                    int opTypeInt = BitConverter.ToInt32(opTypeBytes, 0);
                    EventOperationType eventOperationType = (EventOperationType)Enum.ToObject(typeof(EventOperationType), opTypeInt);

                    eventTypeBytes = Convert.FromBase64String(ASCIIEncoding.ASCII.GetString(eventTypeBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(eventTypeBytes);
                    int eventTypeInt = BitConverter.ToInt32(eventTypeBytes, 0);
                    EventType eventType = (EventType)Enum.ToObject(typeof(EventType), eventTypeInt);

                    eventSerialBytes = Convert.FromBase64String(Encoding.ASCII.GetString(eventSerialBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(eventSerialBytes);
                    ulong eventSerialULong = BitConverter.ToUInt64(eventSerialBytes, 0);
                    ulong? eventSerial = (eventSerialULong == 0) ? null : eventSerialULong;

                    eventAddressBytesLengthByte = Convert.FromBase64String(Encoding.ASCII.GetString(eventAddressBytesLengthByte));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(eventAddressBytesLengthByte);
                    int eventAddressBytesLength = BitConverter.ToInt32(eventAddressBytesLengthByte, 0);

                    if (eventAddressBytesLength != eventAddressBytes.Length)
                        throw new ArgumentOutOfRangeException(nameof(bytes));

                    eventAddressBytes = Convert.FromBase64String(Encoding.ASCII.GetString(eventAddressBytes));
                    EventAddress? eventAddress = (eventAddressBytes.Length > 0) ? EventAddress.Deserialize(eventAddressBytes) : null;

                    return new EventOperation(eventOperationType, eventType, eventSerial, eventAddress);
                }
            }

            public EventOperationType OpType
            {
                get
                {
                    return _opType;
                }
            }
            public ulong? EventSerial
            {
                get
                {
                    return _eventSerial;
                }
            }
            public EventAddress? EventAddress
            {
                get
                {
                    return _eventAddress;
                }
            }

            public EventType EventType
            {
                get
                {
                    return _eventType;
                }
            }
        }

        public class WriteCall : ISerializable
        {
            private readonly bool _active;
            private readonly bool _frozen;
            private readonly long _address;
            private readonly byte[] _writeByte;

            public WriteCall() : this(false, false, 0x00, new byte[0])
            {
            }
            public WriteCall(bool active, bool frozen, long address, byte[] bytes)
            {
                _active = active;
                _frozen = frozen;
                _address = address;
                _writeByte = bytes;
            }

            public byte[] Serialize()
            {
                byte[] activeBytes = BitConverter.GetBytes(_active);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(activeBytes);
                activeBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(activeBytes));

                byte[] frozenBytes = BitConverter.GetBytes(_frozen);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(frozenBytes);
                frozenBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(frozenBytes));

                byte[] addressBytes = BitConverter.GetBytes(_address);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(addressBytes);
                addressBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(addressBytes));

                byte[] writeByteBytes = new byte[0];
                if (_writeByte != null)
                {
                    writeByteBytes = (byte[])_writeByte.Clone();
                }
                writeByteBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(writeByteBytes));

                int writeByteBytesLength = writeByteBytes.Length;
                byte[] writeByteBytesLengthByte = BitConverter.GetBytes(writeByteBytesLength);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(writeByteBytesLengthByte);
                writeByteBytesLengthByte = Encoding.ASCII.GetBytes(Convert.ToBase64String(writeByteBytesLengthByte));

                byte[] res = activeBytes.Concat(new byte[1] { 0 })
                    .Concat(frozenBytes).Concat(new byte[1] { 0 })
                    .Concat(addressBytes).Concat(new byte[1] { 0 })
                    .Concat(writeByteBytesLengthByte).Concat(new byte[1] { 0 })
                    .Concat(writeByteBytes).ToArray();
                byte[] resLengthNonBase64 = BitConverter.GetBytes(res.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(resLengthNonBase64);
                byte[] resLength = Encoding.ASCII.GetBytes(Convert.ToBase64String(resLengthNonBase64));
                return resLength.Concat(new byte[1] { 0 }).Concat(res).Concat(new byte[1] { 0 }).ToArray();
            }

            public static WriteCall Deserialize(byte[] bytes)
            {
                if (bytes == null)
                    throw new ArgumentNullException(nameof(bytes));
                else if (bytes.Length < sizeof(int))
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                else if (bytes.Where(b => b == 0).Count() != 6)
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                else
                {
                    byte[][] segments = bytes.Split(new byte[1] { 0 });
                    if (segments.Length != 6)
                        throw new ArgumentOutOfRangeException(nameof(bytes));
                    byte[] lenBytes = Convert.FromBase64String(Encoding.ASCII.GetString(segments[0]));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(lenBytes);
                    int strLength = BitConverter.ToInt32(lenBytes, 0);
                    if (strLength != segments[1].Length + 1
                        + segments[2].Length + 1
                        + segments[3].Length + 1
                        + segments[4].Length + 1
                        + segments[5].Length)
                        throw new ArgumentOutOfRangeException(nameof(bytes));

                    byte[] activeBytes = segments[1];
                    byte[] frozenBytes = segments[2];
                    byte[] addressBytes = segments[3];
                    byte[] writeByteBytesLengthByte = segments[4];
                    byte[] writeByteBytes = segments[5];

                    activeBytes = Convert.FromBase64String(Encoding.ASCII.GetString(activeBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(activeBytes);
                    bool active = BitConverter.ToBoolean(activeBytes, 0);

                    frozenBytes = Convert.FromBase64String(ASCIIEncoding.ASCII.GetString(frozenBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(frozenBytes);
                    bool frozen = BitConverter.ToBoolean(frozenBytes, 0);

                    addressBytes = Convert.FromBase64String(Encoding.ASCII.GetString(addressBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(addressBytes);
                    long address = BitConverter.ToInt64(addressBytes, 0);

                    writeByteBytesLengthByte = Convert.FromBase64String(Encoding.ASCII.GetString(writeByteBytesLengthByte));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(writeByteBytesLengthByte);
                    int writeByteBytesLength = BitConverter.ToInt32(writeByteBytesLengthByte, 0);

                    if (writeByteBytesLength != writeByteBytes.Length)
                        throw new ArgumentOutOfRangeException(nameof(bytes));

                    writeByteBytes = Convert.FromBase64String(Encoding.ASCII.GetString(writeByteBytes));
                    byte[] writeByte = (byte[])writeByteBytes.Clone();

                    return new WriteCall(active, frozen, address, writeByte);
                }
            }

            public bool Active
            {
                get
                {
                    return _active;
                }
            }

            public bool Frozen
            {
                get
                {
                    return _frozen;
                }
            }

            public long Address
            {
                get
                {
                    return _address;
                }
            }

            public byte[] WriteByte
            {
                get
                {
                    return _writeByte;
                }
            }
        }

        public static EventType MemoryCallbackTypeToEventType(MemoryCallbackType memoryCallbackType)
        {
            if (memoryCallbackType == MemoryCallbackType.Read)
            {
                return EventType.EventType_Read;
            }
            else if (memoryCallbackType == MemoryCallbackType.Write)
            {
                return EventType.EventType_Write;
            }
            else if (memoryCallbackType == MemoryCallbackType.Execute)
            {
                return EventType.EventType_Execute;
            }
            else
            {
                return EventType.EventType_Undefined;
            }
        }

        public static MemoryCallbackType EventTypeToMemoryCallbackType(EventType eventType)
        {
            if (eventType == EventType.EventType_Read)
            {
                return MemoryCallbackType.Read;
            }
            else if (eventType == EventType.EventType_Write)
            {
                return MemoryCallbackType.Write;
            }
            else if (eventType == EventType.EventType_Execute)
            {
                return MemoryCallbackType.Execute;
            }
            else
            {
                throw new ArgumentOutOfRangeException();
            }
        }
    }
}
