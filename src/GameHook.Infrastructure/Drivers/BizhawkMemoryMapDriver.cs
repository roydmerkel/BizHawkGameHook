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

        public BizhawkMemoryMapDriver(AppSettings appSettings)
        {
            _appSettings = appSettings;
            DelayMsBetweenReads = _appSettings.BIZHAWK_DELAY_MS_BETWEEN_READS;
        }

        static string GetStringFromBytes(byte[] data)
        {
            return Encoding.UTF8.GetString(data).TrimEnd('\0');
        }

        static byte[] GetFromMemoryMappedFile(string filename, int fileSize)
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
            try
            {
                using (Semaphore writeCallsSemaphore = Semaphore.OpenExisting(name: "GAMEHOOK_BIZHAWK_WRITE_CALLS.semaphore"))
                //using (Semaphore eventsSemaphore = Semaphore.OpenExisting(name: "GAMEHOOK_BIZHAWK_EVENTS.semaphore"))
                {
                    writeCallsSemaphore.WaitOne();

                    try
                    {
                        int writeCallElementSize = Marshal.SizeOf(typeof(WriteCall));
                        long writeCallMemoryMappedSize = sizeof(int) + sizeof(int) + writeCallElementSize * SharedPlatformConstants.BIZHAWK_MAX_WRITE_CALLS_SIZE;
                        const int writeCallsLookupSize = SharedPlatformConstants.BIZHAWK_MAX_WRITE_CALLS_SIZE;
                        WriteCall[] writeCalls = new WriteCall[writeCallsLookupSize];
                        using var mmfWriteData = MemoryMappedFile.OpenExisting("GAMEHOOK_BIZHAWK_WRITE_CALLS.bin", MemoryMappedFileRights.ReadWrite);
                        using var mmfWriteAccessor = mmfWriteData.CreateViewAccessor(0, writeCallMemoryMappedSize, MemoryMappedFileAccess.ReadWrite);

                        // read in the current queue state.
                        mmfWriteAccessor.Read(0, out int front);
                        mmfWriteAccessor.Read(sizeof(int), out int rear);
                        mmfWriteAccessor.ReadArray(sizeof(int) * 2, writeCalls, 0, writeCallsLookupSize);

                        // create the queue and enqueue the write
                        CircularArrayQueue<WriteCall> queue = new(writeCalls, front, rear);
                        WriteCall writeCall = new(true, false, startingMemoryAddress, values);
                        if (!queue.Enqueue(writeCall))
                        {
                            throw new Exception("write data queue full, too many write events sent too closely together.");
                        }

                        // write the data back.
                        mmfWriteAccessor.Write(0, queue.Front);
                        mmfWriteAccessor.Write(sizeof(int), queue.Rear);
                        writeCalls = queue.Array;
                        mmfWriteAccessor.WriteArray(sizeof(int) * 2, writeCalls, 0, writeCallsLookupSize);
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
                        writeCallsSemaphore.Release();
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
                        EventAddress template = new();
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

        public Task AddEvent(string? name, long address, ushort bank, EventType eventType, EventRegisterOverride[] eventRegisterOverrides, string? bits, int length, int size)
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

                            EventAddress newAddress = new(name, true, address, bank, eventType, eventAddressRegisterOverrides.ToArray(), bits, length, size);
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
                        mmfAccessor.Write(0, (byte)1);
                        mmfAccessor.WriteArray(1, currentEvents.ToArray(), 0, currentEvents.Count);
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

                        if (currentEvents.Count < eventsLookupSize)
                        {
                            EventAddress template = new();

                            while (currentEvents.Count < eventsLookupSize)
                            {
                                currentEvents.Add(template);
                            }
                        }

                        mmfAccessor.Write(0, (byte)1);
                        mmfAccessor.WriteArray(1, currentEvents.ToArray(), 0, currentEvents.Count);
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