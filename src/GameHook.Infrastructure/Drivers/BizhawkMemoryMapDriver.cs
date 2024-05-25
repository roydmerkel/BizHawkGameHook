using GameHook.Domain;
using GameHook.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using static GameHook.Infrastructure.BizHawkInterface;
using static System.Net.Mime.MediaTypeNames;

#pragma warning disable CA1416 // Validate platform compatibility
namespace GameHook.Infrastructure.Drivers
{
    public class BizhawkMemoryMapDriver : IGameHookDriver, IBizhawkMemoryMapDriver
    {
        public string ProperName => "Bizhawk";
        public int DelayMsBetweenReads { get; }

        private int IntegrationVersion;
        private string SystemName = string.Empty;

        private const int METADATA_LENGTH = 32;
        private const int DATA_Length = 4 * 1024 * 1024;

        private readonly AppSettings _appSettings;

        private MemoryMappedFile? _metadataMemoryMappedFile = null;
        private MemoryMappedViewAccessor? _metadataAccessor = null;

        private MemoryMappedFile? _dataMemoryMappedFile = null;
        private MemoryMappedViewAccessor? _dataAccessor = null;

        private MemoryMappedFile? _eventsLookupMemoryMappedFile = null;
        private MemoryMappedViewAccessor? _eventsLookupAccessor = null;
        private int _eventsLookupElementSize = 0;
        private Semaphore? _eventsSemaphore = null;

        private MemoryMappedFile? _writeCallMemoryMappedFile = null;
        private MemoryMappedViewAccessor? _writeCallAccessor = null;
        private int _writeCallElementSize = 0;
        private Semaphore? _writeCallSemaphore = null;

        const int eventsLookupSize = SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE;
        EventAddress[] eventsLookup = [];
        Dictionary<long, int[]> addressEventsLookup = new();

        public BizhawkMemoryMapDriver(AppSettings appSettings)
        {
            _appSettings = appSettings;
            DelayMsBetweenReads = _appSettings.BIZHAWK_DELAY_MS_BETWEEN_READS;
        }

        static string GetStringFromBytes(byte[] data)
        {
            return Encoding.UTF8.GetString(data).TrimEnd('\0');
        }

