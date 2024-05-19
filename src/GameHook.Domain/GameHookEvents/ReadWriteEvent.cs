﻿using GameHook.Domain.Interfaces;

namespace GameHook.Domain.GameHookEvents
{
    public class ReadWriteEvent : GameHookEvent, IGameHookEvent
    {
        public ReadWriteEvent(IGameHookInstance instance, EventAttributes variables) : base(instance, variables)
        {
        }

        public override void ClearEvent(MemoryAddress address, ushort bank)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.RemoveEvent(address, bank, EventType.EventType_Read);
                Instance.Driver.RemoveEvent(address, bank, EventType.EventType_Write);
            }
        }
        public override void SetEvent(string? name, MemoryAddress address, ushort bank, string? bits, int length, int size)
        {
            if (Instance != null && Instance.Driver != null)
            {
                Instance.Driver.AddEvent(name, address, bank, EventType.EventType_Write, EventRegisterOverrides, bits, length, size);
                Instance.Driver.AddEvent(name, address, bank, EventType.EventType_Read, EventRegisterOverrides, bits, length, size);
            }
        }
    }
}
