using GameHook.Domain.Interfaces;

namespace GameHook.Domain.GameHookEvents
{
    public class VariableInstantaneousEvent : GameHookEvent, IGameHookEvent
    {
        public VariableInstantaneousEvent(IGameHookInstance instance, EventAttributes variables) : base(instance, variables)
        {
        }

        public override void ClearEvent(MemoryAddress address)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.RemoveEvent(address, EventType.EventType_Read);
                Instance.Driver.RemoveEvent(address, EventType.EventType_Write);
            }
        }
        public override void SetEvent(MemoryAddress address)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent(address, EventType.EventType_Write, EventRegisterOverrides);
                Instance.Driver.AddEvent(address, EventType.EventType_Read, EventRegisterOverrides);
            }
        }
    }
}
