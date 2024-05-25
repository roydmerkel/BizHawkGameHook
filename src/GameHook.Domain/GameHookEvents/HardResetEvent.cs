using GameHook.Domain.Interfaces;
using System.Net;

namespace GameHook.Domain.GameHookEvents
{
    public class HardResetEvent : GameHookEvent, IGameHookEvent
    {
        public HardResetEvent(IGameHookInstance instance, EventAttributes variables) : base(instance, variables)
        {
            SetEvent("_hardreset", 0, 0, null, 1, 0, false);
        }

        public override void ClearEvent(MemoryAddress address, ushort bank)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.RemoveEvent(0, ushort.MaxValue, EventType.EventType_HardReset);
            }
        }
        public override void SetEvent(string? name, MemoryAddress address, ushort bank, string? bits, int length, int size, bool instantaneous)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent("_hardreset", 0, ushort.MaxValue, EventType.EventType_HardReset, EventRegisterOverrides, bits, length, size, instantaneous);
            }
        }
    }
}
