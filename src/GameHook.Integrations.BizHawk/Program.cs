using BizHawk.Client.Common;
using BizHawk.Client.EmuHawk;
using BizHawk.Common;
using BizHawk.Common.StringExtensions;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Components.M68000;
using BizHawk.Emulation.Cores.Nintendo.BSNES;
using BizHawk.Emulation.Cores.Nintendo.Gameboy;
using BizHawk.Emulation.Cores.Nintendo.GBHawk;
using BizHawk.Emulation.Cores.Nintendo.GBHawkLink;
using BizHawk.Emulation.Cores.Nintendo.GBHawkLink3x;
using BizHawk.Emulation.Cores.Nintendo.GBHawkLink4x;
using BizHawk.Emulation.Cores.Nintendo.Sameboy;
using BizHawk.Emulation.Cores.Nintendo.SNES;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Drawing;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using static BizHawk.Emulation.Cores.Computers.AmstradCPC.CRCT_6845;
using static GameHookIntegration.SharedPlatformConstants.PlatformEntry;
using static GameHookIntegration.SharedPlatformConstants.PlatformMapper;

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

        public string getRegisterString()
        {
            byte[] bytes = BitConverter.GetBytes(Register);
            string register = Encoding.ASCII.GetString(bytes);
            register = register.TrimEnd('\0');

            return register;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EventAddress
    {
        const int MaxRegisterOverride = 6;
        const int MaxBitsLength = 64 * 8 / 8;

        public bool Active;
        public long Address;
        public ushort Bank;
        public EventType EventType;
        public EventAddressRegisterOverride EventAddressRegisterOverride0;
        public EventAddressRegisterOverride EventAddressRegisterOverride1;
        public EventAddressRegisterOverride EventAddressRegisterOverride2;
        public EventAddressRegisterOverride EventAddressRegisterOverride3;
        public EventAddressRegisterOverride EventAddressRegisterOverride4;
        public EventAddressRegisterOverride EventAddressRegisterOverride5;
        public UInt64 Bits0;
        public UInt64 Bits1;
        public UInt64 Bits2;
        public UInt64 Bits3;
        public UInt64 Bits4;
        public UInt64 Bits5;
        public UInt64 Bits6;
        public UInt64 Bits7;
        public int Length;
        public int Size;

        public EventAddress() : this(false, 0x00, ushort.MaxValue, EventType.EventType_Undefined, new List<EventAddressRegisterOverride> { }, null, 1, 0)
        {
        }
        public EventAddress(bool active, long address, ushort bank, EventType eventType, IEnumerable<EventAddressRegisterOverride> eventAddressRegisterOverrides, string? bits, int length, int size)
        {
            EventAddressRegisterOverride0 = new EventAddressRegisterOverride();
            EventAddressRegisterOverride1 = new EventAddressRegisterOverride();
            EventAddressRegisterOverride2 = new EventAddressRegisterOverride();
            EventAddressRegisterOverride3 = new EventAddressRegisterOverride();
            EventAddressRegisterOverride4 = new EventAddressRegisterOverride();
            EventAddressRegisterOverride5 = new EventAddressRegisterOverride();
            Bits0 = 0;
            Bits1 = 0;
            Bits2 = 0;
            Bits3 = 0;
            Bits4 = 0;
            Bits5 = 0;
            Bits6 = 0;
            Bits7 = 0;
            Length = length;
            Size = (size > 0) ? size : 1;

            Active = active;
            Address = address;
            Bank = bank;
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
            if (bits == null)
            {
                // noop, bits is already initialized to 0
            }
            else if (bits.Count() < 0 || bits.Count() > MaxBitsLength)
            {
                throw new ArgumentOutOfRangeException(nameof(bits));
            }
            else
            {
                // TODO: this is an UGLY kludge to get arround pointers, and fixed size arrays
                // requiring "unsafe", which I'm not sure will break BizHawk or not or cause
                // other headaches. In c# 8/.net 2012 then [System.Runtime.CompilerServices.InlineArray()]
                // attribute can be used to bypass.
                List<byte> bitsBytes = new List<byte>(Encoding.ASCII.GetBytes(bits));
                if (bitsBytes == null)
                {
                    bitsBytes = new List<byte> { };
                }
                while (bitsBytes.Count < MaxBitsLength)
                {
                    bitsBytes.Add(0);
                }
                byte[] bitsBytsArray = bitsBytes.ToArray();
                Bits0 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 0).Take(sizeof(UInt64)).ToArray(), 0);
                Bits1 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 1).Take(sizeof(UInt64)).ToArray(), 0);
                Bits2 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 2).Take(sizeof(UInt64)).ToArray(), 0);
                Bits3 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 3).Take(sizeof(UInt64)).ToArray(), 0);
                Bits4 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 4).Take(sizeof(UInt64)).ToArray(), 0);
                Bits5 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 5).Take(sizeof(UInt64)).ToArray(), 0);
                Bits6 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 6).Take(sizeof(UInt64)).ToArray(), 0);
                Bits7 = BitConverter.ToUInt64(bitsBytsArray.Skip(sizeof(UInt64) * 7).Take(sizeof(UInt64)).ToArray(), 0);
            }
        }

        public EventAddressRegisterOverride[] getOverrides()
        {
            List<EventAddressRegisterOverride> overrides = new List<EventAddressRegisterOverride>();
            foreach (var i in new List<EventAddressRegisterOverride> {EventAddressRegisterOverride0,
                        EventAddressRegisterOverride1,
                        EventAddressRegisterOverride2,
                        EventAddressRegisterOverride3,
                        EventAddressRegisterOverride4,
                        EventAddressRegisterOverride5})
            {
                if ((i.Register & 0xFFUL) != 0)
                {
                    overrides.Add(i);
                }
            }

            return overrides.ToArray();
        }

        public string getBitsString()
        {
            IEnumerable<byte> bytes = BitConverter.GetBytes(Bits0).ToList();
            bytes = bytes.Concat(BitConverter.GetBytes(Bits1).ToList());
            bytes = bytes.Concat(BitConverter.GetBytes(Bits2).ToList());
            bytes = bytes.Concat(BitConverter.GetBytes(Bits3).ToList());
            bytes = bytes.Concat(BitConverter.GetBytes(Bits4).ToList());
            bytes = bytes.Concat(BitConverter.GetBytes(Bits5).ToList());
            bytes = bytes.Concat(BitConverter.GetBytes(Bits6).ToList());
            bytes = bytes.Concat(BitConverter.GetBytes(Bits7).ToList());

            string bits = Encoding.ASCII.GetString(bytes.ToArray());
            bits = bits.TrimEnd('\0');

            return bits;
        }
    }

    public ApiContainer? APIs { get; set; }

    [OptionalService]
    public IMemoryDomains? MemoryDomains { get; set; }

    [OptionalService]
    public IDebuggable? Debuggable { get; set; }

    [OptionalService]
    public IBoardInfo? BoardInfo { get; set; }

    [OptionalService]
    public IEmulator? Emulator { get; set; }

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
                    if(!scopeToEntry.ContainsKey(i.BizhawkAlternateIdentifier))
                        scopeToEntry.Add(i.BizhawkAlternateIdentifier, i);
                    if (!scopeToEntry.ContainsKey(i.BizhawkIdentifier))
                        scopeToEntry.Add(i.BizhawkIdentifier, i);
                }
            }
            EventAddress[] eventAddressLookups = new EventAddress[SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE];
            GameHookEventsLookup_Accessor?.ReadArray(1, eventAddressLookups, 0, SharedPlatformConstants.BIZHAWK_MAX_EVENTS_SIZE);

            if (Platform == null)
            {
                throw new Exception("Callbacks aren't supported yet on this platform.");
            }
            if (Platform.GetMapper == null)
            {
                throw new Exception("Callbacks aren't supported yet on this platform.");
            }
            SharedPlatformConstants.PlatformMapper Mapper = Platform.GetMapper(Platform, Emulator, MemoryDomains, Debuggable, BoardInfo?.BoardName);
            if (Mapper.GetBankFunctionAndCallbackDomain == null)
            {
                throw new Exception("Callbacks aren't supported yet on this platform.");
            }
            if(Debuggable == null)
            {
                throw new Exception("Callbacks aren't supported yet on this platform.");
            }

            // clear all current breakpoints.
            Console.WriteLine($"Clearing events.");
            Debuggable?.MemoryCallbacks?.Clear();
            HardReset = null;
            SoftReset = null;

            // get list of unique addressess to break on.
            List<long> breakAddresses = new List<long>();
            foreach(var x in eventAddressLookups)
            {
                if(!x.Active)
                {
                    continue;
                } 
                else if((x.EventType & ~(EventType.EventType_HardReset | EventType.EventType_SoftReset)) == 0)
                {
                    continue;
                }
                else
                {
                    int length = (x.Length > 0) ? x.Length : 1;
                    int size = (x.Size > 0) ? x.Size : 1;
                    long address = x.Address;
                    for (var i = address; i < address + (length * size); i++)
                    {
                        breakAddresses.Add(i);
                    }
                    breakAddresses = breakAddresses.Distinct().ToList();
                    breakAddresses.Sort();
                }
            }
            Console.Out.WriteLine("breakAddresses:");
            foreach (var address in breakAddresses)
            {
                Console.Out.WriteLine(address);
            }
            // group each events by address.
            Dictionary<long, Dictionary<MemoryCallbackType, Dictionary<ushort, List<Tuple<EventAddress, Tuple<string, int>[]>>>>> breakAddressessTypeBankEventAddressess = new Dictionary<long, Dictionary<MemoryCallbackType, Dictionary<ushort, List<Tuple<EventAddress, Tuple<string, int>[]>>>>>();
            foreach(var address in breakAddresses)
            {
                foreach(var x in eventAddressLookups)
                {
                    if (!x.Active)
                    {
                        continue;
                    }
                    else if ((x.EventType & ~(EventType.EventType_HardReset | EventType.EventType_SoftReset)) == 0)
                    {
                        continue;
                    }
                    int length = (x.Length > 0) ? x.Length : 1;
                    int size = (x.Size > 0) ? x.Size : 1;
                    long eventAddress = x.Address;
                    if(eventAddress <= address && eventAddress + (length * size) > address)
                    {
                        if (!breakAddressessTypeBankEventAddressess.ContainsKey(address))
                        {
                            breakAddressessTypeBankEventAddressess.Add(address, new Dictionary<MemoryCallbackType, Dictionary<ushort, List<Tuple<EventAddress, Tuple<string, int>[]>>>>());
                        }
                        if ((x.EventType & EventType.EventType_Read) != 0)
                        {
                            if (!breakAddressessTypeBankEventAddressess[address].ContainsKey(MemoryCallbackType.Read))
                            {
                                breakAddressessTypeBankEventAddressess[address].Add(MemoryCallbackType.Read, new Dictionary<ushort, List<Tuple<EventAddress, Tuple<string, int>[]>>>());
                            }
                            if(!breakAddressessTypeBankEventAddressess[address][MemoryCallbackType.Read].ContainsKey(x.Bank))
                            {
                                breakAddressessTypeBankEventAddressess[address][MemoryCallbackType.Read].Add(x.Bank, new List<Tuple<EventAddress, Tuple<string, int>[]>>());
                            }
                            var untransformedOverrides = x.getOverrides();
                            Tuple<string, int>[] overrides = untransformedOverrides.Select(x => new Tuple<string, int>(x.getRegisterString(), Convert.ToInt32(x.Value))).ToArray();
                            breakAddressessTypeBankEventAddressess[address][MemoryCallbackType.Read][x.Bank].Add(new Tuple<EventAddress, Tuple<string, int>[]>(x, overrides));
                        }
                        if ((x.EventType & EventType.EventType_Write) != 0)
                        {
                            if (!breakAddressessTypeBankEventAddressess[address].ContainsKey(MemoryCallbackType.Write))
                            {
                                breakAddressessTypeBankEventAddressess[address].Add(MemoryCallbackType.Write, new Dictionary<ushort, List<Tuple<EventAddress, Tuple<string, int>[]>>>());
                            }
                            if (!breakAddressessTypeBankEventAddressess[address][MemoryCallbackType.Write].ContainsKey(x.Bank))
                            {
                                breakAddressessTypeBankEventAddressess[address][MemoryCallbackType.Write].Add(x.Bank, new List<Tuple<EventAddress, Tuple<string, int>[]>>());
                            }
                            var untransformedOverrides = x.getOverrides();
                            Tuple<string, int>[] overrides = untransformedOverrides.Select(x => new Tuple<string, int>(x.getRegisterString(), Convert.ToInt32(x.Value))).ToArray();
                            breakAddressessTypeBankEventAddressess[address][MemoryCallbackType.Write][x.Bank].Add(new Tuple<EventAddress, Tuple<string, int>[]>(x, overrides));
                        }
                        if ((x.EventType & EventType.EventType_Execute) != 0)
                        {
                            if (!breakAddressessTypeBankEventAddressess[address].ContainsKey(MemoryCallbackType.Execute))
                            {
                                breakAddressessTypeBankEventAddressess[address].Add(MemoryCallbackType.Execute, new Dictionary<ushort, List<Tuple<EventAddress, Tuple<string, int>[]>>>());
                            }
                            if (!breakAddressessTypeBankEventAddressess[address][MemoryCallbackType.Execute].ContainsKey(x.Bank))
                            {
                                breakAddressessTypeBankEventAddressess[address][MemoryCallbackType.Execute].Add(x.Bank, new List<Tuple<EventAddress, Tuple<string, int>[]>>());
                            }
                            var untransformedOverrides = x.getOverrides();
                            Tuple<string, int>[] overrides = untransformedOverrides.Select(x => new Tuple<string, int>(x.getRegisterString(), Convert.ToInt32(x.Value))).ToArray();
                            breakAddressessTypeBankEventAddressess[address][MemoryCallbackType.Execute][x.Bank].Add(new Tuple<EventAddress, Tuple<string, int>[]>(x, overrides));
                        }
                    }
                }
            }
            // setup callbacks for addressess.
            foreach (var breakAddressTypeBankEventAddress in breakAddressessTypeBankEventAddressess)
            {
                bool found = false;
                var address = breakAddressTypeBankEventAddress.Key;

                string identifierDomain = "";
                long domainAddress = 0;
                Tuple<GetMapperBankDelegate?, string> GetMapperBankAndDomain = Mapper.GetBankFunctionAndCallbackDomain(Convert.ToUInt32(address));
                string domain = GetMapperBankAndDomain.Item2;
                GetMapperBankDelegate? GetBank = GetMapperBankAndDomain.Item1;
                if (GetBank == null)
                {
                    GetBank = () =>
                    {
                        return 0;
                    };
                }
                foreach (SharedPlatformConstants.PlatformMemoryLayoutEntry i in Platform.MemoryLayout)
                {
                    if (i != null && (i.BizhawkIdentifier == domain || i.BizhawkAlternateIdentifier == domain))
                    {
                        identifierDomain = Debuggable!.MemoryCallbacks.AvailableScopes.Contains(i.BizhawkIdentifier) ? i.BizhawkIdentifier : i.BizhawkAlternateIdentifier;
                        found = true;
                        domainAddress = address - i.PhysicalStartingAddress;
                    }
                }
                if (!found)
                {
                    throw new Exception($"Unsupported memory address 0x{address:X}");
                }
                foreach (var eventTypeBankEventAddresses in breakAddressTypeBankEventAddress.Value)
                {
                    var eventType = eventTypeBankEventAddresses.Key;
                    var eventBankEventAddress = eventTypeBankEventAddresses.Value;

                    Console.WriteLine($"Adding event --> {address:X}, {eventType}: BizHawkGameHook_{address:X}_{eventType}.");
                    Debuggable!.MemoryCallbacks!.Add(
                        new MemoryCallback(identifierDomain,
                                            eventType,
                                            $"BizHawkGameHook_{address:X}_{eventType}",
                                            new MemoryCallbackDelegate(
                                                (address, value, flags) =>
                                                {
                                                    ushort[] banks = new ushort[] { ushort.MaxValue, Convert.ToUInt16(GetBank()) };
                                                    foreach(var bank in banks)
                                                    {
                                                        if (eventBankEventAddress.ContainsKey(bank))
                                                        {
                                                            foreach (var callback in eventBankEventAddress[bank])
                                                            {
                                                                var eventAddress = callback.Item1;
                                                                var overrides = callback.Item2;
                                                                if (overrides != null)
                                                                {
                                                                    foreach (var i in overrides)
                                                                    {
                                                                        Console.Out.WriteLine($"Overriding: {i.Item1} with 0x{i.Item2:X}");
                                                                        Debuggable.SetCpuRegister(i.Item1, i.Item2);
                                                                    }
                                                                }
                                                                if (eventType == MemoryCallbackType.Execute)
                                                                {
                                                                    Console.Out.WriteLine($"BizHawkGameHook_{address:X}_{eventType}, bank: {bank:X}");
                                                                }
                                                            }
                                                        }
                                                    }
                                                    return;
                                                }
                                            ),
                                            Convert.ToUInt32(address),
                                            null));
                }
            }
            // setup hard soft.
            foreach (var x in eventAddressLookups)
            {
                 if ((x.EventType & EventType.EventType_HardReset) != 0)
                 {
                     HardReset = new HardResetCallbackDelegate(() =>
                     {
                         Console.Out.WriteLine($"_hardreset");
                         return;
                     });
                 }
                 if ((x.EventType & EventType.EventType_SoftReset) != 0)
                 {
                     SoftReset = new SoftResetCallbackDelegate(() =>
                     {
                         Console.Out.WriteLine($"_softreset");
                         return;
                     });
                 }
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

        Console.Out.WriteLine("Callback domains:");
        if(Debuggable != null && Debuggable.MemoryCallbacks != null && Debuggable.MemoryCallbacks.AvailableScopes != null)
        {
            foreach(var i in Debuggable.MemoryCallbacks.AvailableScopes)
            {
                Console.Out.WriteLine($"scope: {i.ToString()}");
            }
        }
        foreach(var i in MemoryDomains)
        {
            if (i != null)
            {
                Console.Out.WriteLine($"domain: {i.Name}");
                Console.Out.WriteLine($"size: 0x{i.Size:X}");
            }
        }
        foreach(var i in Debuggable.GetCpuFlagsAndRegisters())
        {
            Console.Out.WriteLine(i.Key + ":" + i.Value.Value.ToString());
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

        if (BoardInfo != null && BoardInfo.BoardName != null && Platform != null &&
            Platform.GetMapper != null)
        {
            var boardName = BoardInfo.BoardName;
            var mapper = Platform.GetMapper(Platform, Emulator, MemoryDomains, Debuggable, boardName);

            Console.Out.WriteLine($"BoardInfo: {boardName}");
            if (mapper != null && mapper.GetMapperName != null)
            {
                Console.Out.WriteLine($"Mapper: {mapper.GetMapperName()}");
            }
            else
            {
                Console.Out.WriteLine($"Mapper: null");
            }

            if (mapper != null)
            {
                if (mapper.InitState != null)
                {
                    mapper.InitState(mapper, Platform);
                }
                if (mapper.InitMapperDetection != null)
                {
                    mapper.InitMapperDetection();
                }
            }
        }

        if (Debuggable != null && Debuggable.MemoryCallbacksAvailable())
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
                    var memoryDomain = MemoryDomains?[entry.BizhawkIdentifier] ?? MemoryDomains?[entry.BizhawkAlternateIdentifier] ?? throw new Exception($"Memory domain not found.");

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
    public delegate PlatformMapper GetMapperDelegate(PlatformEntry? Platform, IEmulator? emulator, IMemoryDomains? memoryDomains, IDebuggable? debuggable, string? boardName);

    public static GetMapperDelegate GBMapperDelegate = (platform, emulator, memoryDomains, debuggable, boardName) =>
    {
        if(platform == null)
        {
            throw new Exception("unexpected null platform");
        }
        if (emulator != null && emulator is Gameboy)
        {
            string mapperName = "UNKNOWN";
            if (debuggable != null)
            {
                switch (boardName?.Trim('\0'))
                {
                    case "none":
                    case "NULL":
                    case "NROM":
                    case "Plain ROM":
                    case "NULL [RAM]":
                    case "Plain ROM+RAM":
                    case "NULL [RAM,battery]":
                    case "Plain ROM+RAM+BATTERY":
                    case "MBC0":
                        mapperName = "No Mapper";
                        break;
                    case "MBC1":
                    case "MBC1 ROM":
                    case "MBC1 [RAM]":
                    case "MBC1 ROM+RAM":
                    case "MBC1 [RAM,battery]":
                    case "MBC1 ROM+RAM+BATTERY":
                        mapperName = "MBC1";
                        break;
                    case "MBC1M":
                    case "MBC1M [RAM]":
                    case "MBC1M [RAM,battery]":
                        mapperName = "MBC1M";
                        break;
                    case "MBC2":
                    case "MBC2 ROM":
                    case "MBC2 [battery]":
                    case "MBC2 ROM+BATTERY":
                        mapperName = "MBC2";
                        break;
                    case "MBC3":
                    case "MBC3 ROM":
                    case "MBC3 [RTC,battery]":
                    case "MBC3 ROM+TIMER+BATTERY":
                    case "MBC3 [RAM,RTC,battery]":
                    case "MBC3 ROM+TIMER+RAM+BATTERY":
                    case "MBC3 [RAM]":
                    case "MBC3 ROM+RAM":
                    case "MBC3 [RAM,battery]":
                    case "MBC3 ROM+RAM+BATTERY":
                        mapperName = "MBC3";
                        break;
                    case "MBC4":
                    case "MBC4 [RAM]":
                    case "MBC4 [RAM,battery]":
                        mapperName = "MBC4";
                        break;
                    case "MBC5":
                    case "MBC5 ROM":
                    case "MBC5 [RAM]":
                    case "MBC5 ROM+RAM":
                    case "MBC5 [RAM,battery]":
                    case "MBC5 ROM+RAM+BATTERY":
                    case "MBC5 [rumble]":
                    case "MBC5 ROM+RUMBLE":
                    case "MBC5 [RAM,rumble]":
                    case "MBC5 ROM+RUMBLE+RAM":
                    case "MBC5 [RAM,rumble,battery]":
                    case "MBC5 ROM+RUMBLE+RAM+BATTERY":
                        mapperName = "MBC5";
                        break;
                    case "MBC6":
                        mapperName = "MBC6";
                        break;
                    case "MBC7":
                    case "MBC7 ROM+ACCEL+EEPROM":
                        mapperName = "MBC7";
                        break;
                    case "MMM01":
                    case "MMM01 [RAM]":
                    case "MMM01 [RAM,battery]":
                        mapperName = "MMM01";
                        break;
                    case "M161":
                        mapperName = "M161";
                        break;
                    case "Wisdom Tree":
                    case "Wtree":
                        mapperName = "Wisdom Tree";
                        break;
                    case "Pocket Camera":
                    case "Pocket Camera ROM+RAM+BATTERY":
                    case "CAM":
                    case "CAMERA":
                        mapperName = "Pocket Camera";
                        break;
                    case "Bandai TAMA5":
                    case "TAMA5":
                    case "TAMA":
                        mapperName = "Bandai TAMA5";
                        break;
                    case "HuC3":
                    case "HuC3 ROM+RAM+BATTERY":
                        mapperName = "HuC3";
                        break;
                    case "HuC1":
                    case "HuC1 [RAM,battery]":
                    case "HuC1 ROM+RAM+BATTERY":
                        mapperName = "HuC1";
                        break;
                    case "Schn1":
                        mapperName = "Schn1";
                        break;
                    case "Schn2":
                        mapperName = "Schn2";
                        break;
                    default:
                        throw new Exception($"NYI \"{boardName?.Trim('\0')}\"");
                }
                return new PlatformMapper
                {
                    GetMapperName = () => { return mapperName; },
                    InitMapperDetection = () =>
                    {
                    },
                    GetBankFunctionAndCallbackDomain = (address) =>
                    {
                        PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                        if(platformMemoryLayoutEntry == null)
                        {
                            throw new Exception("unsupported address");
                        }
                        GetMapperBankDelegate? bankDelegate = null;
                        switch(platformMemoryLayoutEntry.BizhawkIdentifier)
                        {
                            case "SGB CARTROM":
                            case "ROM":
                                if(address <= 0x3FFF)
                                {
                                    bankDelegate = () => { return Convert.ToInt32(debuggable.GetCpuFlagsAndRegisters()["ROM0 BANK"].Value); };
                                }
                                else
                                {
                                    bankDelegate = () => { return Convert.ToInt32(debuggable.GetCpuFlagsAndRegisters()["ROMX BANK"].Value); };
                                }
                                break;
                            case "VRAM":
                                bankDelegate = () => { return Convert.ToInt32(debuggable.GetCpuFlagsAndRegisters()["VRAM BANK"].Value); };
                                break;
                            case "SGB CARTRAM":
                            case "CartRAM":
                                bankDelegate = () => { return Convert.ToInt32(debuggable.GetCpuFlagsAndRegisters()["SRAM BANK"].Value); };
                                break;
                            case "SGB WRAM":
                            case "WRAM":
                                bankDelegate = () => { return Convert.ToInt32(debuggable.GetCpuFlagsAndRegisters()["WRAM BANK"].Value); };
                                break;
                            default:
                                bankDelegate = () => { return 255; };
                                break;
                        }
                        return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                    }
                };
            }
            else
            {
                throw new Exception("NYI");
            }
        }
        else if (emulator != null && (emulator is Sameboy))
        {
            //ROM0 BANK:0
            //ROMX BANK:1
            //VRAM BANK:0
            //SRAM BANK:0
            //WRAM BANK:1
            string mapperName = "UNKNOWN";
            if (debuggable != null)
            {
                switch (boardName?.Trim('\0'))
                {
                    case "none":
                    case "NULL":
                    case "NROM":
                    case "Plain ROM":
                    case "NULL [RAM]":
                    case "Plain ROM+RAM":
                    case "NULL [RAM,battery]":
                    case "Plain ROM+RAM+BATTERY":
                    case "MBC0":
                        mapperName = "No Mapper";
                        break;
                    case "MBC1":
                    case "MBC1 ROM":
                    case "MBC1 [RAM]":
                    case "MBC1 ROM+RAM":
                    case "MBC1 [RAM,battery]":
                    case "MBC1 ROM+RAM+BATTERY":
                        mapperName = "MBC1";
                        break;
                    case "MBC1M":
                    case "MBC1M [RAM]":
                    case "MBC1M [RAM,battery]":
                        mapperName = "MBC1M";
                        break;
                    case "MBC2":
                    case "MBC2 ROM":
                    case "MBC2 [battery]":
                    case "MBC2 ROM+BATTERY":
                        mapperName = "MBC2";
                        break;
                    case "MBC3":
                    case "MBC3 ROM":
                    case "MBC3 [RTC,battery]":
                    case "MBC3 ROM+TIMER+BATTERY":
                    case "MBC3 [RAM,RTC,battery]":
                    case "MBC3 ROM+TIMER+RAM+BATTERY":
                    case "MBC3 [RAM]":
                    case "MBC3 ROM+RAM":
                    case "MBC3 [RAM,battery]":
                    case "MBC3 ROM+RAM+BATTERY":
                        mapperName = "MBC3";
                        break;
                    case "MBC4":
                    case "MBC4 [RAM]":
                    case "MBC4 [RAM,battery]":
                        mapperName = "MBC4";
                        break;
                    case "MBC5":
                    case "MBC5 ROM":
                    case "MBC5 [RAM]":
                    case "MBC5 ROM+RAM":
                    case "MBC5 [RAM,battery]":
                    case "MBC5 ROM+RAM+BATTERY":
                    case "MBC5 [rumble]":
                    case "MBC5 ROM+RUMBLE":
                    case "MBC5 [RAM,rumble]":
                    case "MBC5 ROM+RUMBLE+RAM":
                    case "MBC5 [RAM,rumble,battery]":
                    case "MBC5 ROM+RUMBLE+RAM+BATTERY":
                        mapperName = "MBC5";
                        break;
                    case "MBC6":
                        mapperName = "MBC6";
                        break;
                    case "MBC7":
                    case "MBC7 ROM+ACCEL+EEPROM":
                        mapperName = "MBC7";
                        break;
                    case "MMM01":
                    case "MMM01 [RAM]":
                    case "MMM01 [RAM,battery]":
                        mapperName = "MMM01";
                        break;
                    case "M161":
                        mapperName = "M161";
                        break;
                    case "Wisdom Tree":
                    case "Wtree":
                        mapperName = "Wisdom Tree";
                        break;
                    case "Pocket Camera":
                    case "Pocket Camera ROM+RAM+BATTERY":
                    case "CAM":
                    case "CAMERA":
                        mapperName = "Pocket Camera";
                        break;
                    case "Bandai TAMA5":
                    case "TAMA5":
                    case "TAMA":
                        mapperName = "Bandai TAMA5";
                        break;
                    case "HuC3":
                    case "HuC3 ROM+RAM+BATTERY":
                        mapperName = "HuC3";
                        break;
                    case "HuC1":
                    case "HuC1 [RAM,battery]":
                    case "HuC1 ROM+RAM+BATTERY":
                        mapperName = "HuC1";
                        break;
                    case "Schn1":
                        mapperName = "Schn1";
                        break;
                    case "Schn2":
                        mapperName = "Schn2";
                        break;
                    default:
                        throw new Exception($"NYI \"{boardName?.Trim('\0')}\"");
                }
                return new PlatformMapper
                {
                    GetMapperName = () => { return mapperName; },
                    InitMapperDetection = () =>
                    {
                    },
                    GetBankFunctionAndCallbackDomain = (address) =>
                    {
                        PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                        if (platformMemoryLayoutEntry == null)
                        {
                            throw new Exception("unsupported address");
                        }
                        GetMapperBankDelegate? bankDelegate = null;
                        switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                        {
                            case "ROM":
                                if (address <= 0x3FFF)
                                {
                                    bankDelegate = () => { return Convert.ToInt32(debuggable.GetCpuFlagsAndRegisters()["ROM0 BANK"].Value); };
                                }
                                else
                                {
                                    bankDelegate = () => { return Convert.ToInt32(debuggable.GetCpuFlagsAndRegisters()["ROMX BANK"].Value); };
                                }
                                break;
                            case "VRAM":
                                bankDelegate = () => { return Convert.ToInt32(debuggable.GetCpuFlagsAndRegisters()["VRAM BANK"].Value); };
                                break;
                            case "CartRAM":
                                bankDelegate = () => { return Convert.ToInt32(debuggable.GetCpuFlagsAndRegisters()["SRAM BANK"].Value); };
                                break;
                            case "WRAM":
                                bankDelegate = () => { return Convert.ToInt32(debuggable.GetCpuFlagsAndRegisters()["WRAM BANK"].Value); };
                                break;
                            default:
                                bankDelegate = () => { return 0; };
                                break;
                        }
                        return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                    }
                };
            }
            else
            {
                throw new Exception("NYI");
            }
        }
        else if (emulator != null && emulator is GBHawk)
        {
            var mapper = ((GBHawk)emulator).mapper;
            if (mapper != null)
            {
                switch (mapper)
                {
                    case MapperCamera cameraMapper:
                        Console.WriteLine("camera: rom bank: " + cameraMapper.ROM_bank.ToString()
                            + " ram bank: " + cameraMapper.RAM_bank.ToString()
                            + " ram enabled: " + cameraMapper.RAM_enable.ToString());
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "Pocket Camera"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    case "ROM":
                                        if (address <= 0x3FFF)
                                        {
                                            bankDelegate = () => { return 0; };
                                        }
                                        else
                                        {
                                            bankDelegate = () => { return cameraMapper.ROM_bank; };
                                        }
                                        break;
                                    case "CartRAM":
                                        bankDelegate = () => { return cameraMapper.RAM_bank; };
                                        break;
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    case MapperHuC1 huc1Mapper:
                        Console.WriteLine("camera: rom bank: " + huc1Mapper.ROM_bank.ToString()
                            + " ram bank: " + huc1Mapper.RAM_bank.ToString()
                            + " ram enabled: " + huc1Mapper.RAM_enable.ToString());
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "HuC1"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    case "ROM":
                                        if (address <= 0x3FFF)
                                        {
                                            bankDelegate = () => { return 0; };
                                        }
                                        else
                                        {
                                            bankDelegate = () => { return huc1Mapper.ROM_bank; };
                                        }
                                        break;
                                    case "CartRAM":
                                        bankDelegate = () => { return huc1Mapper.RAM_bank; };
                                        break;
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    case MapperHuC3 huc3Mapper:
                        Console.WriteLine("camera: rom bank: " + huc3Mapper.ROM_bank.ToString()
                            + " ram bank: " + huc3Mapper.RAM_bank.ToString()
                            + " ram enabled: " + huc3Mapper.RAM_enable.ToString());
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "HuC3"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    case "ROM":
                                        if (address <= 0x3FFF)
                                        {
                                            bankDelegate = () => { return 0; };
                                        }
                                        else
                                        {
                                            bankDelegate = () => { return huc3Mapper.ROM_bank; };
                                        }
                                        break;
                                    case "CartRAM":
                                        bankDelegate = () => { return huc3Mapper.RAM_bank; };
                                        break;
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    case MapperMBC1 mbc1Mapper:
                        Console.WriteLine("camera: rom bank: " + mbc1Mapper.ROM_bank.ToString()
                            + " ram bank: " + mbc1Mapper.RAM_bank.ToString()
                            + " ram enabled: " + mbc1Mapper.RAM_enable.ToString());
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "MBC1"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    case "ROM":
                                        if (address <= 0x3FFF)
                                        {
                                            bankDelegate = () => { return 0; };
                                        }
                                        else
                                        {
                                            bankDelegate = () => { return mbc1Mapper.ROM_bank; };
                                        }
                                        break;
                                    case "CartRAM":
                                        bankDelegate = () => { return mbc1Mapper.RAM_bank; };
                                        break;
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    case MapperMBC1Multi mbc1mMapper:
                        Console.WriteLine("camera: rom bank: " + mbc1mMapper.ROM_bank.ToString()
                            + " ram bank: " + mbc1mMapper.RAM_bank.ToString()
                            + " ram enabled: " + mbc1mMapper.RAM_enable.ToString());
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "MBC1M"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    case "ROM":
                                        if (address <= 0x3FFF)
                                        {
                                            bankDelegate = () => { return 0; };
                                        }
                                        else
                                        {
                                            bankDelegate = () => { return mbc1mMapper.ROM_bank; };
                                        }
                                        break;
                                    case "CartRAM":
                                        bankDelegate = () => { return mbc1mMapper.RAM_bank; };
                                        break;
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    case MapperMBC2 mbc2Mapper:
                        Console.WriteLine("camera: rom bank: " + mbc2Mapper.ROM_bank.ToString()
                            + " ram bank: " + mbc2Mapper.RAM_bank.ToString()
                            + " ram enabled: " + mbc2Mapper.RAM_enable.ToString());
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "MBC2"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    case "ROM":
                                        if (address <= 0x3FFF)
                                        {
                                            bankDelegate = () => { return 0; };
                                        }
                                        else
                                        {
                                            bankDelegate = () => { return mbc2Mapper.ROM_bank; };
                                        }
                                        break;
                                    case "CartRAM":
                                        bankDelegate = () => { return mbc2Mapper.RAM_bank; };
                                        break;
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    case MapperMBC3 mbc3Mapper:
                        Console.WriteLine("camera: rom bank: " + mbc3Mapper.ROM_bank.ToString()
                            + " ram bank: " + mbc3Mapper.RAM_bank.ToString()
                            + " ram enabled: " + mbc3Mapper.RAM_enable.ToString());
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "MBC3"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    case "ROM":
                                        if (address <= 0x3FFF)
                                        {
                                            bankDelegate = () => { return 0; };
                                        }
                                        else
                                        {
                                            bankDelegate = () => { return mbc3Mapper.ROM_bank; };
                                        }
                                        break;
                                    case "CartRAM":
                                        bankDelegate = () => { return mbc3Mapper.RAM_bank; };
                                        break;
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    case MapperMBC5 mbc5Mapper:
                        Console.WriteLine("camera: rom bank: " + mbc5Mapper.ROM_bank.ToString()
                            + " ram bank: " + mbc5Mapper.RAM_bank.ToString()
                            + " ram enabled: " + mbc5Mapper.RAM_enable.ToString());
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "MBC5"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    case "ROM":
                                        if (address <= 0x3FFF)
                                        {
                                            bankDelegate = () => { return 0; };
                                        }
                                        else
                                        {
                                            bankDelegate = () => { return mbc5Mapper.ROM_bank; };
                                        }
                                        break;
                                    case "CartRAM":
                                        bankDelegate = () => { return mbc5Mapper.RAM_bank; };
                                        break;
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    case MapperMBC7 mbc7Mapper:
                        Console.WriteLine("camera: rom bank: " + mbc7Mapper.ROM_bank.ToString()
                            + " ram 1 enabled: " + mbc7Mapper.RAM_enable_1.ToString()
                            + " ram 2 enabled: " + mbc7Mapper.RAM_enable_2.ToString());
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "MBC7"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    case "ROM":
                                        if (address <= 0x3FFF)
                                        {
                                            bankDelegate = () => { return 0; };
                                        }
                                        else
                                        {
                                            bankDelegate = () => { return mbc7Mapper.ROM_bank; };
                                        }
                                        break;
                                    case "CartRAM":
                                        bankDelegate = () => { return 
                                            mbc7Mapper.RAM_enable_1 == true ? 
                                                1 : 
                                                (mbc7Mapper.RAM_enable_2 ? 2 : 0); };
                                        break;
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    case MapperRM8 mapperRM8:
                        Console.WriteLine("camera: rom bank: " + mapperRM8.ROM_bank.ToString());
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "RM8"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    case "ROM":
                                        if (address <= 0x3FFF)
                                        {
                                            bankDelegate = () => { return 0; };
                                        }
                                        else
                                        {
                                            bankDelegate = () => { return mapperRM8.ROM_bank; };
                                        }
                                        break;
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                //case MapperSachen1 mapperSachen1:
                //    Console.WriteLine("camera: rom bank: " + mapperSachen1.ROM_bank.ToString()
                //        + " BASE Rom Bank: " + mapperSachen1.BASE_ROM_Bank.ToString());
                //    return new PlatformMapper
                //    {
                //        InitState = (thisObj, platform) => {
                //            thisObj.Platform = platform;
                //        },
                //        GetMapperName = () => { return "Schn1"; },
                //        InitMapperDetection = () =>
                //        {
                //        },
                //        GetBankFunctionAndCallbackDomain = (address) =>
                //        {
                //            PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                //            if (platformMemoryLayoutEntry == null)
                //            {
                //                throw new Exception("unsupported address");
                //            }
                //            GetMapperBankDelegate? bankDelegate = null;
                //            switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                //            {
                //                case "ROM":
                //                    if (address <= 0x3FFF)
                //                    {
                //                        bankDelegate = () => { return mapperSachen1.BASE_ROM_Bank; };
                //                    }
                //                    else
                //                    {
                //                        bankDelegate = () => { return mapperSachen1.ROM_bank; };
                //                    }
                //                    break;
                //                default:
                //                    bankDelegate = () => { return 0; };
                //                    break;
                //            }
                //            return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                //        }
                //    };
                //case MapperSachen2 mapperSachen2:
                //    Console.WriteLine("camera: rom bank: " + mapperSachen2.ROM_bank.ToString()
                //        + " BASE Rom Bank: " + mapperSachen2.BASE_ROM_Bank.ToString());
                //    return new PlatformMapper
                //    {
                //        InitState = (thisObj, platform) => {
                //            thisObj.Platform = platform;
                //        },
                //        GetMapperName = () => { return "Schn2"; },
                //        InitMapperDetection = () =>
                //        {
                //        },
                //        GetBankFunctionAndCallbackDomain = (address) =>
                //        {
                //            PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                //            if (platformMemoryLayoutEntry == null)
                //            {
                //                throw new Exception("unsupported address");
                //            }
                //            GetMapperBankDelegate? bankDelegate = null;
                //            switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                //            {
                //                case "ROM":
                //                    if (address <= 0x3FFF)
                //                    {
                //                        bankDelegate = () => { return mapperSachen2.BASE_ROM_Bank; };
                //                    }
                //                    else
                //                    {
                //                        bankDelegate = () => { return mapperSachen2.ROM_bank; };
                //                    }
                //                    break;
                //                default:
                //                    bankDelegate = () => { return 0; };
                //                    break;
                //            }
                //            return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                //        }
                //    };
                case MapperTAMA5 mapperTAMA5:
                        Console.WriteLine("camera: rom bank: " + mapperTAMA5.ROM_bank.ToString()
                            + " ram bank: " + mapperTAMA5.RAM_bank.ToString());
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "Bandai TAMA5"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    case "ROM":
                                        if (address <= 0x3FFF)
                                        {
                                            bankDelegate = () => { return 0; };
                                        }
                                        else
                                        {
                                            bankDelegate = () => { return mapperTAMA5.ROM_bank; };
                                        }
                                        break;
                                    case "CartRAM":
                                        bankDelegate = () => { return mapperTAMA5.RAM_bank; };
                                        break;
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    case MapperWT mapperWT:
                        Console.WriteLine("camera: rom bank: " + mapperWT.ROM_bank.ToString());
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "Wisdom Tree"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    case "ROM":
                                        bankDelegate = () => { return mapperWT.ROM_bank; };
                                        break;
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    case MapperDefault:
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "No Mapper"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    case MapperMBC6:
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "MBC6"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    case MapperMMM01:
                        return new PlatformMapper
                        {
                            InitState = (thisObj, platform) => {
                                thisObj.Platform = platform;
                            },
                            GetMapperName = () => { return "MMM01"; },
                            InitMapperDetection = () =>
                            {
                            },
                            GetBankFunctionAndCallbackDomain = (address) =>
                            {
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                                if (platformMemoryLayoutEntry == null)
                                {
                                    throw new Exception("unsupported address");
                                }
                                GetMapperBankDelegate? bankDelegate = null;
                                switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                                {
                                    default:
                                        bankDelegate = () => { return 0; };
                                        break;
                                }
                                return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "System Bus");
                            }
                        };
                    default:
                        throw new Exception("unknown mapper.");
                }
            }
            else
            {
                throw new Exception("unknown mapper.");
            }
        }
        else if (emulator != null && emulator is LibsnesCore && memoryDomains != null && memoryDomains.Count() > 0)
        {
            return new PlatformMapper
            {
                InitState = (thisObj, platform) => {
                    thisObj.Platform = platform;
                },
                GetMapperName = () => {
                    return ((LibsnesCore)emulator).Api.GameboyMapper.ToString();
                },
                InitMapperDetection = () =>
                {
                },
                GetBankFunctionAndCallbackDomain = (address) =>
                {
                    LibsnesCore snes = (LibsnesCore)debuggable;

                    PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address);
                    if (platformMemoryLayoutEntry == null)
                    {
                        throw new Exception("unsupported address");
                    }
                    GetMapperBankDelegate? bankDelegate = null;
                    switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                    {
                        case "ROM":
                            if (address <= 0x3FFF)
                            {
                                bankDelegate = () => { return Convert.ToInt32(snes.GetBanks()["ROM0 BANK"]); };
                            }
                            else
                            {
                                bankDelegate = () => { return Convert.ToInt32(snes.GetBanks()["ROMX BANK"]); };
                            }
                            break;
                        case "VRAM":
                            bankDelegate = () => { return Convert.ToInt32(snes.GetBanks()["VRAM BANK"]); };
                            break;
                        case "CartRAM":
                            bankDelegate = () => { return Convert.ToInt32(snes.GetBanks()["SRAM BANK"]); };
                            break;
                        case "WRAM":
                            bankDelegate = () => { return Convert.ToInt32(snes.GetBanks()["WRAM BANK"]); };
                            break;
                        default:
                            bankDelegate = () => { return 0xFF; };
                            break;
                    }
                    return new Tuple<GetMapperBankDelegate?, string>(bankDelegate, "SGB System Bus");
                }
                //public delegate int GetMapperBankDelegate();
                //public delegate Tuple<GetMapperBankDelegate?, string> GetMapperAddressGetBankFunctionAndCallbackDomain(uint address);
                //public GetMapperAddressGetBankFunctionAndCallbackDomain? GetBankFunctionAndCallbackDomain = null;
            };
        }
        else
        {
            throw new Exception("Unable to set up banking methodology");
        }
    };

    public class PlatformMapper
    {
        public PlatformEntry? Platform { get; set; }
        public Dictionary<string, object> state = new Dictionary<string, object>();
        public delegate void InitStateDelegate(PlatformMapper thisObj, PlatformEntry? Platform);
        public InitStateDelegate? InitState = null;
        public delegate string GetMapperNameDelegate();
        public GetMapperNameDelegate? GetMapperName = null;
        public delegate void InitMapperDetectionDelegate();
        public InitMapperDetectionDelegate? InitMapperDetection = null;
        public delegate int GetMapperBankDelegate();
        public delegate Tuple<GetMapperBankDelegate?, string> GetMapperAddressGetBankFunctionAndCallbackDelegate(uint address);
        public GetMapperAddressGetBankFunctionAndCallbackDelegate? GetBankFunctionAndCallbackDomain = null;
    };

    public record PlatformEntry
    {
        public bool IsBigEndian { get; set; } = false;
        public bool IsLittleEndian => IsBigEndian == false;
        public string BizhawkIdentifier { get; set; } = string.Empty;
        public int? FrameSkipDefault { get; set; } = null;

        public PlatformMemoryLayoutEntry[] MemoryLayout { get; set; } = Array.Empty<PlatformMemoryLayoutEntry>();

        public GetMapperDelegate? GetMapper = null;

        public PlatformMemoryLayoutEntry? FindMemoryLayout(uint address)
        {
            return MemoryLayout.Where(i =>
            {
                return i != null && i.PhysicalStartingAddress <= address && i.PhysicalEndingAddress >= address;
            }).DefaultIfEmpty(null).FirstOrDefault();
        }
    }

    public record PlatformMemoryLayoutEntry
    {
        public string BizhawkIdentifier { get; set; } = string.Empty;
        public string BizhawkAlternateIdentifier { get; set; } = string.Empty;
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
                    BizhawkAlternateIdentifier = "RAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x00,
                    Length = 0x800
                }
            },
            GetMapper = (platform, emulator, memoryDomains, debuggable, boardName) =>
            {
                throw new Exception("NYI");
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
                    BizhawkAlternateIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x7E0000,
                    Length = 0x10000
                }
            },
            GetMapper = (platform, emulator, memoryDomains, debuggable, boardName) =>
            {
                return new PlatformMapper
                {
                    GetMapperName = () => { return "N/A"; },
                    InitMapperDetection = () =>
                    {
                    },
                    GetBankFunctionAndCallbackDomain = (address) =>
                    {
                        return new Tuple<GetMapperBankDelegate?, string>(() => { return 0; }, "System Bus");
                    }
                };
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
                    BizhawkAlternateIdentifier = "ROM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x7FFF,
                    EventsOnly = true
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "CartRAM",
                    BizhawkAlternateIdentifier = "CartRAM",
                    CustomPacketTransmitPosition = 0x0,
                    PhysicalStartingAddress = 0xA000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "WRAM",
                    BizhawkAlternateIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1,
                    PhysicalStartingAddress = 0xC000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "VRAM",
                    BizhawkAlternateIdentifier = "VRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0x8000,
                    Length = 0x1FFF
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "HRAM",
                    BizhawkAlternateIdentifier = "HRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0xFF80,
                    Length = 0x7E
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "System Bus",
                    BizhawkAlternateIdentifier = "System Bus",
                    CustomPacketTransmitPosition = 0x10000,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x7E,
                    EventsOnly = true
                }
            },
            GetMapper = GBMapperDelegate
        },
        new PlatformEntry()
        {
            IsBigEndian = false,
            BizhawkIdentifier = "GBC",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "ROM",
                    BizhawkAlternateIdentifier = "ROM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x7FFF,
                    EventsOnly = true
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "CartRAM",
                    BizhawkAlternateIdentifier = "CartRAM",
                    CustomPacketTransmitPosition = 0x0,
                    PhysicalStartingAddress = 0xA000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "WRAM",
                    BizhawkAlternateIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1,
                    PhysicalStartingAddress = 0xC000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "VRAM",
                    BizhawkAlternateIdentifier = "VRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0x8000,
                    Length = 0x1FFF
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "HRAM",
                    BizhawkAlternateIdentifier = "HRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0xFF80,
                    Length = 0x7E
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "System Bus",
                    BizhawkAlternateIdentifier = "System Bus",
                    CustomPacketTransmitPosition = 0x10000,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x7E,
                    EventsOnly = true
                }
            },
            GetMapper = GBMapperDelegate
        },
        new PlatformEntry()
        {
            IsBigEndian = false,
            BizhawkIdentifier = "SGB",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "SGB CARTROM",
                    BizhawkAlternateIdentifier = "ROM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x7FFF,
                    EventsOnly = true
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "SGB CARTRAM",
                    BizhawkAlternateIdentifier = "CartRAM",
                    CustomPacketTransmitPosition = 0x0,
                    PhysicalStartingAddress = 0xA000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "SGB WRAM",
                    BizhawkAlternateIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1,
                    PhysicalStartingAddress = 0xC000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "VRAM",
                    BizhawkAlternateIdentifier = "VRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0x8000,
                    Length = 0x1FFF
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "SGB HRAM",
                    BizhawkAlternateIdentifier = "HRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0xFF80,
                    Length = 0x7E
                },
                new PlatformMemoryLayoutEntry {
                    BizhawkIdentifier = "SGB System Bus",
                    BizhawkAlternateIdentifier = "System Bus",
                    CustomPacketTransmitPosition = 0x10000,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x7E,
                    EventsOnly = true
                }
            },
            GetMapper = GBMapperDelegate
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
                    BizhawkAlternateIdentifier = "EWRAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x02000000,
                    Length = 0x00040000
                },
                new PlatformMemoryLayoutEntry
                {
                    BizhawkIdentifier = "IWRAM",
                    BizhawkAlternateIdentifier = "IWRAM",
                    CustomPacketTransmitPosition = 0x00040000 + 1,
                    PhysicalStartingAddress = 0x03000000,
                    Length = 0x00008000
                }
            },
            GetMapper = (platform, emulator, memoryDomains, debuggable, boardName) =>
            {
                return new PlatformMapper
                {
                    GetMapperName = () => { return "N/A"; },
                    InitMapperDetection = () =>
                    {
                    },
                    GetBankFunctionAndCallbackDomain = (address) =>
                    {
                        return new Tuple<GetMapperBankDelegate?, string>(() => { return 0; }, "System Bus");
                    }
                };
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
                    BizhawkAlternateIdentifier = "Main RAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x2000000,
                    Length = 0x400000
                }
            },
            GetMapper = (platform, emulator, memoryDomains, debuggable, boardName) =>
            {
                return new PlatformMapper
                {
                    GetMapperName = () => { return "N/A"; },
                    InitMapperDetection = () =>
                    {
                    },
                    GetBankFunctionAndCallbackDomain = (address) =>
                    {
                        return new Tuple<GetMapperBankDelegate?, string>(() => { return 0; }, "System Bus");
                    }
                };
            }
        }
    };
}
#endregion