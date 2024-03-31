using GameHook.Domain.Interfaces;

namespace GameHook.Domain.GameHookEvents
{
    public class SoftResetEvent : GameHookEvent, IGameHookEvent
    {
        public SoftResetEvent(IGameHookInstance instance, EventAttributes variables) : base(instance, variables)
        {
            SetEvent(0);
        }

        public override void ClearEvent(MemoryAddress address)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.RemoveEvent(0, EventType.EventType_SoftReset);
            }
        }
        public override void SetEvent(MemoryAddress address)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent(0, EventType.EventType_SoftReset, EventRegisterOverrides);
            }
        }
    }
}
