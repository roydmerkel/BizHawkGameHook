using GameHook.Domain.Interfaces;
using System.Drawing;
using System.Net;

namespace GameHook.Domain.GameHookEvents
{
    public class HardResetEvent : GameHookEvent, IGameHookEvent
    {
        public HardResetEvent(IGameHookInstance instance, EventAttributes variables) : base(instance, variables)
        {
            Name = "_hardreset";
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
                Instance.Driver.RemoveEvent(EventType.EventType_HardReset, this);
            }
        }
        public override void SetEvent(IGameHookEvent ev)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent(EventType.EventType_HardReset, this);
            }
        }
    }
}
