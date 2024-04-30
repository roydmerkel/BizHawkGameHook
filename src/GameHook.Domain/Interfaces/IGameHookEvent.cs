namespace GameHook.Domain.Interfaces
{
    [Flags]
    public enum EventType
    {
        EventType_Undefined = 0,
        EventType_Read = 1,
        EventType_Write = 2,
        EventType_Execute = 4,
        EventType_HardReset = 8,
        EventType_SoftReset = 16,
        EventType_ReadWrite = EventType_Read | EventType_Write,
        EventType_ReadExecute = EventType_Read | EventType_Execute,
        EventType_WriteExecute = EventType_Write | EventType_Execute,
        EventType_ReadWriteExecute = EventType_Read | EventType_Write | EventType_Execute,
    }

    public record EventRegisterOverride(string? Register, object? Value);

    public record EventAttributes
    {
        public required string Name { get; init; }
        public string? MemoryContainer { get; init; }
        public ushort? Bank { get; init; }
        public string? Address { get; init; }
        public required EventType EventType { get; init; }
        public string? Description { get; set; }
        public IGameHookProperty? Property { get; init; }

        public IEnumerable<EventRegisterOverride> EventRegisterOverrides { get; init; } = new List<EventRegisterOverride>();
    }

    public interface IGameHookEvent
    {
        string Name { get; }
        string? MemoryContainer { get; }
        uint? Address { get; }
        EventType EventType { get; }
        string? Description { get; set; }
        IGameHookProperty? Property { get; }

        EventRegisterOverride[] EventRegisterOverrides { get; }

        void ProcessLoop(IMemoryManager container);
        void ClearEvent(MemoryAddress address, ushort bank);
        void SetEvent(MemoryAddress address, ushort bank);
        void UpdateAddressFromProperty();
    }
}
