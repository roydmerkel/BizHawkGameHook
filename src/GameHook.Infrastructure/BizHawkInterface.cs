using GameHook.Domain;
using GameHook.Domain.Interfaces;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace GameHook.Infrastructure
{
    public static class ByteOperators
    {
        public static T[][] Split<T>(this T[] data, T[] splitBy)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(splitBy);

            int[] indecies = data.Select((x, idx) => new { val = x, index = idx }).Where(x => splitBy.Any(y => y != null && y.Equals(x.val))).Select(x => x.index).ToArray();
            ArraySegment<T>[] segments;
            if(indecies.Length > 0 && indecies.Last() < data.Length - 1)
            {
                segments = indecies.Select((x, idx) => (idx == 0) ? new ArraySegment<T>(data, 0, x) : new ArraySegment<T>(data, indecies[idx - 1] + 1, x - indecies[idx - 1] - 1)).Append(new ArraySegment<T>(data, indecies[^1] + 1, data.Length - indecies[^1] - 1)).ToArray();
            }
            else if(indecies.Length == 0)
            {
                segments = [ new ArraySegment<T>(data) ];
            }
            else
            {
                segments = indecies.Select((x, idx) => (idx == 0) ? new ArraySegment<T>(data, 0, x) : new ArraySegment<T>(data, indecies[idx - 1] + 1, x - indecies[idx - 1] - 1)).ToArray();
            }
            
            return segments.Select(x => x.Array ?? throw new NullReferenceException()).ToArray();
        }
    }
    internal static class BizHawkInterface
    {
        public struct EventAddressRegisterOverride(string registerName, ulong value) : ISerializable
        {
            public string Register = registerName ?? throw new ArgumentNullException(nameof(registerName)); // actually an 8 byte ascii string
            public ulong Value = value;

            public EventAddressRegisterOverride() : this("", 0x00)
            {
            }

            public readonly byte[] Serialize()
            {
                byte[] registerBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(Register)));
                byte[] valueBytesNonBase64 = BitConverter.GetBytes(Value);
                if(!BitConverter.IsLittleEndian)
                    Array.Reverse(valueBytesNonBase64);
                byte[] valueBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(valueBytesNonBase64));
                byte[] res = [.. registerBytes, .. new byte[1] { 0 }, .. valueBytes];
                byte[] resLengthNonBase64 = BitConverter.GetBytes(res.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(resLengthNonBase64);
                byte[] resLength = Encoding.ASCII.GetBytes(Convert.ToBase64String(resLengthNonBase64));
                return [.. resLength, .. new byte[1] { 0 }, .. res, .. new byte[1] { 0 }];
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
                    byte[][] segments = bytes.Split([ (byte)0 ]);
                    if(segments.Length != 3)
                        throw new ArgumentOutOfRangeException(nameof(bytes));
                    byte[] lengthBytes = Convert.FromBase64String(Encoding.ASCII.GetString(segments[0]));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(lengthBytes);
                    int length = BitConverter.ToInt32(lengthBytes, 0);
                    if(length != segments[1].Length + 1 + segments[2].Length)
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

        public class EventAddress(string? name, bool active, long address, ushort bank, EventType eventType, IEnumerable<EventAddressRegisterOverride> eventAddressRegisterOverrides, string? bits, int length, int size, bool instantaneous) : ISerializable
        {
            public bool Active = active;
            public long Address = address;
            public ushort Bank = bank;
            public EventType EventType = eventType;
            public EventAddressRegisterOverride[] EventAddressRegisterOverrides = eventAddressRegisterOverrides?.ToArray() ?? [];
            public string Bits = bits ?? "";
            public int Length = length;
            public int Size = (size > 0) ? size : 1;
            public string Name = name ?? "";
            public bool Instantaneous = instantaneous;

            public EventAddress() : this(null, false, 0x00, ushort.MaxValue, EventType.EventType_Undefined, new List<EventAddressRegisterOverride> { }, null, 1, 0, false)
            {
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

                byte[] res =
                [
                    .. activeBytes,
                    .. new byte[1] { 0 },
                    .. addressBytes,
                    .. new byte[1] { 0 },
                    .. bankBytes,
                    .. new byte[1] { 0 },
                    .. eventTypeBytes,
                    .. new byte[1] { 0 },
                    .. eventAddressRegisterOverridesBytes,
                    .. new byte[1] { 0 },
                    .. bitsBytes,
                    .. new byte[1] { 0 },
                    .. lengthBytes,
                    .. new byte[1] { 0 },
                    .. sizeBytes,
                    .. new byte[1] { 0 },
                    .. nameBytes,
                    .. new byte[1] { 0 },
                    .. instantaneousBytes,
                ];
                byte[] resLengthNonBase64 = BitConverter.GetBytes(res.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(resLengthNonBase64);
                byte[] resLength = Encoding.ASCII.GetBytes(Convert.ToBase64String(resLengthNonBase64));
                return [.. resLength, .. new byte[1] { 0 }, .. res, .. new byte[1] { 0 }];
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
                    byte[][] segments = bytes.Split([(byte)0]);
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
                    if (eventAddressRegisterOverridesStrings.Length == 0)
                        throw new ArgumentOutOfRangeException(nameof(bytes));
                    byte[] eventAddressRegisterOverridesLengthBytes = Convert.FromBase64String(eventAddressRegisterOverridesStrings[0]);
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(eventAddressRegisterOverridesLengthBytes);
                    int eventAddressRegisterOverridesLength = BitConverter.ToInt32(eventAddressRegisterOverridesLengthBytes, 0);

                    EventAddressRegisterOverride[] eventAddressRegisterOverrides;
                    if (eventAddressRegisterOverridesLength == 0 && eventAddressRegisterOverridesStrings.Length > 0)
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

        public enum EventOperationType
        {
            EventOperationType_Undefined = 0,
            EventOperationType_Clear = 1,
            EventOperationType_Add = 2,
            EventOperationType_Remove = 3,
        }
        public class EventOperation(EventOperationType opType, EventType eventType, ulong? eventSerial, EventAddress? eventAddress) : ISerializable
        {
            private readonly EventOperationType _opType = opType;
            private readonly ulong? _eventSerial = eventSerial;
            private readonly EventAddress? _eventAddress = eventAddress;
            private readonly EventType _eventType = eventType;

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

                byte[] eventAddressBytes = [];
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

                byte[] res = [.. opTypeBytes, .. new byte[1] { 0 },
                    .. eventTypeBytes, .. new byte[1] { 0 },
                    .. eventSerialBytes, .. new byte[1] { 0 },
                    .. eventAddressBytesLengthByte, .. new byte[1] { 0 }, 
                    .. eventAddressBytes];
                byte[] resLengthNonBase64 = BitConverter.GetBytes(res.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(resLengthNonBase64);
                byte[] resLength = Encoding.ASCII.GetBytes(Convert.ToBase64String(resLengthNonBase64));
                return [.. resLength, .. new byte[1] { 0 }, .. res, .. new byte[1] { 0 }];
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
                    byte[][] segments = bytes.Split([(byte)0]);
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

                    opTypeBytes = Convert.FromBase64String(ASCIIEncoding.ASCII.GetString(opTypeBytes));
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
                    return EventType;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct WriteCall
        {
            const int MaxWriteLength = 64 * 16 / 8;

            public bool Active;
            public bool Frozen;
            public long Address;
            public int WriteByteCount;
            public ulong WriteByte0;
            public ulong WriteByte1;
            public ulong WriteByte2;
            public ulong WriteByte3;
            public ulong WriteByte4;
            public ulong WriteByte5;
            public ulong WriteByte6;
            public ulong WriteByte7;
            public ulong WriteByte8;
            public ulong WriteByte9;
            public ulong WriteByte10;
            public ulong WriteByte11;
            public ulong WriteByte12;
            public ulong WriteByte13;
            public ulong WriteByte14;
            public ulong WriteByte15;

            public WriteCall() : this(false, false, 0x00, [])
            {
            }
            public WriteCall(bool active, bool frozen, long address, byte[] bytes)
            {
                Active = active;
                Frozen = frozen;
                Address = address;
                WriteByteCount = (bytes != null) ? bytes.Length : 0;
                WriteByte0 = 0;
                WriteByte1 = 0;
                WriteByte2 = 0;
                WriteByte3 = 0;
                WriteByte4 = 0;
                WriteByte5 = 0;
                WriteByte6 = 0;
                WriteByte7 = 0;
                WriteByte8 = 0;
                WriteByte9 = 0;
                WriteByte10 = 0;
                WriteByte11 = 0;
                WriteByte12 = 0;
                WriteByte13 = 0;
                WriteByte14 = 0;
                WriteByte15 = 0;

                if (bytes == null)
                {
                    // noop, bits is already initialized to 0
                }
                else if (bytes.Length < 0 || bytes.Length > MaxWriteLength)
                {
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                }
                else
                {
                    // TODO: this is an UGLY kludge to get arround pointers, and fixed size arrays
                    // requiring "unsafe", which I'm not sure will break BizHawk or not or cause
                    // other headaches. In c# 8/.net 2012 then [System.Runtime.CompilerServices.InlineArray()]
                    // attribute can be used to bypass.
                    for (int i = 0; i * 8 < bytes.Length; i++)
                    {
                        int j;
                        byte[] writeBytes = new byte[8];
                        for (j = 0; i * 8 + j < bytes.Length; j++)
                        {
                            writeBytes[j] = bytes[i * 8 + j];
                        }
                        for (; j < 8; j++)
                        {
                            writeBytes[j] = 0;
                        }
                        switch (i)
                        {
                            case 0:
                                WriteByte0 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 1:
                                WriteByte1 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 2:
                                WriteByte2 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 3:
                                WriteByte3 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 4:
                                WriteByte4 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 5:
                                WriteByte5 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 6:
                                WriteByte6 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 7:
                                WriteByte7 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 8:
                                WriteByte8 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 9:
                                WriteByte9 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 10:
                                WriteByte10 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 11:
                                WriteByte11 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 12:
                                WriteByte12 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 13:
                                WriteByte13 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 14:
                                WriteByte14 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            case 15:
                                WriteByte15 = BitConverter.ToUInt64(writeBytes, 0);
                                break;
                            default:
                                throw new Exception("unexpected byte.");
                        }
                    }
                }
            }

            public readonly byte[] GetBytes()
            {
                List<byte> bytes =
                [
                    .. BitConverter.GetBytes(WriteByte0),
                    .. BitConverter.GetBytes(WriteByte1),
                    .. BitConverter.GetBytes(WriteByte2),
                    .. BitConverter.GetBytes(WriteByte3),
                    .. BitConverter.GetBytes(WriteByte4),
                    .. BitConverter.GetBytes(WriteByte5),
                    .. BitConverter.GetBytes(WriteByte6),
                    .. BitConverter.GetBytes(WriteByte7),
                    .. BitConverter.GetBytes(WriteByte8),
                    .. BitConverter.GetBytes(WriteByte9),
                    .. BitConverter.GetBytes(WriteByte10),
                    .. BitConverter.GetBytes(WriteByte11),
                    .. BitConverter.GetBytes(WriteByte12),
                    .. BitConverter.GetBytes(WriteByte13),
                    .. BitConverter.GetBytes(WriteByte14),
                    .. BitConverter.GetBytes(WriteByte15),
                ];

                while (bytes.Count > WriteByteCount)
                {
                    bytes.RemoveAt(bytes.Count - 1);
                }
                return [.. bytes];
            }
        }
    }
}
