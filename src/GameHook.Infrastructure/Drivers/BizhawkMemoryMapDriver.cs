using GameHook.Domain;
using GameHook.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using static GameHook.Infrastructure.BizHawkInterface;
using static GameHook.Infrastructure.PipeBase<GameHook.Infrastructure.BizHawkInterface.EventOperation>;
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

        private ILogger<BizhawkMemoryMapDriver> Logger { get; }
        private readonly AppSettings _appSettings;

        private MemoryMappedFile? _metadataMemoryMappedFile = null;
        private MemoryMappedViewAccessor? _metadataAccessor = null;

        private MemoryMappedFile? _dataMemoryMappedFile = null;
        private MemoryMappedViewAccessor? _dataAccessor = null;

        private MemoryMappedFile? _writeCallMemoryMappedFile = null;
        private MemoryMappedViewAccessor? _writeCallAccessor = null;
        private int _writeCallElementSize = 0;
        private Semaphore? _writeCallSemaphore = null;

        const int eventsLookupSize = SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE;
        IGameHookEvent[] eventsLookup = [];
        Dictionary<long, IGameHookEvent[]> addressEventsLookup = [];

        PipeClient<EventOperation>? eventsPipe = null;

        public BizhawkMemoryMapDriver(ILogger<BizhawkMemoryMapDriver> logger, AppSettings appSettings)
        {
            Logger = logger;
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

            eventsLookup = new IGameHookEvent[eventsLookupSize];
            addressEventsLookup = [];

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
                eventsPipe = new("GAMEHOOK_BIZHAWK_EVENTS.pipe", x => EventOperation.Deserialize(x));
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

        void EventPipeConnectedHandler(object sender, PipeConnectedArgs e)
        {
        }

        void EventReadHandler(object sender, PipeReadArgs e)
        {
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

                    IGameHookEvent[] events = (addressEventsLookup != null) ? (addressEventsLookup.ContainsKey(startingMemoryAddress) ? addressEventsLookup[startingMemoryAddress] : []) : [];
                    bool frozen = events.Any(x => x?.Property?.Instantaneous != null && x.Property.Instantaneous.Value);

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
            if(eventsPipe == null)
            {
                throw new NullReferenceException(nameof(eventsPipe));
            }
            try
            {
                eventsLookup = [];
                addressEventsLookup = [];
                eventsPipe.Write(new EventOperation(EventOperationType.EventOperationType_Clear, EventType.EventType_Undefined, null, null));

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

        public Task AddEvent(EventType eventType, IGameHookEvent eventObj)
        {
            if (eventsPipe == null)
            {
                throw new NullReferenceException(nameof(eventsPipe));
            }
            if (eventObj == null)
            {
                throw new NullReferenceException(nameof(eventObj));
            }
            try
            {
                List<EventAddressRegisterOverride> eventAddressRegisterOverrides = [];
                foreach(var over in eventObj.EventRegisterOverrides)
                {
                    string registerValue = over?.Register ?? throw new NullReferenceException(nameof(over.Register));
                    string overValue = over?.Value?.ToString() ?? throw new NullReferenceException(nameof(over.Value));
                    eventAddressRegisterOverrides.Add(new EventAddressRegisterOverride(registerValue, ulong.Parse(overValue)));
                }
                string? name = eventObj.Name;
                bool active = true;
                long address = (eventObj.Address != null) ? eventObj.Address.Value : 0;
                ushort bank = (eventObj.Bank != null) ? eventObj.Bank.Value : ushort.MaxValue;
                string? bits = eventObj.Bits;
                int length = (eventObj.Length != null) ? eventObj.Length.Value : 0;
                int size = eventObj.Size != null ? eventObj.Size.Value : 0;
                bool instantaneous = (eventObj?.Property?.Instantaneous != null) && eventObj.Property.Instantaneous.Value;
                ulong serialNumber = eventObj?.SerialNumber ?? 0;

                eventsPipe.Write(new EventOperation(EventOperationType.EventOperationType_Add, eventType, serialNumber, new EventAddress(name, active, address, bank, eventType, eventAddressRegisterOverrides.AsEnumerable(), bits, length, size, instantaneous)));

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

        public Task RemoveEvent(EventType eventType, IGameHookEvent eventObj)
        {
            if (eventsPipe == null)
            {
                throw new NullReferenceException(nameof(eventsPipe));
            }
            if(eventObj == null)
            {
                throw new NullReferenceException(nameof(eventsPipe));
            }
            try
            {
                List<EventAddressRegisterOverride> eventAddressRegisterOverrides = [];
                foreach (var over in eventObj.EventRegisterOverrides)
                {
                    string registerValue = over?.Register ?? throw new NullReferenceException(nameof(over.Register));
                    string overValue = over?.Value?.ToString() ?? throw new NullReferenceException(nameof(over.Value));
                    eventAddressRegisterOverrides.Add(new EventAddressRegisterOverride(registerValue, ulong.Parse(overValue)));
                }
                string? name = eventObj.Name;
                bool active = true;
                long address = (eventObj.Address != null) ? eventObj.Address.Value : 0;
                ushort bank = (eventObj.Bank != null) ? eventObj.Bank.Value : ushort.MaxValue;
                string? bits = eventObj.Bits;
                int length = (eventObj.Length != null) ? eventObj.Length.Value : 0;
                int size = eventObj.Size != null ? eventObj.Size.Value : 0;
                bool instantaneous = (eventObj?.Property?.Instantaneous != null) && eventObj.Property.Instantaneous.Value;
                ulong serialNumber = eventObj?.SerialNumber ?? 0;

                eventsPipe.Write(new EventOperation(EventOperationType.EventOperationType_Remove, eventType, serialNumber, new EventAddress(name, active, address, bank, eventType, eventAddressRegisterOverrides.AsEnumerable(), bits, length, size, instantaneous)));

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