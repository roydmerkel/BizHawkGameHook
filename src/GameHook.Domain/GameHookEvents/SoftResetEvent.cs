using GameHook.Domain.Interfaces;
using System.Drawing;

namespace GameHook.Domain.GameHookEvents
{
    public class SoftResetEvent : GameHookEvent, IGameHookEvent
    {
        public SoftResetEvent(IGameHookInstance instance, EventAttributes variables) : base(instance, variables)
        {
            Name = "_softreset";
            Address = 0;
            Bank = ushort.MaxValue;
            Bits = null;
            Length = 1;
            Size = 0;
        }

        public override void ClearEvent(IGameHookEvent ev)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.RemoveEvent(EventType.EventType_SoftReset, this);
            }
        }
        public override void SetEvent(IGameHookEvent ev)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent(EventType.EventType_SoftReset, this);
            }
        }
    }
}
