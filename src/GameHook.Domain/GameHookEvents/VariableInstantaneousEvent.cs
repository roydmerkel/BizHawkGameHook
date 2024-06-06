using GameHook.Domain.Interfaces;

namespace GameHook.Domain.GameHookEvents
{
    public class VariableInstantaneousEvent : GameHookEvent, IGameHookEvent
    {
        public VariableInstantaneousEvent(IGameHookInstance instance, EventAttributes variables) : base(instance, variables)
        {
            Size = Property?.Size ?? 0;
            Length = Property?.Length ?? 1;
            Bits = Property?.Bits;
        }

        public override void ClearEvent(IGameHookEvent ev)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.RemoveEvent(EventType.EventType_Read, this);
                Instance.Driver.RemoveEvent(EventType.EventType_Write, this);
            }
        }
        public override void SetEvent(IGameHookEvent ev)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent(EventType.EventType_Write, this);
                Instance.Driver.AddEvent(EventType.EventType_Read, this);
            }
        }
    }
}
