using BizHawk.Client.Common;
using BizHawk.Client.EmuHawk;
using BizHawk.Common;
using BizHawk.Emulation.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Drawing;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace GameHookIntegration;

[ExternalTool("GameHook.Integrations.BizHawk")]
public sealed class GameHookIntegrationForm : ToolFormBase, IToolForm, IExternalToolForm, IDisposable
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

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EventAddressRegisterOverride
    {
        public ulong Register; // actually an 8 byte ascii string
        public ulong Value;

        public EventAddressRegisterOverride() : this("", 0x00)
        {
        }
        public EventAddressRegisterOverride(string registerName, ulong value)
        {
            if (registerName == null)
            {
                throw new ArgumentNullException(nameof(registerName));
            }
            // string needs to be same size as Register (ulong)
            //
            // TODO: a fixed size char array would work better, but I would require
            // adding unsafe and I'm not sure if this would break BizHawk or cause
            // build headaches.
            else if (registerName.Length < 0 || registerName.Length > sizeof(ulong) - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(registerName));
            }
            // encode as ASCII, don't need custom check as c# throws an exception for
            // trying to encode non-ascii strings as ascii.
            else
            {
                List<byte> bytes = new List<byte>(Encoding.ASCII.GetBytes(registerName));
                if (bytes == null)
                {
                    bytes = new List<byte> { };
                }
                while (bytes.Count < sizeof(ulong))
                {
                    bytes.Add(0);
                }
                Register = BitConverter.ToUInt64(bytes.ToArray(), 0);
            }
            Value = value;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EventAddress
    {
        const int MaxRegisterOverride = 6;

        public bool Active;
        public long Address;
        public EventType EventType;
        public EventAddressRegisterOverride EventAddressRegisterOverride0;
        public EventAddressRegisterOverride EventAddressRegisterOverride1;
        public EventAddressRegisterOverride EventAddressRegisterOverride2;
        public EventAddressRegisterOverride EventAddressRegisterOverride3;
        public EventAddressRegisterOverride EventAddressRegisterOverride4;
        public EventAddressRegisterOverride EventAddressRegisterOverride5;

        public EventAddress() : this(false, 0x00, EventType.EventType_Undefined, new List<EventAddressRegisterOverride> { })
        {
        }
        public EventAddress(bool active, long address, EventType eventType, IEnumerable<EventAddressRegisterOverride> eventAddressRegisterOverrides)
        {
            EventAddressRegisterOverride0 = new EventAddressRegisterOverride();
            EventAddressRegisterOverride1 = new EventAddressRegisterOverride();
            EventAddressRegisterOverride2 = new EventAddressRegisterOverride();
            EventAddressRegisterOverride3 = new EventAddressRegisterOverride();
            EventAddressRegisterOverride4 = new EventAddressRegisterOverride();
            EventAddressRegisterOverride5 = new EventAddressRegisterOverride();

            Active = active;
            Address = address;
            EventType = eventType;
            if (eventAddressRegisterOverrides == null)
            {
                throw new ArgumentNullException(nameof(eventAddressRegisterOverrides));
            }
            else if (eventAddressRegisterOverrides.Count() < 0 || eventAddressRegisterOverrides.Count() > MaxRegisterOverride)
            {
                throw new ArgumentOutOfRangeException(nameof(eventAddressRegisterOverrides));
            }
            else
            {
                int idx = 0;
                // TODO: this is an UGLY kludge to get arround pointers, and fixed size arrays
                // requiring "unsafe", which I'm not sure will break BizHawk or not or cause
                // other headaches. In c# 8/.net 2012 then [System.Runtime.CompilerServices.InlineArray()]
                // attribute can be used to bypass.
                foreach (EventAddressRegisterOverride eventAddressRegisterOverride in eventAddressRegisterOverrides)
                {
                    switch (idx)
                    {
                        case 0:
                            EventAddressRegisterOverride0 = eventAddressRegisterOverride;
                            break;
                        case 1:
                            EventAddressRegisterOverride1 = eventAddressRegisterOverride;
                            break;
                        case 2:
                            EventAddressRegisterOverride2 = eventAddressRegisterOverride;
                            break;
                        case 3:
                            EventAddressRegisterOverride3 = eventAddressRegisterOverride;
                            break;
                        case 4:
                            EventAddressRegisterOverride4 = eventAddressRegisterOverride;
                            break;
                        case 5:
                            EventAddressRegisterOverride5 = eventAddressRegisterOverride;
                            break;
                        default:
                            throw new Exception("unexpected index");
                    }
                    idx++;
                }
            }
        }
    }

    public ApiContainer? APIs { get; set; }

    [OptionalService]
    public IMemoryDomains? MemoryDomains { get; set; }

    [OptionalService]
    public IDebuggable? Debuggable { get; set; }

    [OptionalService]
    public IJoypadApi? Joypad { get; set; }

    protected override string WindowTitleStatic => "GameHook Integration";

    private readonly Label MainLabel = new() { Text = "Loading...", Height = 50, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Top };

    private readonly MemoryMappedFile GameHookMetadata_MemoryMappedFile;
    private readonly MemoryMappedViewAccessor GameHookMetadata_Accessor;

    private readonly MemoryMappedFile? GameHookData_MemoryMappedFile;
    private readonly MemoryMappedViewAccessor? GameHookData_Accessor;

    private readonly MemoryMappedFile? GameHookEventsLookup_MemoryMappedFile;
    private readonly MemoryMappedViewAccessor? GameHookEventsLookup_Accessor;
    private int GameHookEventsLookup_ElementSize;
    private readonly Semaphore GameHookEvents_eventsSemaphore;

    private byte[] DataBuffer { get; } = new byte[SharedPlatformConstants.BIZHAWK_DATA_PACKET_SIZE];

    private string System = string.Empty;

    private SharedPlatformConstants.PlatformEntry? Platform = null;
    private int? FrameSkip = null;

    public delegate void HardResetCallbackDelegate();
    public delegate void SoftResetCallbackDelegate();

    public HardResetCallbackDelegate? HardReset { get; set; } = null;
    public SoftResetCallbackDelegate? SoftReset { get; set; } = null;

    public GameHookIntegrationForm()
    {
        ShowInTaskbar = false;

        ClientSize = new(300, 60);
        SuspendLayout();

        Controls.Add(MainLabel);

        ResumeLayout(performLayout: false);
        PerformLayout();

        GameHookMetadata_MemoryMappedFile = MemoryMappedFile.CreateOrOpen("GAMEHOOK_BIZHAWK.bin", SharedPlatformConstants.BIZHAWK_METADATA_PACKET_SIZE, MemoryMappedFileAccess.ReadWrite);
        GameHookMetadata_Accessor = GameHookMetadata_MemoryMappedFile.CreateViewAccessor();

        GameHookData_MemoryMappedFile = MemoryMappedFile.CreateOrOpen("GAMEHOOK_BIZHAWK_DATA.bin", SharedPlatformConstants.BIZHAWK_DATA_PACKET_SIZE, MemoryMappedFileAccess.ReadWrite);
        GameHookData_Accessor = GameHookData_MemoryMappedFile.CreateViewAccessor();

        GameHookEventsLookup_ElementSize = Marshal.SizeOf(typeof(EventAddress));
        long memoryMappedSize = 1 + GameHookEventsLookup_ElementSize * SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE;
        GameHookEventsLookup_MemoryMappedFile = MemoryMappedFile.CreateOrOpen("GAMEHOOK_BIZHAWK_EVENTS_LOOKUPS.bin", memoryMappedSize, MemoryMappedFileAccess.ReadWrite);
        GameHookEventsLookup_Accessor = GameHookEventsLookup_MemoryMappedFile.CreateViewAccessor(0, memoryMappedSize, MemoryMappedFileAccess.ReadWrite);

        GameHookEvents_eventsSemaphore = new Semaphore(initialCount: 1, maximumCount: 1, name: "GAMEHOOK_BIZHAWK_EVENTS.semaphore");
    }

    private void SyncEvents()
    {
        //Debuggable.MemoryCallbacks.Clear();
        GameHookEvents_eventsSemaphore.WaitOne();
        byte dirtyInt = 0;
        GameHookEventsLookup_Accessor?.Read(0, out dirtyInt);
        bool dirty = dirtyInt != 0;
        if (dirty)
        {
            IDictionary<string, SharedPlatformConstants.PlatformMemoryLayoutEntry> scopeToEntry = new Dictionary<string, SharedPlatformConstants.PlatformMemoryLayoutEntry>();
            if (Platform != null)
            {
                foreach (var i in Platform.MemoryLayout)
                {
                    scopeToEntry.Add(i.BizhawkIdentifier, i);
                }
            }
            EventAddress[] eventAddressLookups = new EventAddress[SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE];
            GameHookEventsLookup_Accessor?.ReadArray(1, eventAddressLookups, 0, SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE);

            IEnumerable<Tuple<uint, EventType>> existingMemoryBreakpoints = 
                (Debuggable == null) ? new Tuple<uint, EventType>[0] : 
                    (Debuggable.MemoryCallbacks == null) ? new Tuple<uint, EventType>[0] : 
                        Debuggable.MemoryCallbacks.Where(x => x.Address != null)
                            .Select(x => new Tuple<uint, EventType>(x.Address!.Value + Convert.ToUInt32(scopeToEntry[x.Scope].PhysicalStartingAddress), MemoryCallbackTypeToEventType(x.Type)));

            // check for missing elements from existingMemoryBreakpoints in eventAddressLookups and remove them
            if (existingMemoryBreakpoints != null)
            {
                foreach (var missingAddress in existingMemoryBreakpoints.Where(x => !eventAddressLookups.Any(y => y.Active && x.Item1 == y.Address && x.Item2 == y.EventType)).Select(x => x))
                {
                    var toRemove = Debuggable?.MemoryCallbacks?.Where(x => x.Address == missingAddress.Item1 && x.Type == EventTypeToMemoryCallbackType(missingAddress.Item2)).Select(x => new Tuple<IMemoryCallback, MemoryCallbackDelegate>(x, x.Callback));
                    if (toRemove != null)
                    {
                        foreach (var i in toRemove)
                        {
                            Console.WriteLine($"remove: {i.Item1.Name}");
                        }
                        Debuggable?.MemoryCallbacks?.RemoveAll(toRemove.Select(x => x.Item2));
                    }
                }
            }

            // check for missing breakpoints
            var missingEventAddressLookups = eventAddressLookups.Where(
                x => (
                    x.Active && 
                    (existingMemoryBreakpoints == null || 
                    !existingMemoryBreakpoints.Any(y => y.Item1 == x.Address && y.Item2 == x.EventType))
                )
                || (x.Active && (x.EventType & EventType.EventType_HardReset) != 0 && HardReset == null)
                || (x.Active && (x.EventType & EventType.EventType_SoftReset) != 0 && SoftReset == null)
            ).Select(x => x);
            foreach(var missingLookup in missingEventAddressLookups)
            {
                Console.WriteLine($"Adding: {missingLookup.Address} --> {missingLookup.EventType}");
                if (Platform != null)
                {
                    bool found = false;
                    if ((missingLookup.EventType & EventType.EventType_HardReset) != 0)
                    {
                        found = true;
                        HardReset = new HardResetCallbackDelegate(() =>
                        {
                            Console.Out.WriteLine($"_hardreset");
                            return;
                        });
                    }
                    if ((missingLookup.EventType & EventType.EventType_SoftReset) != 0)
                    {
                        found = true;
                        SoftReset = new SoftResetCallbackDelegate(() =>
                        {
                            Console.Out.WriteLine($"_softreset");
                            return;
                        });
                    }
                    foreach (SharedPlatformConstants.PlatformMemoryLayoutEntry i in Platform.MemoryLayout)
                    {
                        if(i != null && i.PhysicalStartingAddress <= missingLookup.Address && i.PhysicalEndingAddress >= missingLookup.Address)
                        {
                            found = true;
                            long address = missingLookup.Address - i.PhysicalStartingAddress;
                            if((missingLookup.EventType & EventType.EventType_Read) != 0)
                            {
                                Debuggable!.MemoryCallbacks!.Add(
                                    new MemoryCallback(i.BizhawkIdentifier, 
                                                        MemoryCallbackType.Read,
                                                        $"BizHawkGameHook_{missingLookup.Address:X}_read", 
                                                        new MemoryCallbackDelegate(
                                                            (address, value, flags) => {
                                                                //Console.Out.WriteLine($"BizHawkGameHook_{missingLookup.Address:X}_read");
                                                                return; 
                                                            }
                                                        ), 
                                                        Convert.ToUInt32(address), 
                                                        null));
                            }
                            if ((missingLookup.EventType & EventType.EventType_Write) != 0)
                            {
                                Debuggable!.MemoryCallbacks!.Add(
                                    new MemoryCallback(i.BizhawkIdentifier,
                                                        MemoryCallbackType.Write,
                                                        $"BizHawkGameHook_{missingLookup.Address:X}_write",
                                                        new MemoryCallbackDelegate(
                                                            (address, value, flags) => {
                                                                //Console.Out.WriteLine($"BizHawkGameHook_{missingLookup.Address:X}_write");
                                                                return;
                                                            }
                                                        ),
                                                        Convert.ToUInt32(address),
                                                        null));
                            }
                            if ((missingLookup.EventType & EventType.EventType_Execute) != 0)
                            {
                                Debuggable!.MemoryCallbacks!.Add(
                                    new MemoryCallback(i.BizhawkIdentifier,
                                                        MemoryCallbackType.Execute,
                                                        $"BizHawkGameHook_{missingLookup.Address:X}_execute",
                                                        new MemoryCallbackDelegate(
                                                            (address, value, flags) => {
                                                                Console.Out.WriteLine($"BizHawkGameHook_{missingLookup.Address:X}_execute");
                                                                return;
                                                            }
                                                        ),
                                                        Convert.ToUInt32(address),
                                                        null));
                            }
                            break;
                        }
                    }
                    if(!found)
                    {
                        throw new Exception($"Unsupported memory address 0x{missingLookup:X}");
                    }
                }

                //MemoryCallback memoryCallback = new MemoryCallback(scope, type, name, callback, address, mask);
                //Debuggable?.MemoryCallbacks?.Add()
            }
            GameHookEventsLookup_Accessor?.Write(0, (byte)0);
        }
        GameHookEvents_eventsSemaphore.Release();
    }

    public override void Restart()
    {
        if (MemoryDomains == null)
        {
            return;
        }

        foreach(var i in MemoryDomains)
        {
            if (i != null)
            {
                Console.Out.WriteLine($"domain: {i.Name}");
                Console.Out.WriteLine($"size: 0x{i.Size:X}");
            }
        }
        var data = new byte[SharedPlatformConstants.BIZHAWK_METADATA_PACKET_SIZE];

        data[0] = 0x00;
        data[1] = SharedPlatformConstants.BIZHAWK_INTEGRATION_VERSION;

        System = APIs?.Emulation.GetGameInfo()?.System ?? string.Empty;
        Array.Copy(Encoding.UTF8.GetBytes(System), 0, data, 2, System.Length);

        GameHookMetadata_Accessor.WriteArray(0, data, 0, data.Length);

        Platform = SharedPlatformConstants.Information.SingleOrDefault(x => x.BizhawkIdentifier == System);

        if (string.IsNullOrWhiteSpace(System))
        {
            MainLabel.Text = "No game is loaded, doing nothing.";
        }
        else if (Platform == null)
        {
            MainLabel.Text = $"{System} is not yet supported.";
        }
        else
        {
            FrameSkip = Platform.FrameSkipDefault;

            MainLabel.Text = $"Sending {System} data to GameHook...";
        }

        if(Debuggable != null && Debuggable.MemoryCallbacksAvailable())
        {
            SyncEvents();
        }
    }

    protected override void UpdateAfter()
    {
        if (MemoryDomains == null)
        {
            return;
        }

        if (APIs!.Joypad != null)
        {
            IReadOnlyDictionary<string, object>[] dictionaries = new IReadOnlyDictionary<string, object>[] {
                        APIs!.Joypad.Get(null),
                        APIs!.Joypad.GetImmediate(null),
                        APIs!.Joypad.GetWithMovie(null)
                };

            bool breakLoop = false;
            foreach (IReadOnlyDictionary<string, object> dict in dictionaries)
            {
                if (dict.Count > 0)
                {
                    if (dict.ContainsKey("Power"))
                    {
                        object hardReset;
                        bool got = dict.TryGetValue("Power", out hardReset);
                        if (got && hardReset is bool)
                        {
                            if ((bool)hardReset)
                            {
                                if (HardReset != null)
                                {
                                    HardReset();
                                }
                                breakLoop = true;
                            }
                        }
                    }

                    if (dict.ContainsKey("Reset"))
                    {
                        object softReset;
                        bool got = dict.TryGetValue("Reset", out softReset);
                        if (got && softReset is bool)
                        {
                            if ((bool)softReset)
                            {
                                if(SoftReset != null)
                                {
                                    SoftReset();
                                }
                                breakLoop = true;
                            }
                        }
                    }
                }
                if (breakLoop)
                {
                    break;
                }
            }
        }

        try
        {
            if (Platform == null) { return; }

            if (Platform.FrameSkipDefault != null)
            {
                FrameSkip -= 1;

                if (FrameSkip != 0) { return; }
            }

            if (Debuggable != null && Debuggable.MemoryCallbacksAvailable())
            {
                SyncEvents();
            }

            foreach (var entry in Platform.MemoryLayout)
            {
                if(entry.EventsOnly)
                {
                    continue;
                }
                try
                {
                    var memoryDomain = MemoryDomains?[entry.BizhawkIdentifier] ?? throw new Exception($"Memory domain not found.");

                    memoryDomain.BulkPeekByte(0x00L.RangeToExclusive(entry.Length), DataBuffer);

                    GameHookData_Accessor?.WriteArray(entry.CustomPacketTransmitPosition, DataBuffer, 0, entry.Length);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Unable to read memory domain {entry.BizhawkIdentifier}. {ex.Message}", ex);
                }
            }

            if (FrameSkip == 0)
            {
                FrameSkip = Platform.FrameSkipDefault;
            }
        }
        catch (Exception ex)
        {
            MainLabel.Text = $"Error: {ex.Message}";
        }
    }

    public EventType MemoryCallbackTypeToEventType(MemoryCallbackType memoryCallbackType)
    {
        if(memoryCallbackType == MemoryCallbackType.Read)
        {
            return EventType.EventType_Read;
        }
        else if (memoryCallbackType == MemoryCallbackType.Write)
        {
            return EventType.EventType_Write;
        }
        else if (memoryCallbackType == MemoryCallbackType.Execute)
        {
            return EventType.EventType_Execute;
        }
        else
        {
            return EventType.EventType_Undefined;
        }
    }

    public MemoryCallbackType EventTypeToMemoryCallbackType(EventType eventType)
    {
        if (eventType == EventType.EventType_Read)
        {
            return MemoryCallbackType.Read;
        }
        else if (eventType == EventType.EventType_Write)
        {
            return MemoryCallbackType.Write;
        }
        else if (eventType == EventType.EventType_Execute)
        {
            return MemoryCallbackType.Execute;
        }
        else
        {
            throw new ArgumentOutOfRangeException();
        }
    }
}

