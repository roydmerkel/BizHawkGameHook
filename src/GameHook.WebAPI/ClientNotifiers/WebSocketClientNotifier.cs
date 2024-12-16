using GameHook.Domain.Interfaces;
using GameHook.WebAPI.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GameHook.WebAPI.ClientNotifiers
{
    public class WebSocketClientNotifier(ILogger<WebSocketClientNotifier> logger, IHubContext<UpdateHub> hubContext) : IClientNotifier
    {
        private readonly ILogger<WebSocketClientNotifier> _logger = logger;
        private readonly IHubContext<UpdateHub> _hubContext = hubContext;

        public Task SendInstanceReset() =>
            _hubContext.Clients.All.SendAsync("InstanceReset");

        public Task SendMapperLoaded(IGameHookMapper mapper) =>
            _hubContext.Clients.All.SendAsync("MapperLoaded");

        public Task SendError(IProblemDetails problemDetails) =>
            _hubContext.Clients.All.SendAsync("Error", problemDetails);

        public async Task SendPropertiesChanged(IEnumerable<IGameHookProperty> properties)
        {
            await _hubContext.Clients.All.SendAsync("PropertiesChanged", properties.Select(x => new
            {
                path = x.Path,
                memoryContainer = x.MemoryContainer,
                address = x.Address,
                length = x.Length,
                size = x.Size,
                reference = x.Reference,
                bits = x.Bits,
                description = x.Description,
                value = x.Value,
                bytes = x.Bytes?.Select(x => (int)x).ToArray(),

                isFrozen = x.IsFrozen,
                isReadOnly = x.IsReadOnly,

                fieldsChanged = x.FieldsChanged
            }).ToArray());
        }

        public async Task SendImmediateReadValues(IEnumerable<IGameHookProperty> properties)
        {
            await _hubContext.Clients.All.SendAsync("ImmediateReadValues", properties.Select(x => new
            {
                path = x.Path,
                memoryContainer = x.MemoryContainer,
                address = x.Address,
                immediateWriteValues = x.ImmediateWriteValues
            }).ToArray());
        }

        public async Task SendTriggeredEvents(IEnumerable<IGameHookEvent> events)
        {
            await _hubContext.Clients.All.SendAsync("TriggeredEvents", events.Select(x => new
            {
                name = x.Name,
                memoryContainer = x.MemoryContainer,
                address = x.Address,
                bank = x.Bank,
                eventType = x.EventType,
                description = x.Description,
                length = x.Length,
                size = x.Size,
                bits = x.Bits,
                triggered = x.Triggered,
                enabled = x.Enabled,
        }).ToArray());
        }

        public async Task SendEnabledEvents(IEnumerable<IGameHookEvent> events)
        {
            await _hubContext.Clients.All.SendAsync("EnabledEvents", events.Select(x => new
            {
                name = x.Name,
                memoryContainer = x.MemoryContainer,
                address = x.Address,
                bank = x.Bank,
                eventType = x.EventType,
                description = x.Description,
                length = x.Length,
                size = x.Size,
                bits = x.Bits,
                triggered = x.Triggered,
                enabled = x.Enabled,
            }).ToArray());
        }

        public async Task SendDisabledEvents(IEnumerable<IGameHookEvent> events)
        {
            await _hubContext.Clients.All.SendAsync("DisabledEvents", events.Select(x => new
            {
                name = x.Name,
                memoryContainer = x.MemoryContainer,
                address = x.Address,
                bank = x.Bank,
                eventType = x.EventType,
                description = x.Description,
                length = x.Length,
                size = x.Size,
                bits = x.Bits,
                triggered = x.Triggered,
                enabled = x.Enabled,
            }).ToArray());
        }
    }
}
