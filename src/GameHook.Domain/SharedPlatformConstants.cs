namespace GameHook.Domain;
public static class SharedPlatformConstants
{
    public record PlatformEntry
    {
        public bool IsBigEndian { get; set; } = false;
        public bool IsLittleEndian => IsBigEndian == false;
        public string BizhawkIdentifier { get; set; } = string.Empty;
        public int? FrameSkipDefault { get; set; } = null;

        public PlatformMemoryLayoutEntry[] MemoryLayout { get; set; } = [];
    }

    public record PlatformMemoryLayoutEntry
    {
        public string BizhawkIdentifier { get; set; } = string.Empty;
        public int CustomPacketTransmitPosition { get; set; } = 0;
        public int Length { get; set; } = 0;

        public MemoryAddress PhysicalStartingAddress = 0x00;
        public MemoryAddress PhysicalEndingAddress => (MemoryAddress)(PhysicalStartingAddress + Length);
    }

    public const int BIZHAWK_INTEGRATION_VERSION = 0x00;
    public const int BIZHAWK_METADATA_PACKET_SIZE = 32;
    public const int BIZHAWK_ROM_PACKET_SIZE = 0x200000 * 2;
    public const int BIZHAWK_DATA_PACKET_SIZE = 4 * 1024 * 1024;
    public const int BIZHAWK_MAX_DATA_EVENTS = 256 * 2;
    public const int BIZHAWK_MAX_EXECUTION_EVENTS_SIZE = 256;
    public const int BIZHAWK_MAX_EVENTS_SIZE = BIZHAWK_MAX_DATA_EVENTS + BIZHAWK_MAX_EXECUTION_EVENTS_SIZE;
    public const int BIZHAWK_MAX_WRITE_CALLS_SIZE = 256 * 2;

    public static readonly IEnumerable<PlatformEntry> Information = new List<PlatformEntry>()
    {
        new()
        {
            IsBigEndian = true,
            BizhawkIdentifier = "NES",
            MemoryLayout =
            [
                new()
                {
                    BizhawkIdentifier = "RAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x00,
                    Length = 0x800
                }
            ]
        },
        new()
        {
            IsBigEndian = false,
            BizhawkIdentifier = "SNES",
            MemoryLayout =
            [
                new()
                {
                    BizhawkIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x003000,
                    Length = 0x112
                },
                new()
                {
                    BizhawkIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x400000,
                    Length = 0x3EFF
                },
                new()
                {
                    BizhawkIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x7E0000,
                    Length = 0x1FFFF
                }
            ]
        },
        new()
        {
            IsBigEndian = false,
            BizhawkIdentifier = "GB",
            MemoryLayout =
            [
                new() {
                    BizhawkIdentifier = "CartRAM",
                    CustomPacketTransmitPosition = 0x0,
                    PhysicalStartingAddress = 0xA000,
                    Length = 0x1FFF
                },
                new() {
                    BizhawkIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1,
                    PhysicalStartingAddress = 0xC000,
                    Length = 0x1FFF
                },
                new() {
                    BizhawkIdentifier = "VRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0x8000,
                    Length = 0x1FFF
                },
                new() {
                    BizhawkIdentifier = "HRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0xFF80,
                    Length = 0x7E
                }
            ]
        },
        new()
        {
            IsBigEndian = false,
            BizhawkIdentifier = "SGB",
            MemoryLayout =
            [
                new() {
                    BizhawkIdentifier = "CartRAM",
                    CustomPacketTransmitPosition = 0x0,
                    PhysicalStartingAddress = 0xA000,
                    Length = 0x1FFF
                },
                new() {
                    BizhawkIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1,
                    PhysicalStartingAddress = 0xC000,
                    Length = 0x1FFF
                },
                new() {
                    BizhawkIdentifier = "VRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0x8000,
                    Length = 0x1FFF
                },
                new() {
                    BizhawkIdentifier = "HRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0xFF80,
                    Length = 0x7E
                }
            ]
        },
        new()
        {
            IsBigEndian = false,
            BizhawkIdentifier = "GBC",
            MemoryLayout =
            [
                new() {
                    BizhawkIdentifier = "CartRAM",
                    CustomPacketTransmitPosition = 0x0,
                    PhysicalStartingAddress = 0xA000,
                    Length = 0x1FFF
                },
                new() {
                    BizhawkIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1,
                    PhysicalStartingAddress = 0xC000,
                    Length = 0x1FFF
                },
                new() {
                    BizhawkIdentifier = "VRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0x8000,
                    Length = 0x1FFF
                },
                new() {
                    BizhawkIdentifier = "HRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0xFF80,
                    Length = 0x7E
                }
            ]
        },
        new()
        {
            IsBigEndian = true,
            BizhawkIdentifier = "GBA",
            MemoryLayout =
            [
                new()
                {
                    BizhawkIdentifier = "EWRAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x02000000,
                    Length = 0x00040000
                },
                new()
                {
                    BizhawkIdentifier = "IWRAM",
                    CustomPacketTransmitPosition = 0x00040000 + 1,
                    PhysicalStartingAddress = 0x03000000,
                    Length = 0x00008000
                }
            ]
        },
        new()
        {
            IsBigEndian = true,
            BizhawkIdentifier = "NDS",
            FrameSkipDefault = 15,
            MemoryLayout = [
                new() {
                    BizhawkIdentifier = "Main RAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x2000000,
                    Length = 0x400000
                }
            ]
        }
    };
}