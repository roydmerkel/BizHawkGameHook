namespace GameHook.Domain.Interfaces
{
    public interface IClientNotifier
    {
        Task SendInstanceReset();
        Task SendMapperLoaded(IGameHookMapper mapper);
        Task SendError(IProblemDetails problemDetails);
        Task SendPropertiesChanged(IEnumerable<IGameHookProperty> properties);

        Task SendImmediateReadValues(IEnumerable<IGameHookProperty> properties);
        Task SendTriggeredEvents(IEnumerable<IGameHookEvent> events);
        Task SendEnabledEvents(IEnumerable<IGameHookEvent> events);
        Task SendDisabledEvents(IEnumerable<IGameHookEvent> events);
    }
}
