using GameHook.Domain.Interfaces;

namespace GameHook.Domain.GameHookEvents
{
    public class WriteEvent(IGameHookInstance instance, EventAttributes variables) : GameHookEvent(instance, variables), IGameHookEvent
    {
        public override void ClearEvent(IGameHookEvent ev)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.RemoveEvent(EventType.EventType_Write, this);
            }
        }
        public override void SetEvent(IGameHookEvent ev)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent(EventType.EventType_Write, this);
            }
        }
    }
}
