using GameHook.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static GameHook.Infrastructure.BizHawkInterface;

namespace GameHook.Infrastructure
{
    internal static class BizHawkInterface
    {
        public class CircularArrayQueue<T> where T : struct
        {
            private readonly T[] _array;
            private int _front;
            private int _rear;
            private readonly int _capacity;

            public CircularArrayQueue(int capacity)
            {
                _array = new T[capacity];
                _front = -1;
                _rear = -1;
                _capacity = capacity;
            }
            public CircularArrayQueue(T[] array, int startIdx, int endIdx)
            {
                _array = array;
                _front = startIdx;
                _rear = endIdx;
                _capacity = array.Length;
            }

            public bool Enqueue(T value)
            {
                if ((_rear + 1) % _capacity == _front)
                {
                    return false;
                }
                else if (_front == -1)
                {
                    _front = 0;
                    _rear = 0;
                    _array[_rear] = value;
                    return true;
                }
                else
                {
                    _rear = (_rear + 1) % _capacity;
                    _array[_rear] = value;
                    return true;
                }
            }

            public T? Dequeue()
            {
                if (_front == -1)
                {
                    return null;
                }
                else
                {
                    T ret = _array[_front];
                    if (_front == _rear)
                    {
                        _front = -1;
                        _rear = -1;
                    }
                    else
                    {
                        _front = (_front + 1) % _capacity;
                    }
                    return ret;
                }
            }

            public int Front { get { return _front; } }
            public int Rear { get { return _rear; } }
            public T[] Array { get { return _array; } }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct EventAddressRegisterOverride
        {
            public ulong Register; // actually an 8 byte ascii string
            public ulong Value;

            public EventAddressRegisterOverride() : this("", 0x00)
            {
            }
            public EventAddressRegisterOverride(string registerName, ulong value)
            {
                if (registerName == null)
                {
                    throw new ArgumentNullException(nameof(registerName));
                }
                // string needs to be same size as Register (ulong)
                //
                // TODO: a fixed size char array would work better, but I would require
                // adding unsafe and I'm not sure if this would break BizHawk or cause
                // build headaches.
                else if (registerName.Length < 0 || registerName.Length > sizeof(ulong) - 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(registerName));
                }
                // encode as ASCII, don't need custom check as c# throws an exception for
                // trying to encode non-ascii strings as ascii.
                else
                {
                    List<byte> bytes = new(Encoding.ASCII.GetBytes(registerName));
                    bytes ??= [];
                    while (bytes.Count < sizeof(ulong))
                    {
                        bytes.Add(0);
                    }
                    Register = BitConverter.ToUInt64([.. bytes], 0);
                }
                Value = value;
            }