#region PlatformConstants
public static class SharedPlatformConstants
{
    public record PlatformEntry
    {
        public bool IsBigEndian { get; set; } = false;
        public bool IsLittleEndian => IsBigEndian == false;
        public string BizhawkIdentifier { get; set; } = string.Empty;
        public int? FrameSkipDefault { get; set; } = null;

        public PlatformMemoryLayoutEntry[] MemoryLayout { get; set; } = Array.Empty<PlatformMemoryLayoutEntry>();
    }

    public record PlatformMemoryLayoutEntry
    {
        public string BizhawkIdentifier { get; set; } = string.Empty;
        public int CustomPacketTransmitPosition { get; set; } = 0;
        public int Length { get; set; } = 0;

        public long PhysicalStartingAddress = 0x00;
        public long PhysicalEndingAddress => PhysicalStartingAddress + Length;
        public bool EventsOnly { get; set; } = false;
    }

    public const int BIZHAWK_INTEGRATION_VERSION = 0x00;
    public const int BIZHAWK_METADATA_PACKET_SIZE = 32;
    public const int BIZHAWK_ROM_PACKET_SIZE = 0x200000 * 2;
    public const int BIZHAWK_DATA_PACKET_SIZE = 4 * 1024 * 1024;
    public const int BIZHAWK_MAX_DATA_EVENTS = 256 * 2;
    public const int BIZHAWK_MAX_EXECUTION_EVENTS_SIZE = 256;
    public const int BIZHAWK_MAX_EVENTS_SIZE = BIZHAWK_MAX_DATA_EVENTS + BIZHAWK_MAX_EXECUTION_EVENTS_SIZE;

