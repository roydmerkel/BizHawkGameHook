using GameHook.Domain.Interfaces;
using System.Net;

namespace GameHook.Domain.GameHookEvents
{
    public class HardResetEvent : GameHookEvent, IGameHookEvent
    {
        public HardResetEvent(IGameHookInstance instance, EventAttributes variables) : base(instance, variables)
        {
            SetEvent(0);
        }

        public override void ClearEvent(MemoryAddress address)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.RemoveEvent(0, EventType.EventType_HardReset);
            }
        }
        public override void SetEvent(MemoryAddress address)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent(0, EventType.EventType_HardReset, EventRegisterOverrides);
            }
        }
    }
}
