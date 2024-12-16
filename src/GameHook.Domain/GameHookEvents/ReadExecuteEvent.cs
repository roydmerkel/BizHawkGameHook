using GameHook.Domain.Interfaces;

namespace GameHook.Domain.GameHookEvents
{
    public class ReadExecuteEvent(IGameHookInstance instance, EventAttributes variables) : GameHookEvent(instance, variables), IGameHookEvent
    {
        public override void ClearEvent()
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.RemoveEvent(EventType.EventType_Read, this);
                Instance.Driver.RemoveEvent(EventType.EventType_Execute, this);
            }
        }
        public override void SetEvent()
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent(EventType.EventType_Execute, this);
                Instance.Driver.AddEvent(EventType.EventType_Read, this);
            }
        }

        public override void DisableEvent()
        {
            Enabled = false;
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.DisableEvent(EventType.EventType_Read, this);
                Instance.Driver.DisableEvent(EventType.EventType_Execute, this);
            }
        }

        public override void EnableEvent()
        {
            Enabled = true;
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.EnableEvent(EventType.EventType_Execute, this);
                Instance.Driver.EnableEvent(EventType.EventType_Read, this);
            }
        }
    }
}
