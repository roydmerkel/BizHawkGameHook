﻿using GameHook.Domain;
using GameHook.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Xml.Schema;
using static GameHook.Infrastructure.BizHawkInterface;
using static GameHook.Infrastructure.PipeBase<GameHook.Infrastructure.BizHawkInterface.EventOperation>;

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

        List<IGameHookEvent> eventsLookup = [];
        Dictionary<long, IGameHookEvent[]> addressEventsLookup = [];

        PipeClient<EventOperation>? eventsPipe = null;
        PipeClient<WriteCall>? writeCallsPipe = null;
        PipeClient<InstantReadEvents>? instantReadValuesPipe = null;

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
                writeCallsPipe = new("GAMEHOOK_BIZHAWK_WRITE.pipe", x => WriteCall.Deserialize(x));
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
                instantReadValuesPipe = new("GAMEHOOK_BIZHAWK_INSTANT_READ.pipe", x => InstantReadEvents.Deserialize(x));
                instantReadValuesPipe.PipeReadEvent += OnInstantReadValuesRead;
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

        private void OnInstantReadValuesRead(object? sender, PipeBase<InstantReadEvents>.PipeReadArgs e)
        {
            foreach(KeyValuePair<EventAddress, IList<byte[]>> instantReadEvent in e.Arg)
            {
                EventAddress eventAddress = instantReadEvent.Key;
                IList<byte[]> values = instantReadEvent.Value;

                IGameHookEvent? ev = eventsLookup.Where(x => x.SerialNumber == eventAddress.SerialNumber).DefaultIfEmpty(null).FirstOrDefault();
                if(ev != null && ev.Property != null && ev.Property.Instantaneous != null && ev.Property.Instantaneous.Value && ev.Property.ImmediateWriteBytes != null)
                {
                    foreach (byte[] value in values)
                    {
                        ev.Property.ImmediateWriteBytes.Add(value);
                    }
                }
            }
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
            if (writeCallsPipe == null)
            {
                throw new NullReferenceException(nameof(writeCallsPipe));
            }
            try
            {
                IGameHookEvent[] events = (addressEventsLookup != null) ? (addressEventsLookup.ContainsKey(startingMemoryAddress) ? addressEventsLookup[startingMemoryAddress] : []) : [];
                bool frozen = events.Any(x => x?.Property?.Instantaneous != null && x.Property.Instantaneous.Value);

                writeCallsPipe.Write(new WriteCall(true, frozen, startingMemoryAddress, values));
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

        public Task ClearEvents()
        {
            if(eventsPipe == null)
            {
                throw new NullReferenceException(nameof(eventsPipe));
            }
            try
            {
                addressEventsLookup = [];
                eventsLookup = [];
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

                eventsPipe.Write(new EventOperation(EventOperationType.EventOperationType_Add, eventType, serialNumber, new EventAddress(serialNumber, name, active, address, bank, eventType, eventAddressRegisterOverrides.AsEnumerable(), bits, length, size, instantaneous)));
                eventsLookup.Add(eventObj);

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

                eventsPipe.Write(new EventOperation(EventOperationType.EventOperationType_Remove, eventType, serialNumber, new EventAddress(serialNumber, name, active, address, bank, eventType, eventAddressRegisterOverrides.AsEnumerable(), bits, length, size, instantaneous)));
                eventsLookup.Remove(eventObj);

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