        public Task EstablishConnection()
        {
            byte[] metadata = new byte[METADATA_LENGTH];

            if (_metadataAccessor != null)
            {
                _metadataAccessor.Dispose();
                _metadataAccessor = null;
            }

            if (_metadataMemoryMappedFile != null)
            {
                _metadataMemoryMappedFile.Dispose();
                _metadataMemoryMappedFile = null;
            }

            if (_dataAccessor != null)
            {
                _dataAccessor.Dispose();
                _dataAccessor = null;
            }

            if (_dataMemoryMappedFile != null)
            {
                _dataMemoryMappedFile.Dispose();
                _dataMemoryMappedFile = null;
            }

            if (_eventsSemaphore != null)
            {
                _eventsSemaphore.Close();
                _eventsSemaphore.Dispose();
                _eventsSemaphore = null;
            }

            if (_eventsLookupAccessor != null)
            {
                _eventsLookupAccessor.Dispose();
                _eventsLookupAccessor = null;
            }

            if (_eventsLookupMemoryMappedFile != null)
            {
                _eventsLookupMemoryMappedFile.Dispose();
                _eventsLookupMemoryMappedFile = null;
            }

            if (_writeCallSemaphore != null)
            {
                _writeCallSemaphore.Close();
                _writeCallSemaphore.Dispose();
                _writeCallSemaphore = null;
            }

            if (_writeCallAccessor != null)
            {
                _writeCallAccessor.Dispose();
                _writeCallAccessor = null;
            }

            if (_writeCallMemoryMappedFile != null)
            {
                _writeCallMemoryMappedFile.Dispose();
                _writeCallMemoryMappedFile = null;
            }

            eventsLookup = new EventAddress[eventsLookupSize];
            EventAddress template = new();
            Array.Fill(eventsLookup, template);

            try
            {
                _metadataMemoryMappedFile = MemoryMappedFile.OpenExisting("GAMEHOOK_BIZHAWK.bin", MemoryMappedFileRights.Read);
                _metadataAccessor = _metadataMemoryMappedFile.CreateViewAccessor(0, METADATA_LENGTH, MemoryMappedFileAccess.Read);

                _metadataAccessor.ReadArray(0, metadata, 0, METADATA_LENGTH);
            }
            catch (FileNotFoundException ex)
            {
                throw new VisibleException("Can't establish a communication with BizHawk. Is Bizhawk open? Is the GameHook integration tool running?", ex);
            }
            catch
            {
                throw;
            }

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

            try
            {
                _dataMemoryMappedFile = MemoryMappedFile.OpenExisting("GAMEHOOK_BIZHAWK_DATA.bin", MemoryMappedFileRights.Read);
                _dataAccessor = _dataMemoryMappedFile.CreateViewAccessor(0, DATA_Length, MemoryMappedFileAccess.Read);
            }
            catch (FileNotFoundException ex)
            {
                throw new VisibleException("Can't establish a communication with BizHawk. Is Bizhawk open? Is the GameHook integration tool running?", ex);
            }
            catch
            {
                throw;
            }

            try
            {
                int eventElementSize = Marshal.SizeOf(typeof(EventAddress));
                _eventsLookupElementSize = 1 + eventElementSize * SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE;
                _eventsLookupMemoryMappedFile = MemoryMappedFile.OpenExisting("GAMEHOOK_BIZHAWK_EVENTS_LOOKUPS.bin", MemoryMappedFileRights.Write);
                _eventsLookupAccessor = _eventsLookupMemoryMappedFile.CreateViewAccessor(0, _eventsLookupElementSize, MemoryMappedFileAccess.Write);

                _eventsSemaphore = Semaphore.OpenExisting(name: "GAMEHOOK_BIZHAWK_WRITE_CALLS.semaphore");
            }
            catch (FileNotFoundException ex)
            {
                throw new VisibleException("Can't establish a communication with BizHawk. Is Bizhawk open? Is the GameHook integration tool running?", ex);
            }
            catch
            {
                throw;
            }

            try
            {
                int writeCallEventElementSize = Marshal.SizeOf(typeof(WriteCall));
                _writeCallElementSize = sizeof(int) + sizeof(int) + writeCallEventElementSize * SharedPlatformConstants.BIZHAWK_MAX_WRITE_CALLS_SIZE; // front, rear, array.
                _writeCallMemoryMappedFile = MemoryMappedFile.OpenExisting("GAMEHOOK_BIZHAWK_WRITE_CALLS.bin", MemoryMappedFileRights.ReadWrite);
                _writeCallAccessor = _writeCallMemoryMappedFile.CreateViewAccessor(0, _writeCallElementSize, MemoryMappedFileAccess.ReadWrite);

                _writeCallSemaphore = Semaphore.OpenExisting(name: "GAMEHOOK_BIZHAWK_WRITE_CALLS.semaphore");
            }
            catch (FileNotFoundException ex)
            {
                throw new VisibleException("Can't establish a communication with BizHawk. Is Bizhawk open? Is the GameHook integration tool running?", ex);
            }
            catch
            {
                throw;
            }

            return Task.CompletedTask;
        }

        public Task<Dictionary<uint, byte[]>> ReadBytes(IEnumerable<MemoryAddressBlock> blocks)
        {
            var platform = SharedPlatformConstants.Information.SingleOrDefault(x => x.BizhawkIdentifier == SystemName) ?? throw new Exception($"System {SystemName} is not yet supported.");

            byte[] data = new byte[DATA_Length];
            try
            {
                _dataAccessor!.ReadArray(0, data, 0, DATA_Length);
            }
            catch (FileNotFoundException ex)
            {
                throw new VisibleException("Can't establish a communication with BizHawk. Is Bizhawk open? Is the GameHook integration tool running?", ex);
            }
            catch
            {
                throw;
            }

            return Task.FromResult(platform.MemoryLayout.ToDictionary(
                x => x.PhysicalStartingAddress,
                x => data[x.CustomPacketTransmitPosition..(x.CustomPacketTransmitPosition + x.Length)]
            ));
        }

