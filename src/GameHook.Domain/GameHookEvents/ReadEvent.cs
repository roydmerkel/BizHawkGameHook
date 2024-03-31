using GameHook.Domain.Interfaces;

namespace GameHook.Domain.GameHookEvents
{
    public class ReadEvent : GameHookEvent, IGameHookEvent
    {
        public ReadEvent(IGameHookInstance instance, EventAttributes variables) : base(instance, variables)
        {
        }

        public override void ClearEvent(MemoryAddress address)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.RemoveEvent(address, EventType.EventType_Read);
            }
        }
        public override void SetEvent(MemoryAddress address)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent(address, EventType.EventType_Read, EventRegisterOverrides);
            }
        }
    }
}
