using GameHook.Domain;
using GameHook.Domain.Interfaces;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

#pragma warning disable CA1416 // Validate platform compatibility
namespace GameHook.Infrastructure.Drivers
{
    public class BizhawkMemoryMapDriver : IGameHookDriver, IBizhawkMemoryMapDriver
    {
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
                    List<byte> bytes = new List<byte>(Encoding.ASCII.GetBytes(registerName));
                    if (bytes == null)
                    {
                        bytes = new List<byte> { };
                    }
                    while (bytes.Count < sizeof(ulong))
                    {
                        bytes.Add(0);
                    }
                    Register = BitConverter.ToUInt64(bytes.ToArray(), 0);
                }
                Value = value;
            }

            public string getRegisterString()
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
            public UInt64 Bits0;
            public UInt64 Bits1;
            public UInt64 Bits2;
            public UInt64 Bits3;
            public UInt64 Bits4;
            public UInt64 Bits5;
            public UInt64 Bits6;
            public UInt64 Bits7;
            public int Length;
            public int Size;

            public EventAddress() : this(false, 0x00, ushort.MaxValue, EventType.EventType_Undefined, new List<EventAddressRegisterOverride> { }, null, 1, 0)
            {
            }
            public EventAddress(bool active, long address, ushort bank, EventType eventType, IEnumerable<EventAddressRegisterOverride> eventAddressRegisterOverrides, string? bits, int length, int size)
            {
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
                else if (bits.Count() < 0 || bits.Count() > MaxBitsLength)
                {
                    throw new ArgumentOutOfRangeException(nameof(bits));
                }
                else
                {
                    // TODO: this is an UGLY kludge to get arround pointers, and fixed size arrays
                    // requiring "unsafe", which I'm not sure will break BizHawk or not or cause
                    // other headaches. In c# 8/.net 2012 then [System.Runtime.CompilerServices.InlineArray()]
                    // attribute can be used to bypass.
                    List<byte> bitsBytes = new List<byte>(Encoding.ASCII.GetBytes(bits));
                    if (bitsBytes == null)
                    {
                        bitsBytes = new List<byte> { };
                    }
                    while (bitsBytes.Count < MaxBitsLength)
                    {
                        bitsBytes.Add(0);
                    }
                    byte[] bitsBytsArray = bitsBytes.ToArray();
                    Bits0 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 0).Take(sizeof(UInt64)).ToArray(), 0);
                    Bits1 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 1).Take(sizeof(UInt64)).ToArray(), 0);
                    Bits2 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 2).Take(sizeof(UInt64)).ToArray(), 0);
                    Bits3 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 3).Take(sizeof(UInt64)).ToArray(), 0);
                    Bits4 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 4).Take(sizeof(UInt64)).ToArray(), 0);
                    Bits5 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 5).Take(sizeof(UInt64)).ToArray(), 0);
                    Bits6 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 6).Take(sizeof(UInt64)).ToArray(), 0);
                    Bits7 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 7).Take(sizeof(UInt64)).ToArray(), 0);
                }
            }

            public EventAddressRegisterOverride[] getOverrides()
            {
                List<EventAddressRegisterOverride> overrides = new List<EventAddressRegisterOverride>();
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

                return overrides.ToArray();
            }

            public string getBitsString()
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
        }

        public string ProperName => "Bizhawk";
        public int DelayMsBetweenReads { get; }

        private int IntegrationVersion;
        private string SystemName = string.Empty;

        private const int METADATA_LENGTH = 32;
        private const int DATA_Length = 4 * 1024 * 1024;

        public BizhawkMemoryMapDriver(AppSettings appSettings)
        {
            DelayMsBetweenReads = appSettings.BIZHAWK_DELAY_MS_BETWEEN_READS;
        }

        string GetStringFromBytes(byte[] data)
        {
            return Encoding.UTF8.GetString(data).TrimEnd('\0');
        }

        byte[] GetFromMemoryMappedFile(string filename, int fileSize)
        {
            try
            {
                using var mmfData = MemoryMappedFile.OpenExisting(filename, MemoryMappedFileRights.Read);
                using var mmfAccessor = mmfData.CreateViewAccessor(0, fileSize, MemoryMappedFileAccess.Read);

                byte[] data = new byte[fileSize];
                mmfAccessor.ReadArray(0, data, 0, fileSize);

                return data;
            }
            catch (FileNotFoundException ex)
            {
                throw new VisibleException("Can't establish a communication with BizHawk. Is Bizhawk open? Is the GameHook integration tool running?", ex);
            }
            catch
            {
                throw;
            }
        }

        public Task EstablishConnection()
        {
            var metadata = GetFromMemoryMappedFile("GAMEHOOK_BIZHAWK.bin", METADATA_LENGTH);

            IntegrationVersion = metadata[1];

            if (IntegrationVersion != SharedPlatformConstants.BIZHAWK_INTEGRATION_VERSION)
            {
                throw new VisibleException("BizHawk's Game Hook integration is out of date! Please update it.");
            }

            SystemName = GetStringFromBytes(metadata[2..31]);

            if (string.IsNullOrEmpty(SystemName))
            {
                throw new VisibleException("BizHawk connection is established, but does not have a game running.");
            }

            return Task.CompletedTask;
        }

        public Task<Dictionary<uint, byte[]>> ReadBytes(IEnumerable<MemoryAddressBlock> blocks)
        {
            var platform = SharedPlatformConstants.Information.SingleOrDefault(x => x.BizhawkIdentifier == SystemName) ?? throw new Exception($"System {SystemName} is not yet supported.");

            var data = GetFromMemoryMappedFile("GAMEHOOK_BIZHAWK_DATA.bin", DATA_Length);

            return Task.FromResult(platform.MemoryLayout.ToDictionary(
                x => x.PhysicalStartingAddress,
                x => data[x.CustomPacketTransmitPosition..(x.CustomPacketTransmitPosition + x.Length)]
            ));
        }

        public Task WriteBytes(uint startingMemoryAddress, byte[] values)
        {
            throw new NotImplementedException();
        }

        public Task ClearEvents()
        {
            try
            {
                using (Semaphore eventsSemaphore = Semaphore.OpenExisting(name: "GAMEHOOK_BIZHAWK_EVENTS.semaphore"))
                {
                    eventsSemaphore.WaitOne();

                    try
                    {
                        int eventElementSize = Marshal.SizeOf(typeof(EventAddress));
                        long memoryMappedSize = 1 + eventElementSize * SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE;
                        const int eventsLookupSize = SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE;
                        EventAddress[] resetEventsLookup = new EventAddress[eventsLookupSize];
                        using var mmfData = MemoryMappedFile.OpenExisting("GAMEHOOK_BIZHAWK_EVENTS_LOOKUPS.bin", MemoryMappedFileRights.Write);
                        using var mmfAccessor = mmfData.CreateViewAccessor(0, memoryMappedSize, MemoryMappedFileAccess.Write);
                        EventAddress template = new EventAddress();
                        Array.Fill(resetEventsLookup, template, 0, resetEventsLookup.Length);
                        mmfAccessor.Write(0, (byte)1);
                        mmfAccessor.WriteArray(1, resetEventsLookup, 0, resetEventsLookup.Length);
                    }
                    catch (FileNotFoundException ex)
                    {
                        throw new VisibleException("Can't establish a communication with BizHawk. Is Bizhawk open? Is the GameHook integration tool running?", ex);
                    }
                    catch
                    {
                        throw;
                    }
                    finally
                    {
                        eventsSemaphore.Release();
                    }
                }

                return Task.CompletedTask;
            }
            catch (IOException ex)
            {
                throw new VisibleException("Can't establish a communication with BizHawk. Is Bizhawk open? Is the GameHook integration tool running?", ex);
            }
            catch
            {
                throw;
            }
        }

        public Task AddEvent(long address, ushort bank, EventType eventType, EventRegisterOverride[] eventRegisterOverrides, string? bits, int length, int size)
        {
            try
            {
                using (Semaphore eventsSemaphore = Semaphore.OpenExisting(name: "GAMEHOOK_BIZHAWK_EVENTS.semaphore"))
                {
                    eventsSemaphore.WaitOne();

                    try
                    {
                        int eventElementSize = Marshal.SizeOf(typeof(EventAddress));
                        long memoryMappedSize = 1 + eventElementSize * SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE;
                        const int eventsLookupSize = SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE;
                        using var mmfData = MemoryMappedFile.OpenExisting("GAMEHOOK_BIZHAWK_EVENTS_LOOKUPS.bin", MemoryMappedFileRights.ReadWrite);
                        using var mmfAccessor = mmfData.CreateViewAccessor(0, memoryMappedSize, MemoryMappedFileAccess.ReadWrite);
                        EventAddress[] currentEventsLookup = new EventAddress[eventsLookupSize];
                        mmfAccessor.ReadArray(1, currentEventsLookup, 0, currentEventsLookup.Length);
                        List<EventAddress> currentEvents = currentEventsLookup.Where(x => x.Active).ToList();
                        if(currentEvents.Count() >= eventsLookupSize)
                        {
                            throw new Exception("Too many events, please contact devs to get more added, or remove some events or instant properties.");
                        }
                        else
                        {
                            IEnumerable<EventAddressRegisterOverride> eventAddressRegisterOverrides;
                            if (eventRegisterOverrides != null && eventRegisterOverrides.Length > 0)
                            {
                                eventAddressRegisterOverrides = eventRegisterOverrides.Select(x =>
                                {
                                    if (x != null)
                                    {
                                        if (x.Value != null && x.Register != null)
                                        {
                                            string? valueString = x.Value.ToString();
                                            if (valueString != null)
                                            {
                                                return new EventAddressRegisterOverride(x.Register, ulong.Parse(valueString));
                                            }
                                            else
                                            {
                                                throw new NullReferenceException();
                                            }
                                        }
                                        else
                                        {
                                            throw new NullReferenceException();
                                        }
                                    }
                                    else
                                    {
                                        throw new NullReferenceException();
                                    }
                                });
                            }
                            else
                            {
                                eventAddressRegisterOverrides = new List<EventAddressRegisterOverride>();
                            }

                            EventAddress newAddress = new EventAddress(true, address, bank, eventType, eventAddressRegisterOverrides.ToArray(), bits, length, size);
                            currentEvents.Add(newAddress);

                            if(currentEvents.Count() < eventsLookupSize)
                            {
                                EventAddress template = new EventAddress();

                                while(currentEvents.Count() < eventsLookupSize)
                                {
                                    currentEvents.Add(template);
                                }
                            }
                        }
                        mmfAccessor.Write(0, (byte)1);
                        mmfAccessor.WriteArray(1, currentEvents.ToArray(), 0, currentEvents.Count());
                    }
                    catch (FileNotFoundException ex)
                    {
                        throw new VisibleException("Can't establish a communication with BizHawk. Is Bizhawk open? Is the GameHook integration tool running?", ex);
                    }
                    catch
                    {
                        throw;
                    }
                    finally
                    {
                        eventsSemaphore.Release();
                    }
                }

                return Task.CompletedTask;
            }
            catch (IOException ex)
            {
                throw new VisibleException("Can't establish a communication with BizHawk. Is Bizhawk open? Is the GameHook integration tool running?", ex);
            }
            catch
            {
                throw;
            }
        }

        public Task RemoveEvent(long address, ushort bank, EventType eventType)
        {
            try
            {
                using (Semaphore eventsSemaphore = Semaphore.OpenExisting(name: "GAMEHOOK_BIZHAWK_EVENTS.semaphore"))
                {
                    eventsSemaphore.WaitOne();

                    try
                    {
                        int eventElementSize = Marshal.SizeOf(typeof(EventAddress));
                        long memoryMappedSize = 1 + eventElementSize * SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE;
                        const int eventsLookupSize = SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE;
                        using var mmfData = MemoryMappedFile.OpenExisting("GAMEHOOK_BIZHAWK_EVENTS_LOOKUPS.bin", MemoryMappedFileRights.ReadWrite);
                        using var mmfAccessor = mmfData.CreateViewAccessor(0, memoryMappedSize, MemoryMappedFileAccess.ReadWrite);
                        EventAddress[] currentEventsLookup = new EventAddress[eventsLookupSize];
                        mmfAccessor.ReadArray(1, currentEventsLookup, 0, currentEventsLookup.Length);
                        List<EventAddress> currentEvents = currentEventsLookup.Where(x => x.Active && (x.Address != address || x.EventType != eventType || x.Bank != bank)).ToList();

                        if (currentEvents.Count() < eventsLookupSize)
                        {
                            EventAddress template = new EventAddress();

                            while (currentEvents.Count() < eventsLookupSize)
                            {
                                currentEvents.Add(template);
                            }
                        }

                        mmfAccessor.Write(0, (byte)1);
                        mmfAccessor.WriteArray(1, currentEvents.ToArray(), 0, currentEvents.Count());
                    }
                    catch (FileNotFoundException ex)
                    {
                        throw new VisibleException("Can't establish a communication with BizHawk. Is Bizhawk open? Is the GameHook integration tool running?", ex);
                    }
                    catch
                    {
                        throw;
                    }
                    finally
                    {
                        eventsSemaphore.Release();
                    }
                }

                return Task.CompletedTask;
            }
            catch (IOException ex)
            {
                throw new VisibleException("Can't establish a communication with BizHawk. Is Bizhawk open? Is the GameHook integration tool running?", ex);
            }
            catch
            {
                throw;
            }
        }
    }
}
#pragma warning restore CA1416 // Validate platform compatibility