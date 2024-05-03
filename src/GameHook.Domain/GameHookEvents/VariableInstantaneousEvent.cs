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

        public override void ClearEvent(MemoryAddress address, ushort bank)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.RemoveEvent(address, bank, EventType.EventType_Read);
                Instance.Driver.RemoveEvent(address, bank, EventType.EventType_Write);
            }
        }
        public override void SetEvent(MemoryAddress address, ushort bank)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent(address, bank, EventType.EventType_Write, EventRegisterOverrides);
                Instance.Driver.AddEvent(address, bank, EventType.EventType_Read, EventRegisterOverrides);
            }
        }
    }
}
