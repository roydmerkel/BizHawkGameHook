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
            
            return segments.Select(x => x.ToArray() ?? throw new NullReferenceException()).ToArray();
        }
    }
    internal static class BizHawkInterface
    {
        public class InstantReadEvents : Dictionary<EventAddress, IList<byte[]>>, ISerializable
        {
            public InstantReadEvents() : base()
            {
            }

            public static InstantReadEvents Deserialize(byte[] bytes)
            {
                InstantReadEvents ret = [];

                if (bytes == null)
                    throw new ArgumentNullException(nameof(bytes));
                else if (bytes.Length < sizeof(int))
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                else if (bytes.Where(b => b == 0).Count() != 3)
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                else
                {
                    byte[][] segments = bytes.Split([(byte)0]);
                    if (segments.Length != 3)
                        throw new ArgumentOutOfRangeException(nameof(bytes));
                    byte[] entryCountBytes = segments[0];
                    entryCountBytes = Convert.FromBase64String(Encoding.ASCII.GetString(entryCountBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(entryCountBytes);
                    int entryCount = BitConverter.ToInt32(entryCountBytes, 0);

                    byte[] bytesStringLengthBytes = segments[1];
                    bytesStringLengthBytes = Convert.FromBase64String(Encoding.ASCII.GetString(bytesStringLengthBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(bytesStringLengthBytes);
                    int bytesStringLength = BitConverter.ToInt32(bytesStringLengthBytes, 0);

                    byte[] bytesStringBytes = segments[2];
                    if (bytesStringLength != bytesStringBytes.Length)
                        throw new ArgumentOutOfRangeException(nameof(bytes));
                    bytesStringBytes = Convert.FromBase64String(Encoding.ASCII.GetString(bytesStringBytes));
                    string bytesStringString = Encoding.UTF8.GetString(bytesStringBytes);
                    List<string> bytesStringsList = [.. bytesStringString.Split('\0')];
                    for(int i = bytesStringsList.Count - 1; i >= 0; i--)
                        if (bytesStringsList[i] == null || String.IsNullOrEmpty(bytesStringsList[i]))
                            bytesStringsList.RemoveAt(i);
                    string[] bytesStrings = [.. bytesStringsList];
                    if (entryCount != bytesStrings.Length)
                        throw new ArgumentOutOfRangeException(nameof(bytes));

                    foreach (string bytesString in bytesStrings)
                    {
                        byte[] kvBytes = Convert.FromBase64String(Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(bytesString)));
                        if (kvBytes.Where(b => b == 0).Count() != 4)
                            throw new ArgumentOutOfRangeException(nameof(bytes));

                        byte[][] kvBytesBytes = kvBytes.Split([(byte)0]);

                        byte[] eventAddressBytes = Convert.FromBase64String(Encoding.ASCII.GetString(kvBytesBytes[0]));
                        EventAddress eventAddress = EventAddress.Deserialize(eventAddressBytes);

                        ret.Add(eventAddress, new List<byte[]>());

                        byte[] countBytesBytes = kvBytesBytes[1];
                        countBytesBytes = Convert.FromBase64String(Encoding.ASCII.GetString(countBytesBytes));
                        if (!BitConverter.IsLittleEndian)
                            Array.Reverse(countBytesBytes);
                        int countBytes = BitConverter.ToInt32(countBytesBytes, 0);

                        byte[] bytesBytesStringLengthBytes = kvBytesBytes[2];
                        bytesBytesStringLengthBytes = Convert.FromBase64String(Encoding.ASCII.GetString(bytesBytesStringLengthBytes));
                        if (!BitConverter.IsLittleEndian)
                            Array.Reverse(bytesBytesStringLengthBytes);
                        int bytesBytesStringLength = BitConverter.ToInt32(bytesBytesStringLengthBytes, 0);

                        byte[] bytesBytesStringsBytes = kvBytesBytes[3];
                        if (bytesBytesStringsBytes.Length != bytesBytesStringLength)
                            throw new ArgumentOutOfRangeException(nameof(bytes));
                        string bytesBytesStrings = Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.ASCII.GetString(bytesBytesStringsBytes)));
                        List<string> bytesBytesStringsListList = [.. bytesBytesStrings.Split('\0')];
                        for (int i = bytesBytesStringsListList.Count - 1; i >= 0; i--)
                            if (bytesBytesStringsListList[i] == null || String.IsNullOrEmpty(bytesBytesStringsListList[i]))
                                bytesBytesStringsListList.RemoveAt(i);
                        string[] bytesBytesStringsList = [.. bytesBytesStringsListList];

                        if (bytesBytesStringsList.Length != countBytes)
                            throw new ArgumentOutOfRangeException(nameof(bytes));

                        foreach (var bytesBytesString in bytesBytesStringsList)
                        {
                            byte[] bytesBytes = Convert.FromBase64String(Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(bytesBytesString)));
                            ret[eventAddress].Add(bytesBytes);
                        }
                    }

                    return ret;
                }
                throw new NotImplementedException();
            }

            public byte[] Serialize()
            {
                byte[] entryCountBytes = BitConverter.GetBytes(Keys.Count);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(entryCountBytes);
                entryCountBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(entryCountBytes));

                List<string> bytesStringList = [];
                foreach(EventAddress eventAddress in Keys)
                {
                    byte[] eventAddressBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(eventAddress.Serialize()));
                    byte[] countBytes = BitConverter.GetBytes(this[eventAddress].Count);
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(countBytes);
                    countBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(countBytes));

                    List<string> bytesBytesStringsList = [];
                    foreach (var bytesBytes in this[eventAddress])
                    {
                        bytesBytesStringsList.Add(Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(Convert.ToBase64String(bytesBytes))));
                    }
                    byte[] bytesBytesString = Encoding.UTF8.GetBytes(String.Join("\0", [.. bytesBytesStringsList]));
                    bytesBytesString = Encoding.ASCII.GetBytes(Convert.ToBase64String(bytesBytesString));

                    byte[] bytesBytesStringLength = BitConverter.GetBytes(bytesBytesString.Length);
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(bytesBytesStringLength);
                    bytesBytesStringLength = Encoding.ASCII.GetBytes(Convert.ToBase64String(bytesBytesStringLength));

                    byte[] bytes =
                    [
                        .. eventAddressBytes,
                        .. new byte[1] { 0 },
                        .. countBytes,
                        .. new byte[1] { 0 },
                        .. bytesBytesStringLength,
                        .. new byte[1] { 0 },
                        .. bytesBytesString,
                        .. new byte[1] { 0 },
                    ];

                    bytesStringList.Add(Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(Convert.ToBase64String(bytes))));
                }
                byte[] bytesString = Encoding.ASCII.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\0", [.. bytesStringList]))));

                byte[] bytesStringLength = BitConverter.GetBytes(bytesString.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(bytesStringLength);
                bytesStringLength = Encoding.ASCII.GetBytes(Convert.ToBase64String(bytesStringLength));

                byte[] retBytes = [.. entryCountBytes, .. new byte[1] { 0 }, .. bytesStringLength, .. new byte[1] { 0 }, .. bytesString, .. new byte[1] { 0 }];

                return retBytes;
            }
        }

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

        public class EventAddress(ulong serialNumber, string? name, bool active, long address, ushort bank, EventType eventType, IEnumerable<EventAddressRegisterOverride> eventAddressRegisterOverrides, string? bits, int length, int size, bool instantaneous) : ISerializable
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
            public ulong SerialNumber = serialNumber;

            public EventAddress() : this(0, null, false, 0x00, ushort.MaxValue, EventType.EventType_Undefined, new List<EventAddressRegisterOverride> { }, null, 1, 0, false)
            {
            }

            public byte[] Serialize()
            {
                byte[] serialNumberBytes = BitConverter.GetBytes(SerialNumber);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(serialNumberBytes);
                serialNumberBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(serialNumberBytes));
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
                    .. serialNumberBytes,
                    .. new byte[1] { 0 },
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
                else if (bytes.Where(b => b == 0).Count() != 12)
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                else
                {
                    byte[][] segments = bytes.Split([(byte)0]);
                    if (segments.Length != 12)
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
                        + segments[10].Length + 1
                        + segments[11].Length)
                        throw new ArgumentOutOfRangeException(nameof(bytes));

                    byte[] serialNumberBytes = segments[1];
                    byte[] activeBytes = segments[2];
                    byte[] addressBytes = segments[3];
                    byte[] bankBytes = segments[4];
                    byte[] eventTypeBytes = segments[5];
                    byte[] eventAddressRegisterOverridesBytes = segments[6];
                    byte[] bitsBytes = segments[7];
                    byte[] lengthBytes = segments[8];
                    byte[] sizeBytes = segments[9];
                    byte[] nameBytes = segments[10];
                    byte[] instantaneousBytes = segments[11];

                    serialNumberBytes = Convert.FromBase64String(Encoding.ASCII.GetString(serialNumberBytes));
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(serialNumberBytes);
                    ulong serialNumber = BitConverter.ToUInt64(serialNumberBytes, 0);

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

                    return new EventAddress(serialNumber, name, active, address, bank, eventType, eventAddressRegisterOverrides, bits, length, size, instantaneous);
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

        public class WriteCall(bool active, bool frozen, long address, byte[] bytes) : ISerializable
        {
            private readonly bool _active = active;
            private readonly bool _frozen = frozen;
            private readonly long _address = address;
            private readonly byte[] _writeByte = bytes;

            public WriteCall() : this(false, false, 0x00, [])
            {
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

                byte[] writeByteBytes = [];
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

                byte[] res =
                [
                    .. activeBytes,
                    .. new byte[1] { 0 },
                    .. frozenBytes,
                    .. new byte[1] { 0 },
                    .. addressBytes,
                    .. new byte[1] { 0 },
                    .. writeByteBytesLengthByte,
                    .. new byte[1] { 0 },
                    .. writeByteBytes,
                ];
                byte[] resLengthNonBase64 = BitConverter.GetBytes(res.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(resLengthNonBase64);
                byte[] resLength = Encoding.ASCII.GetBytes(Convert.ToBase64String(resLengthNonBase64));
                return [.. resLength, .. new byte[1] { 0 }, .. res, .. new byte[1] { 0 }];
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
    }
}
