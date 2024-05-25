using GameHook.Domain.Interfaces;

namespace GameHook.Domain.GameHookEvents
{
    public class ExecuteEvent : GameHookEvent, IGameHookEvent
    {
        public ExecuteEvent(IGameHookInstance instance, EventAttributes variables) : base(instance, variables)
        {
        }

        public override void ClearEvent(MemoryAddress address, ushort bank)
        {
            if(Instance != null && Instance.Driver != null)
            {
                Instance.Driver.RemoveEvent(address, bank, EventType.EventType_Execute);
            }
        }
        public override void SetEvent(string? name, MemoryAddress address, ushort bank, string? bits, int length, int size, bool instantaneous)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent(name, address, bank, EventType.EventType_Execute, EventRegisterOverrides, bits, length, size, instantaneous);
            }
        }
    }
}
