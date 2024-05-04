namespace GameHook.Domain.Interfaces
{
    /// <summary>
    /// Driver interface for interacting with a emulator.
    /// 
    /// - Driver should not log anything above LogDebug.
    /// - Any errors encountered should be thrown as exceptions.
    /// </summary>
    public interface IGameHookDriver
    {
        string ProperName { get; }

        int DelayMsBetweenReads { get; }

        Task EstablishConnection();

        Task<Dictionary<uint, byte[]>> ReadBytes(IEnumerable<MemoryAddressBlock> blocks);

        Task WriteBytes(uint startingMemoryAddress, byte[] values);

        Task ClearEvents();

        Task AddEvent(long address, ushort bank, EventType eventType, EventRegisterOverride[] eventRegisterOverrides, string? bits, int length, int size);

        Task RemoveEvent(long address, ushort bank, EventType eventType);
    }

    public interface IBizhawkMemoryMapDriver : IGameHookDriver { }
    public interface IRetroArchUdpPollingDriver : IGameHookDriver { }
    public interface IStaticMemoryDriver : IGameHookDriver
    {
        Task SetMemoryFragment(string filename);
    }
}