        public Task WriteBytes(uint startingMemoryAddress, byte[] values)
        {
            try
            {
                _writeCallSemaphore!.WaitOne();

                try
                {
                    const int writeCallsLookupSize = SharedPlatformConstants.BIZHAWK_MAX_WRITE_CALLS_SIZE;
                    WriteCall[] writeCalls = new WriteCall[writeCallsLookupSize];

                    // read in the current queue state.
                    _writeCallAccessor!.Read(0, out int front);
                    _writeCallAccessor.Read(sizeof(int), out int rear);
                    _writeCallAccessor.ReadArray(sizeof(int) * 2, writeCalls, 0, writeCallsLookupSize);

                    int[] eventIdx = (addressEventsLookup != null) ? (addressEventsLookup.ContainsKey(startingMemoryAddress) ? addressEventsLookup[startingMemoryAddress] : []) : [];
                    bool frozen = eventIdx.Any(x => eventsLookup[x].Instantaneous);

                    // create the queue and enqueue the write
                    CircularArrayQueue <WriteCall> queue = new(writeCalls, front, rear);
                    WriteCall writeCall = new(true, frozen, startingMemoryAddress, values);
                    if (!queue.Enqueue(writeCall))
                    {
                        throw new Exception("write data queue full, too many write events sent too closely together.");
                    }

                    // write the data back.
                    _writeCallAccessor.Write(0, queue.Front);
                    _writeCallAccessor.Write(sizeof(int), queue.Rear);
                    writeCalls = queue.Array;
                    _writeCallAccessor.WriteArray(sizeof(int) * 2, writeCalls, 0, writeCallsLookupSize);
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
                    //eventsSemaphore.Release();
                    _writeCallSemaphore.Release();
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

        public Task ClearEvents()
        {
            try
            {
                _eventsSemaphore!.WaitOne();

                try
                {
                    EventAddress template = new();
                    Array.Fill(eventsLookup, template, 0, eventsLookup.Length);
                    addressEventsLookup = new();
                    _eventsLookupAccessor!.Write(0, (byte)1);
                    _eventsLookupAccessor.WriteArray(1, eventsLookup, 0, eventsLookup.Length);
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
                    _eventsSemaphore.Release();
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

        public Task AddEvent(string? name, long address, ushort bank, EventType eventType, EventRegisterOverride[] eventRegisterOverrides, string? bits, int length, int size, bool instantaneous)
        {
            try
            {
                _eventsSemaphore!.WaitOne();

                try
                {
                    List<EventAddress> currentEvents = eventsLookup.Where(x => x.Active).ToList();
                    if(currentEvents.Count >= eventsLookupSize)
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

                        EventAddress newAddress = new(name, true, address, bank, eventType, eventAddressRegisterOverrides.ToArray(), bits, length, size, instantaneous);
                        currentEvents.Add(newAddress);

                        if(currentEvents.Count < eventsLookupSize)
                        {
                            EventAddress template = new();

                            while(currentEvents.Count < eventsLookupSize)
                            {
                                currentEvents.Add(template);
                            }
                        }
                    }
                    currentEvents.CopyTo(eventsLookup, 0);

                    IEnumerable<long> keys = eventsLookup.Where(x => x.Active).DistinctBy(x => x.Address).Select(x => x.Address);
                    addressEventsLookup = keys.ToDictionary(x => x, x => eventsLookup.Select((y, idx) => new Tuple<int, long, bool>(idx, y.Address, y.Active)).Where(y => y.Item3 && y.Item2 == x).Select(y => y.Item1).ToArray());

                    _eventsLookupAccessor.Write(0, (byte)1);
                    _eventsLookupAccessor.WriteArray(1, eventsLookup, 0, eventsLookup.Length);
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
                    _eventsSemaphore.Release();
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
                _eventsSemaphore!.WaitOne();

                try
                {
                    List<EventAddress> currentEvents = eventsLookup.Where(x => x.Active && (x.Address != address || x.EventType != eventType || x.Bank != bank)).ToList();

                    if (currentEvents.Count < eventsLookupSize)
                    {
                        EventAddress template = new();

                        while (currentEvents.Count < eventsLookupSize)
                        {
                            currentEvents.Add(template);
                        }
                    }
                    currentEvents.CopyTo(eventsLookup, 0);

                    IEnumerable<long> keys = eventsLookup.Where(x => x.Active).DistinctBy(x => x.Address).Select(x => x.Address);
                    addressEventsLookup = keys.ToDictionary(x => x, x => eventsLookup.Select((y, idx) => new Tuple<int, long, bool>(idx, y.Address, y.Active)).Where(y => y.Item3 && y.Item2 == x).Select(y => y.Item1).ToArray());

                    _eventsLookupAccessor.Write(0, (byte)1);
                    _eventsLookupAccessor.WriteArray(1, currentEvents.ToArray(), 0, currentEvents.Count);
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
                    _eventsSemaphore.Release();
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