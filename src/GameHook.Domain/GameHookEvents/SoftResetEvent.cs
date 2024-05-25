using GameHook.Domain.Interfaces;

namespace GameHook.Domain.GameHookEvents
{
    public class SoftResetEvent : GameHookEvent, IGameHookEvent
    {
        public SoftResetEvent(IGameHookInstance instance, EventAttributes variables) : base(instance, variables)
        {
            SetEvent("_softreset", 0, 0, null, 1, 0, false);
        }

        public override void ClearEvent(MemoryAddress address, ushort bank)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.RemoveEvent(0, ushort.MaxValue, EventType.EventType_SoftReset);
            }
        }
        public override void SetEvent(string? name, MemoryAddress address, ushort bank, string? bits, int length, int size, bool instantaneous)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent("_softreset", 0, ushort.MaxValue, EventType.EventType_SoftReset, EventRegisterOverrides, bits, length, size, instantaneous);
            }
        }
    }
}