    public static readonly IEnumerable<PlatformEntry> Information = new List<PlatformEntry>()
    {
        new PlatformEntry
        {
            IsBigEndian = true,
            BizhawkIdentifier = "NES",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new PlatformMemoryLayoutEntry
                {
                    BizhawkIdentifier = "RAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x00,
                    Length = 0x800
                }
            }
        },
        new PlatformEntry
        {
            IsBigEndian = false,
            BizhawkIdentifier = "SNES",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new PlatformMemoryLayoutEntry
                {
                    BizhawkIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x7E0000,
                    Length = 0x10000
                }
            }
        },
        new PlatformEntry()
        {
            IsBigEndian = false,
            BizhawkIdentifier = "GB",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "ROM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x7FFF,
                    EventsOnly = true
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "CartRAM",
                    CustomPacketTransmitPosition = 0x0,
                    PhysicalStartingAddress = 0xA000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1,
                    PhysicalStartingAddress = 0xC000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "VRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0x8000,
                    Length = 0x1FFF
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "HRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0xFF80,
                    Length = 0x7E
                }
            }
        },
        new PlatformEntry()
        {
            IsBigEndian = false,
            BizhawkIdentifier = "GBC",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "ROM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x7FFF,
                    EventsOnly = true
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "CartRAM",
                    CustomPacketTransmitPosition = 0x0,
                    PhysicalStartingAddress = 0xA000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1,
                    PhysicalStartingAddress = 0xC000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "VRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0x8000,
                    Length = 0x1FFF
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "HRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0xFF80,
                    Length = 0x7E
                }
            }
        },
        new PlatformEntry()
        {
            IsBigEndian = false,
            BizhawkIdentifier = "SGB",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "ROM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x7FFF,
                    EventsOnly = true
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "CartRAM",
                    CustomPacketTransmitPosition = 0x0,
                    PhysicalStartingAddress = 0xA000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1,
                    PhysicalStartingAddress = 0xC000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "VRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0x8000,
                    Length = 0x1FFF
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "HRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0xFF80,
                    Length = 0x7E
                }
            }
        },
        new PlatformEntry
        {
            IsBigEndian = true,
            BizhawkIdentifier = "GBA",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new PlatformMemoryLayoutEntry
                {
                    BizhawkIdentifier = "EWRAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x02000000,
                    Length = 0x00040000
                },
                new PlatformMemoryLayoutEntry
                {
                    BizhawkIdentifier = "IWRAM",
                    CustomPacketTransmitPosition = 0x00040000 + 1,
                    PhysicalStartingAddress = 0x03000000,
                    Length = 0x00008000
                }
            }
        },
        new PlatformEntry()
        {
            IsBigEndian = true,
            BizhawkIdentifier = "NDS",
            FrameSkipDefault = 15,
            MemoryLayout = new PlatformMemoryLayoutEntry[] {
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "Main RAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x2000000,
                    Length = 0x400000
                }
            }
        }
    };
}
#endregion