            public readonly string GetRegisterString()
            {
                byte[] bytes = BitConverter.GetBytes(Register);
                string register = Encoding.ASCII.GetString(bytes);
                register = register.TrimEnd('\0');

                return register;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct EventAddress
        {
            const int MaxRegisterOverride = 6;
            const int MaxBitsLength = 64 * 8 / 8;
            const int MaxNameLength = 64 * 16 / 8;

            public bool Active;
            public long Address;
            public ushort Bank;
            public EventType EventType;
            public EventAddressRegisterOverride EventAddressRegisterOverride0;
            public EventAddressRegisterOverride EventAddressRegisterOverride1;
            public EventAddressRegisterOverride EventAddressRegisterOverride2;
            public EventAddressRegisterOverride EventAddressRegisterOverride3;
            public EventAddressRegisterOverride EventAddressRegisterOverride4;
            public EventAddressRegisterOverride EventAddressRegisterOverride5;
            public ulong Bits0;
            public ulong Bits1;
            public ulong Bits2;
            public ulong Bits3;
            public ulong Bits4;
            public ulong Bits5;
            public ulong Bits6;
            public ulong Bits7;
            public int Length;
            public int Size;
            public ulong Name0;
            public ulong Name1;
            public ulong Name2;
            public ulong Name3;
            public ulong Name4;
            public ulong Name5;
            public ulong Name6;
            public ulong Name7;
            public ulong Name8;
            public ulong Name9;
            public ulong Name10;
            public ulong Name11;
            public ulong Name12;
            public ulong Name13;
            public ulong Name14;
            public ulong Name15;
            public bool Instantaneous;

            public EventAddress() : this(null, false, 0x00, ushort.MaxValue, EventType.EventType_Undefined, new List<EventAddressRegisterOverride> { }, null, 1, 0, false)
            {
            }
            public EventAddress(string? name, bool active, long address, ushort bank, EventType eventType, IEnumerable<EventAddressRegisterOverride> eventAddressRegisterOverrides, string? bits, int length, int size, bool instantaneous)
            {
                Name0 = 0;
                Name1 = 0;
                Name2 = 0;
                Name3 = 0;
                Name4 = 0;
                Name5 = 0;
                Name6 = 0;
                Name7 = 0;
                Name8 = 0;
                Name9 = 0;
                Name10 = 0;
                Name11 = 0;
                Name12 = 0;
                Name13 = 0;
                Name14 = 0;
                Name15 = 0;
                EventAddressRegisterOverride0 = new EventAddressRegisterOverride();
                EventAddressRegisterOverride1 = new EventAddressRegisterOverride();
                EventAddressRegisterOverride2 = new EventAddressRegisterOverride();
                EventAddressRegisterOverride3 = new EventAddressRegisterOverride();
                EventAddressRegisterOverride4 = new EventAddressRegisterOverride();
                EventAddressRegisterOverride5 = new EventAddressRegisterOverride();
                Bits0 = 0;
                Bits1 = 0;
                Bits2 = 0;
                Bits3 = 0;
                Bits4 = 0;
                Bits5 = 0;
                Bits6 = 0;
                Bits7 = 0;
                Length = length;
                Size = (size > 0) ? size : 1;
                Instantaneous = instantaneous;

                if (name == null)
                {
                    // noop, bits is already initialized to 0
                }
                else if (name.Length < 0 || name.Length > MaxNameLength)
                {
                    throw new ArgumentOutOfRangeException(nameof(name));
                }
                else
                {
                    // TODO: this is an UGLY kludge to get arround pointers, and fixed size arrays
                    // requiring "unsafe", which I'm not sure will break BizHawk or not or cause
                    // other headaches. In c# 8/.net 2012 then [System.Runtime.CompilerServices.InlineArray()]
                    // attribute can be used to bypass.
                    List<byte> nameBytes = new(Encoding.UTF8.GetBytes(name));
                    nameBytes ??= [];
                    while (nameBytes.Count < MaxNameLength)
                    {
                        nameBytes.Add(0);
                    }
                    byte[] nameBytsArray = [.. nameBytes];
                    Name0 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 0).Take(sizeof(ulong)).ToArray(), 0);
                    Name1 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 1).Take(sizeof(ulong)).ToArray(), 0);
                    Name2 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 2).Take(sizeof(ulong)).ToArray(), 0);
                    Name3 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 3).Take(sizeof(ulong)).ToArray(), 0);
                    Name4 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 4).Take(sizeof(ulong)).ToArray(), 0);
                    Name5 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 5).Take(sizeof(ulong)).ToArray(), 0);
                    Name6 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 6).Take(sizeof(ulong)).ToArray(), 0);
                    Name7 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 7).Take(sizeof(ulong)).ToArray(), 0);
                    Name8 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 8).Take(sizeof(ulong)).ToArray(), 0);
                    Name9 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 9).Take(sizeof(ulong)).ToArray(), 0);
                    Name10 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 10).Take(sizeof(ulong)).ToArray(), 0);
                    Name11 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 11).Take(sizeof(ulong)).ToArray(), 0);
                    Name12 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 12).Take(sizeof(ulong)).ToArray(), 0);
                    Name13 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 13).Take(sizeof(ulong)).ToArray(), 0);
                    Name14 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 14).Take(sizeof(ulong)).ToArray(), 0);
                    Name15 = BitConverter.ToUInt64(nameBytsArray.Skip(sizeof(ulong) * 15).Take(sizeof(ulong)).ToArray(), 0);
                }
                Active = active;
                Address = address;
                Bank = bank;
                EventType = eventType;
                if (eventAddressRegisterOverrides == null)
                {
                    throw new ArgumentNullException(nameof(eventAddressRegisterOverrides));
                }
                else if (eventAddressRegisterOverrides.Count() < 0 || eventAddressRegisterOverrides.Count() > MaxRegisterOverride)
                {
                    throw new ArgumentOutOfRangeException(nameof(eventAddressRegisterOverrides));
                }
                else
                {
                    int idx = 0;
                    // TODO: this is an UGLY kludge to get arround pointers, and fixed size arrays
                    // requiring "unsafe", which I'm not sure will break BizHawk or not or cause
                    // other headaches. In c# 8/.net 2012 then [System.Runtime.CompilerServices.InlineArray()]
                    // attribute can be used to bypass.
                    foreach (EventAddressRegisterOverride eventAddressRegisterOverride in eventAddressRegisterOverrides)
                    {
                        switch (idx)
                        {
                            case 0:
                                EventAddressRegisterOverride0 = eventAddressRegisterOverride;
                                break;
                            case 1:
                                EventAddressRegisterOverride1 = eventAddressRegisterOverride;
                                break;
                            case 2:
                                EventAddressRegisterOverride2 = eventAddressRegisterOverride;
                                break;
                            case 3:
                                EventAddressRegisterOverride3 = eventAddressRegisterOverride;
                                break;
                            case 4:
                                EventAddressRegisterOverride4 = eventAddressRegisterOverride;
                                break;
                            case 5:
                                EventAddressRegisterOverride5 = eventAddressRegisterOverride;
                                break;
                            default:
                                throw new Exception("unexpected index");
                        }
                        idx++;
                    }
                }
                if (bits == null)
                {
                    // noop, bits is already initialized to 0
                }
                else if (bits.Length < 0 || bits.Length > MaxBitsLength)
                {
                    throw new ArgumentOutOfRangeException(nameof(bits));
                }
                else
                {
                    // TODO: this is an UGLY kludge to get arround pointers, and fixed size arrays
                    // requiring "unsafe", which I'm not sure will break BizHawk or not or cause
                    // other headaches. In c# 8/.net 2012 then [System.Runtime.CompilerServices.InlineArray()]
                    // attribute can be used to bypass.
                    List<byte> bitsBytes = new(Encoding.ASCII.GetBytes(bits));
                    bitsBytes ??= [];
                    while (bitsBytes.Count < MaxBitsLength)
                    {
                        bitsBytes.Add(0);
                    }
                    byte[] bitsBytsArray = [.. bitsBytes];
                    Bits0 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(ulong) * 0).Take(sizeof(ulong)).ToArray(), 0);
                    Bits1 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(ulong) * 1).Take(sizeof(ulong)).ToArray(), 0);
                    Bits2 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(ulong) * 2).Take(sizeof(ulong)).ToArray(), 0);
                    Bits3 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(ulong) * 3).Take(sizeof(ulong)).ToArray(), 0);
                    Bits4 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(ulong) * 4).Take(sizeof(ulong)).ToArray(), 0);
                    Bits5 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(ulong) * 5).Take(sizeof(ulong)).ToArray(), 0);
                    Bits6 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(ulong) * 6).Take(sizeof(ulong)).ToArray(), 0);
                    Bits7 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(ulong) * 7).Take(sizeof(ulong)).ToArray(), 0);
                }
            }

            public readonly EventAddressRegisterOverride[] GetOverrides()
            {
                List<EventAddressRegisterOverride> overrides = [];
                foreach (var i in new List<EventAddressRegisterOverride> {EventAddressRegisterOverride0,
                        EventAddressRegisterOverride1,
                        EventAddressRegisterOverride2,
                        EventAddressRegisterOverride3,
                        EventAddressRegisterOverride4,
                        EventAddressRegisterOverride5})
                {
                    if ((i.Register & 0xFFUL) != 0)
                    {
                        overrides.Add(i);
                    }
                }

                return [.. overrides];
            }

            public readonly string GetBitsString()
            {
                IEnumerable<byte> bytes = BitConverter.GetBytes(Bits0).ToList();
                bytes = bytes.Concat(BitConverter.GetBytes(Bits1).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Bits2).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Bits3).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Bits4).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Bits5).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Bits6).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Bits7).ToList());

                string bits = Encoding.ASCII.GetString(bytes.ToArray());
                bits = bits.TrimEnd('\0');

                return bits;
            }

            public readonly string GetNameString()
            {
                IEnumerable<byte> bytes = BitConverter.GetBytes(Name0).ToList();
                bytes = bytes.Concat(BitConverter.GetBytes(Name1).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name2).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name3).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name4).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name5).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name6).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name7).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name8).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name9).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name10).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name11).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name12).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name13).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name14).ToList());
                bytes = bytes.Concat(BitConverter.GetBytes(Name15).ToList());

                string name = Encoding.UTF8.GetString(bytes.ToArray());
                name = name.TrimEnd('\0');

                return name;
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
