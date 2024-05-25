using BizHawk.Client.Common;
using BizHawk.Client.EmuHawk;
using BizHawk.Common;
using BizHawk.Common.StringExtensions;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Nintendo.Gameboy;
using BizHawk.Emulation.Cores.Nintendo.GBHawk;
using BizHawk.Emulation.Cores.Nintendo.Sameboy;
using BizHawk.Emulation.Cores.Nintendo.SNES;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using static GameHook.Integrations.BizHawk.BizHawkInterface;
using static GameHookIntegration.SharedPlatformConstants;
using static GameHookIntegration.SharedPlatformConstants.PlatformMapper;

namespace GameHookIntegration;

[ExternalTool("GameHook.Integrations.BizHawk")]
public sealed class GameHookIntegrationForm : ToolFormBase, IToolForm, IExternalToolForm, IDisposable
{
    private static void Fill<T>(T[] array, T value, int startIndex, int count)
    {
        if (array == null)
        {
            throw new ArgumentNullException(nameof(array));
        }

        if ((uint)startIndex > (uint)array.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(array), "Index must be less then array length.");
        }

        if ((uint)count > (uint)(array.Length - startIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(array), "count plus start index must be less then or equal to array length");
        }

        for (int i = startIndex; i < startIndex + count; i++)
        {
            array[i] = value;
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
    private readonly int GameHookEventsLookup_ElementSize;
    private readonly Semaphore GameHookEvents_eventsSemaphore;

    private readonly MemoryMappedFile? GameHookWriteCall_MemoryMappedFile;
    private readonly MemoryMappedViewAccessor? GameHookWriteCall_Accessor;
    private readonly int GameHookWriteCall_ElementSize;
    private readonly Semaphore GameHookWriteCall_Semaphore;

    private readonly Dictionary<MemoryDomain, Dictionary<long, byte>> InstantWriteMap;

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

        GameHookWriteCall_ElementSize = Marshal.SizeOf(typeof(WriteCall));
        long writeEventsMappedSize = sizeof (int) + sizeof (int) + GameHookWriteCall_ElementSize * SharedPlatformConstants.BIZHAWK_MAX_WRITE_CALLS_SIZE; // front, rear, array.
        GameHookWriteCall_MemoryMappedFile = MemoryMappedFile.CreateOrOpen("GAMEHOOK_BIZHAWK_WRITE_CALLS.bin", writeEventsMappedSize, MemoryMappedFileAccess.ReadWrite);
        GameHookWriteCall_Accessor = GameHookWriteCall_MemoryMappedFile.CreateViewAccessor(0, writeEventsMappedSize, MemoryMappedFileAccess.ReadWrite);

        WriteCall template = new();
        WriteCall[] writeCalls = new WriteCall[SharedPlatformConstants.BIZHAWK_MAX_WRITE_CALLS_SIZE];
        Fill(writeCalls, template, 0, writeCalls.Length);
        GameHookWriteCall_Accessor.Write(0, -1);
        GameHookWriteCall_Accessor.Write(sizeof(int), -1);
        GameHookWriteCall_Accessor.WriteArray(sizeof(int) * 2, writeCalls, 0, writeCalls.Length);

        GameHookWriteCall_Semaphore = new Semaphore(initialCount: 1, maximumCount: 1, name: "GAMEHOOK_BIZHAWK_WRITE_CALLS.semaphore");

        InstantWriteMap = new();
    }

    private void SyncEvents()
    {
        Debuggable?.MemoryCallbacks?.Clear();
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

            // process events into tuples.
            int idx = -1;
            List<Tuple<int, long, string, int[], EventAddress, Tuple<string, int>[]>> processedEventAddressLookups = new();
            foreach (var x in eventAddressLookups)
            {
                idx++;
                if (!x.Active)
                {
                    continue;
                }
                else if ((x.EventType & ~(EventType.EventType_HardReset | EventType.EventType_SoftReset)) == 0)
                {
                    continue;
                }
                else
                {
                    var untransformedOverrides = x.GetOverrides();
                    Tuple<string, int>[] overrides = untransformedOverrides.Select(x => new Tuple<string, int>(x.GetRegisterString(), Convert.ToInt32(x.Value))).ToArray();
                    string bitsString = x.GetBitsString();
                    List<int> bits = new();
                    if (bitsString != null && bitsString != "")
                    {
                        foreach (var subset in bitsString.Split(','))
                        {
                            string[] range = subset.Split('-');
                            if (range.Length > 2 || range.Length <= 0 || range[0] == "")
                            {
                                throw new Exception("missing string or missing hyphen");
                            }
                            else if (range.Length == 1)
                            {
                                if (int.TryParse(range[0], out int startEnd))
                                {
                                    int start = startEnd;
                                    int end = startEnd;
                                    bits.AddRange(Enumerable.Range(start, end - start + 1));
                                }
                                else
                                {
                                    throw new Exception($"unexpected range: {subset}");
                                }
                            }
                            else
                            {
                                if (int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end))
                                {
                                    bits.AddRange(Enumerable.Range(start, end - start + 1));
                                }
                                else
                                {
                                    throw new Exception($"unexpected range: {subset}");
                                }
                            }
                        }
                    }
                    Tuple<int, long, string, int[], EventAddress, Tuple<string, int>[]> processedEventAddressLookup = new(idx, 0, x.GetNameString(), bits.ToArray(), x, overrides);
                    processedEventAddressLookups.Add(processedEventAddressLookup);
                }
            }

            // clear all current breakpoints.
            Console.WriteLine($"Clearing events.");
            Debuggable?.MemoryCallbacks?.Clear();
            HardReset = null;
            SoftReset = null;

            // get list of unique addressess to break on.
            List<long> breakAddresses = new();
            foreach(var x in processedEventAddressLookups)
            {
                    int length = (x.Item5.Length > 0) ? x.Item5.Length : 1;
                    int size = (x.Item5.Size > 0) ? x.Item5.Size : 1;
                    long address = x.Item5.Address;
                    for (var i = address; i < address + (length * size); i++)
                    {
                        breakAddresses.Add(i);
                    }
                    breakAddresses = breakAddresses.Distinct().ToList();
                    breakAddresses.Sort();
            }
            Console.Out.WriteLine("breakAddresses:");
            foreach (var address in breakAddresses)
            {
                Console.Out.WriteLine(address);
            }
            // group each events by address.
            EventType[] eventTypes = new EventType[] { EventType.EventType_Read, EventType.EventType_Write, EventType.EventType_Execute };
            MemoryCallbackType[] callbacktypes = new MemoryCallbackType[] { MemoryCallbackType.Read, MemoryCallbackType.Write, MemoryCallbackType.Execute };
            Dictionary<long, Dictionary<MemoryCallbackType, Dictionary<ushort, List<Tuple<int, long, string, int[]?, EventAddress, Tuple<string, int>[]>>>>> breakAddressessTypeBankEventAddressess = new();
            foreach(var address in breakAddresses)
            {
                foreach(var x in processedEventAddressLookups)
                {
                    int length = (x.Item5.Length > 0) ? x.Item5.Length : 1;
                    int size = (x.Item5.Size > 0) ? x.Item5.Size : 1;
                    long eventAddress = x.Item5.Address;

                    if (eventAddress <= address && eventAddress + (length * size) > address)
                    {
                        if (!breakAddressessTypeBankEventAddressess.ContainsKey(address))
                        {
                            breakAddressessTypeBankEventAddressess.Add(address, new Dictionary<MemoryCallbackType, Dictionary<ushort, List<Tuple<int, long, string, int[]?, EventAddress, Tuple<string, int>[]>>>>());
                        }
                        int callbackTypeIdx = -1;
                        foreach (var eventType in eventTypes)
                        {
                            callbackTypeIdx++;
                            if ((x.Item5.EventType & eventType) != 0)
                            {
                                if (!breakAddressessTypeBankEventAddressess[address].ContainsKey(callbacktypes[callbackTypeIdx]))
                                {
                                    breakAddressessTypeBankEventAddressess[address].Add(callbacktypes[callbackTypeIdx], new Dictionary<ushort, List<Tuple<int, long, string, int[]?, EventAddress, Tuple<string, int>[]>>>());
                                }
                                if (!breakAddressessTypeBankEventAddressess[address][callbacktypes[callbackTypeIdx]].ContainsKey(x.Item5.Bank))
                                {
                                    breakAddressessTypeBankEventAddressess[address][callbacktypes[callbackTypeIdx]].Add(x.Item5.Bank, new List<Tuple<int, long, string, int[]?, EventAddress, Tuple<string, int>[]>>());
                                }
                                int[]? bits = null;
                                if(x.Item4 != null && x.Item4.Length >= 1)
                                {
                                    long offset = address - eventAddress;
                                    int bitOffsetStart = Convert.ToInt32(offset * 8);
                                    int bitOffsetEnd = bitOffsetStart + 8;
                                    bits = x.Item4.Where(x => x >= bitOffsetStart && x < bitOffsetEnd).Select(x => x - bitOffsetStart).ToArray();
                                }
                                breakAddressessTypeBankEventAddressess[address][callbacktypes[callbackTypeIdx]][x.Item5.Bank].Add(new Tuple<int, long, string, int[]?, EventAddress, Tuple<string, int>[]>(x.Item1, address - eventAddress, x.Item3, bits, x.Item5, x.Item6));
                            }
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
                GetBank ??= () =>
                    {
                        return 0;
                    };
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
                                                                var eventIdx = callback.Item1;
                                                                var eventOffset = callback.Item2;
                                                                var eventName = callback.Item3;
                                                                var eventBits = callback.Item4;
                                                                var eventAddress = callback.Item5;
                                                                var overrides = callback.Item6;
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
                                                                    Console.Out.WriteLine($"BizHawkGameHook_{address:X}_{eventType}, eventIdx: {eventIdx}, eventOffset: {eventOffset}, name: {eventName}, bank: {bank:X}, bits: {string.Join(",", eventBits ?? (new int[0]))}");
                                                                }
                                                                else if(eventType == MemoryCallbackType.Read)
                                                                {
                                                                    Console.Out.WriteLine($"BizHawkGameHook_{address:X}_{eventType}, eventIdx: {eventIdx}, eventOffset: {eventOffset}, name: {eventName}, bank: {bank:X}, bits: {string.Join(",", eventBits ?? (new int[0]))}");
                                                                    byte newByte = 0;

                                                                    MemoryDomain domain = MemoryDomains![identifierDomain] ?? throw new Exception("unexpted memory domain");
                                                                    if (InstantWriteMap.ContainsKey(domain) && InstantWriteMap[domain].ContainsKey(domainAddress))
                                                                    {
                                                                        newByte = InstantWriteMap[domain][domainAddress];

                                                                        if (eventBits != null && eventBits.Length > 0)
                                                                        {
                                                                            byte oldByte = domain!.PeekByte(domainAddress);
                                                                            Console.WriteLine($"oldByte: {oldByte:X}, domainAddress: {domainAddress:X}");

                                                                            var inputBits = new BitArray(new byte[] { newByte });
                                                                            var outputBits = new BitArray(new byte[] { oldByte });

                                                                            foreach (var x in eventBits)
                                                                            {
                                                                                outputBits[x] = inputBits[x];
                                                                            }
                                                                            byte[] newByteContainer = new byte[1];
                                                                            outputBits.CopyTo(newByteContainer, 0);
                                                                            newByte = newByteContainer[0];
                                                                        }
                                                                        Console.WriteLine($"newByte: {newByte:X}, domainAddress: {domainAddress:X}");
                                                                        domain!.PokeByte(domainAddress, newByte);
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                    return;
                                                }
                                            ),
                                            Convert.ToUInt32(domainAddress),
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

    private void WriteData()
    {
        if (Platform == null)
        {
            return;
        }

        GameHookWriteCall_Semaphore.WaitOne();

        InstantWriteMap.Clear();

        // read in the current queue state.
        GameHookWriteCall_Accessor!.Read(0, out int front);

        if (front != -1)
        {
            GameHookWriteCall_Accessor!.Read(sizeof(int), out int rear);

            if (rear != -1)
            {
                WriteCall[] writeCalls = new WriteCall[SharedPlatformConstants.BIZHAWK_MAX_WRITE_CALLS_SIZE];
                GameHookWriteCall_Accessor!.ReadArray(sizeof(int) * 2, writeCalls, 0, SharedPlatformConstants.BIZHAWK_MAX_WRITE_CALLS_SIZE);

                bool changed = false;
                // create the queue and enqueue the write
                CircularArrayQueue<WriteCall> queue = new(writeCalls, front, rear);
                WriteCall? data;
                while ((data = queue.Dequeue()) != null)
                {
                    //MemoryDomains.First().PokeByte();
                    if (data.Value.Active)
                    {
                        changed = true;
                        UInt32 address = Convert.ToUInt32(data.Value.Address);
                        UInt32 baseAddress = address;
                        PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = Platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
                        MemoryDomain domain = MemoryDomains?[platformMemoryLayoutEntry.BizhawkIdentifier] ?? MemoryDomains?[platformMemoryLayoutEntry.BizhawkAlternateIdentifier] ?? throw new Exception("unsupported adress");
                        address -= Convert.ToUInt32(platformMemoryLayoutEntry.PhysicalStartingAddress);

                        byte[] bytes = data.Value.GetBytes();
                        for (int i = 0; i < bytes.Length; i++)
                        {
                            domain.PokeByte(address + i, bytes[i]);
                        }

                        if (data.Value.Frozen)
                        {
                            PlatformMemoryLayoutEntry[] platformMemoryLayoutEntries = Platform.FindMemoryLayouts(baseAddress) ?? throw new Exception("unsupported address");
                            MemoryDomain[] domains = platformMemoryLayoutEntries.Select(platformMemoryLayoutEntry => MemoryDomains?[platformMemoryLayoutEntry.BizhawkIdentifier] ?? MemoryDomains?[platformMemoryLayoutEntry.BizhawkAlternateIdentifier] ?? throw new Exception("unsupported adress")).ToArray();

                            for (int idx = 0; idx < platformMemoryLayoutEntries.Length && idx < domains.Length; idx++)
                            {
                                MemoryDomain curDomain = domains[idx];
                                PlatformMemoryLayoutEntry curPlatformMemoryLayoutEntry = platformMemoryLayoutEntries[idx];
                                if (!InstantWriteMap.ContainsKey(curDomain))
                                {
                                    InstantWriteMap.Add(curDomain, new());
                                }

                                UInt32 addr = baseAddress - Convert.ToUInt32(curPlatformMemoryLayoutEntry.PhysicalStartingAddress);

                                for (int i = 0; i < bytes.Length; i++)
                                {
                                    if (!InstantWriteMap[curDomain].ContainsKey(addr + i))
                                    {
                                        InstantWriteMap[curDomain].Add(addr + i, bytes[i]);
                                    }
                                    else
                                    {
                                        InstantWriteMap[curDomain][addr + i] = bytes[i];
                                    }
                                }
                            }
                        }

                    }
                    else
                    {
                        throw new Exception("unexpected inactive write message");
                    }
                }

                if (changed)
                {
                    // write the queue back.
                    writeCalls = queue.Array;
                    GameHookWriteCall_Accessor!.Write(0, queue.Front);
                    GameHookWriteCall_Accessor!.Write(sizeof(int), queue.Rear);
                    GameHookWriteCall_Accessor!.WriteArray(sizeof(int) * 2, writeCalls, 0, SharedPlatformConstants.BIZHAWK_MAX_WRITE_CALLS_SIZE);
                }
            }
        }

        GameHookWriteCall_Semaphore.Release();
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
                Console.Out.WriteLine($"scope: {i}");
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
        foreach(var i in Debuggable?.GetCpuFlagsAndRegisters() ?? new Dictionary<string, RegisterValue>())
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
                mapper.InitState?.Invoke(mapper, Platform);
                mapper.InitMapperDetection?.Invoke();
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
                        bool got = dict.TryGetValue("Power", out object hardReset);
                        if (got && hardReset is bool v)
                        {
                            if (v)
                            {
                                HardReset?.Invoke();
                                breakLoop = true;
                            }
                        }
                    }

                    if (dict.ContainsKey("Reset"))
                    {
                        bool got = dict.TryGetValue("Reset", out object softReset);
                        if (got && softReset is bool v)
                        {
                            if (v)
                            {
                                SoftReset?.Invoke();
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

            WriteData();

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
                mapperName = (boardName?.Trim('\0')) switch
                {
                    "none" or "NULL" or "NROM" or "Plain ROM" or "NULL [RAM]" or "Plain ROM+RAM" or "NULL [RAM,battery]" or "Plain ROM+RAM+BATTERY" or "MBC0" => "No Mapper",
                    "MBC1" or "MBC1 ROM" or "MBC1 [RAM]" or "MBC1 ROM+RAM" or "MBC1 [RAM,battery]" or "MBC1 ROM+RAM+BATTERY" => "MBC1",
                    "MBC1M" or "MBC1M [RAM]" or "MBC1M [RAM,battery]" => "MBC1M",
                    "MBC2" or "MBC2 ROM" or "MBC2 [battery]" or "MBC2 ROM+BATTERY" => "MBC2",
                    "MBC3" or "MBC3 ROM" or "MBC3 [RTC,battery]" or "MBC3 ROM+TIMER+BATTERY" or "MBC3 [RAM,RTC,battery]" or "MBC3 ROM+TIMER+RAM+BATTERY" or "MBC3 [RAM]" or "MBC3 ROM+RAM" or "MBC3 [RAM,battery]" or "MBC3 ROM+RAM+BATTERY" => "MBC3",
                    "MBC4" or "MBC4 [RAM]" or "MBC4 [RAM,battery]" => "MBC4",
                    "MBC5" or "MBC5 ROM" or "MBC5 [RAM]" or "MBC5 ROM+RAM" or "MBC5 [RAM,battery]" or "MBC5 ROM+RAM+BATTERY" or "MBC5 [rumble]" or "MBC5 ROM+RUMBLE" or "MBC5 [RAM,rumble]" or "MBC5 ROM+RUMBLE+RAM" or "MBC5 [RAM,rumble,battery]" or "MBC5 ROM+RUMBLE+RAM+BATTERY" => "MBC5",
                    "MBC6" => "MBC6",
                    "MBC7" or "MBC7 ROM+ACCEL+EEPROM" => "MBC7",
                    "MMM01" or "MMM01 [RAM]" or "MMM01 [RAM,battery]" => "MMM01",
                    "M161" => "M161",
                    "Wisdom Tree" or "Wtree" => "Wisdom Tree",
                    "Pocket Camera" or "Pocket Camera ROM+RAM+BATTERY" or "CAM" or "CAMERA" => "Pocket Camera",
                    "Bandai TAMA5" or "TAMA5" or "TAMA" => "Bandai TAMA5",
                    "HuC3" or "HuC3 ROM+RAM+BATTERY" => "HuC3",
                    "HuC1" or "HuC1 [RAM,battery]" or "HuC1 ROM+RAM+BATTERY" => "HuC1",
                    "Schn1" => "Schn1",
                    "Schn2" => "Schn2",
                    _ => throw new Exception($"NYI \"{boardName?.Trim('\0')}\""),
                };
                return new PlatformMapper
                {
                    GetMapperName = () => { return mapperName; },
                    InitMapperDetection = () =>
                    {
                    },
                    GetBankFunctionAndCallbackDomain = (address) =>
                    {
                        PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
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
                mapperName = (boardName?.Trim('\0')) switch
                {
                    "none" or "NULL" or "NROM" or "Plain ROM" or "NULL [RAM]" or "Plain ROM+RAM" or "NULL [RAM,battery]" or "Plain ROM+RAM+BATTERY" or "MBC0" => "No Mapper",
                    "MBC1" or "MBC1 ROM" or "MBC1 [RAM]" or "MBC1 ROM+RAM" or "MBC1 [RAM,battery]" or "MBC1 ROM+RAM+BATTERY" => "MBC1",
                    "MBC1M" or "MBC1M [RAM]" or "MBC1M [RAM,battery]" => "MBC1M",
                    "MBC2" or "MBC2 ROM" or "MBC2 [battery]" or "MBC2 ROM+BATTERY" => "MBC2",
                    "MBC3" or "MBC3 ROM" or "MBC3 [RTC,battery]" or "MBC3 ROM+TIMER+BATTERY" or "MBC3 [RAM,RTC,battery]" or "MBC3 ROM+TIMER+RAM+BATTERY" or "MBC3 [RAM]" or "MBC3 ROM+RAM" or "MBC3 [RAM,battery]" or "MBC3 ROM+RAM+BATTERY" => "MBC3",
                    "MBC4" or "MBC4 [RAM]" or "MBC4 [RAM,battery]" => "MBC4",
                    "MBC5" or "MBC5 ROM" or "MBC5 [RAM]" or "MBC5 ROM+RAM" or "MBC5 [RAM,battery]" or "MBC5 ROM+RAM+BATTERY" or "MBC5 [rumble]" or "MBC5 ROM+RUMBLE" or "MBC5 [RAM,rumble]" or "MBC5 ROM+RUMBLE+RAM" or "MBC5 [RAM,rumble,battery]" or "MBC5 ROM+RUMBLE+RAM+BATTERY" => "MBC5",
                    "MBC6" => "MBC6",
                    "MBC7" or "MBC7 ROM+ACCEL+EEPROM" => "MBC7",
                    "MMM01" or "MMM01 [RAM]" or "MMM01 [RAM,battery]" => "MMM01",
                    "M161" => "M161",
                    "Wisdom Tree" or "Wtree" => "Wisdom Tree",
                    "Pocket Camera" or "Pocket Camera ROM+RAM+BATTERY" or "CAM" or "CAMERA" => "Pocket Camera",
                    "Bandai TAMA5" or "TAMA5" or "TAMA" => "Bandai TAMA5",
                    "HuC3" or "HuC3 ROM+RAM+BATTERY" => "HuC3",
                    "HuC1" or "HuC1 [RAM,battery]" or "HuC1 ROM+RAM+BATTERY" => "HuC1",
                    "Schn1" => "Schn1",
                    "Schn2" => "Schn2",
                    _ => throw new Exception($"NYI \"{boardName?.Trim('\0')}\""),
                };
                return new PlatformMapper
                {
                    GetMapperName = () => { return mapperName; },
                    InitMapperDetection = () =>
                    {
                    },
                    GetBankFunctionAndCallbackDomain = (address) =>
                    {
                        PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
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
        else if (emulator != null && emulator is GBHawk hawk)
        {
            var mapper = hawk.mapper;
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
                                GetMapperBankDelegate? bankDelegate = null;
                                bankDelegate = platformMemoryLayoutEntry.BizhawkIdentifier switch
                                {
                                    "ROM" => () => { return mapperWT.ROM_bank; }

                                    ,
                                    _ => () => { return 0; }

                                    ,
                                };
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
                                GetMapperBankDelegate? bankDelegate = null;
                                bankDelegate = platformMemoryLayoutEntry.BizhawkIdentifier switch
                                {
                                    _ => () => { return 0; }

                                    ,
                                };
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
                                GetMapperBankDelegate? bankDelegate = null;
                                bankDelegate = platformMemoryLayoutEntry.BizhawkIdentifier switch
                                {
                                    _ => () => { return 0; }

                                    ,
                                };
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
                                PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
                                GetMapperBankDelegate? bankDelegate = null;
                                bankDelegate = platformMemoryLayoutEntry.BizhawkIdentifier switch
                                {
                                    _ => () => { return 0; }

                                    ,
                                };
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
        else if (emulator != null && emulator is LibsnesCore core && memoryDomains != null && memoryDomains.Count() > 0)
        {
            return new PlatformMapper
            {
                InitState = (thisObj, platform) => {
                    thisObj.Platform = platform;
                },
                GetMapperName = () => {
                    return core.Api.GameboyMapper.ToString();
                },
                InitMapperDetection = () =>
                {
                },
                GetBankFunctionAndCallbackDomain = (address) =>
                {
                    PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
                    GetMapperBankDelegate? bankDelegate = null;
                    switch (platformMemoryLayoutEntry.BizhawkIdentifier)
                    {
                        case "ROM":
                            if (address <= 0x3FFF)
                            {
                                bankDelegate = () => { return Convert.ToInt32(core.GetBanks()["ROM0 BANK"]); };
                            }
                            else
                            {
                                bankDelegate = () => { return Convert.ToInt32(core.GetBanks()["ROMX BANK"]); };
                            }
                            break;
                        case "VRAM":
                            bankDelegate = () => { return Convert.ToInt32(core.GetBanks()["VRAM BANK"]); };
                            break;
                        case "CartRAM":
                            bankDelegate = () => { return Convert.ToInt32(core.GetBanks()["SRAM BANK"]); };
                            break;
                        case "WRAM":
                            bankDelegate = () => { return Convert.ToInt32(core.GetBanks()["WRAM BANK"]); };
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
        public Dictionary<string, object> state = new();
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

        public PlatformMemoryLayoutEntry[] FindMemoryLayouts(uint address)
        {
            return MemoryLayout.Where(i =>
            {
                return i != null && i.PhysicalStartingAddress <= address && i.PhysicalEndingAddress >= address;
            }).ToArray();
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
    public const int BIZHAWK_MAX_WRITE_CALLS_SIZE = 256 * 2;

    public static readonly IEnumerable<PlatformEntry> Information = new List<PlatformEntry>()
    {
        new() {
            IsBigEndian = true,
            BizhawkIdentifier = "NES",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new() {
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
        new() {
            IsBigEndian = false,
            BizhawkIdentifier = "SNES",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new() {
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
        new()
        {
            IsBigEndian = false,
            BizhawkIdentifier = "GB",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new() {
                    BizhawkIdentifier = "ROM",
                    BizhawkAlternateIdentifier = "ROM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x7FFF,
                    EventsOnly = true
                },
                new() {
                    BizhawkIdentifier = "CartRAM",
                    BizhawkAlternateIdentifier = "CartRAM",
                    CustomPacketTransmitPosition = 0x0,
                    PhysicalStartingAddress = 0xA000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new() {
                    BizhawkIdentifier = "WRAM",
                    BizhawkAlternateIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1,
                    PhysicalStartingAddress = 0xC000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new() {
                    BizhawkIdentifier = "VRAM",
                    BizhawkAlternateIdentifier = "VRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0x8000,
                    Length = 0x1FFF
                },
                new() {
                    BizhawkIdentifier = "HRAM",
                    BizhawkAlternateIdentifier = "HRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0xFF80,
                    Length = 0x7E
                },
                new() {
                    BizhawkIdentifier = "System Bus",
                    BizhawkAlternateIdentifier = "System Bus",
                    CustomPacketTransmitPosition = 0x10000,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x10000,
                    EventsOnly = true
                }
            },
            GetMapper = GBMapperDelegate
        },
        new()
        {
            IsBigEndian = false,
            BizhawkIdentifier = "GBC",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new() {
                    BizhawkIdentifier = "ROM",
                    BizhawkAlternateIdentifier = "ROM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x7FFF,
                    EventsOnly = true
                },
                new() {
                    BizhawkIdentifier = "CartRAM",
                    BizhawkAlternateIdentifier = "CartRAM",
                    CustomPacketTransmitPosition = 0x0,
                    PhysicalStartingAddress = 0xA000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new() {
                    BizhawkIdentifier = "WRAM",
                    BizhawkAlternateIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1,
                    PhysicalStartingAddress = 0xC000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new() {
                    BizhawkIdentifier = "VRAM",
                    BizhawkAlternateIdentifier = "VRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0x8000,
                    Length = 0x1FFF
                },
                new() {
                    BizhawkIdentifier = "HRAM",
                    BizhawkAlternateIdentifier = "HRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0xFF80,
                    Length = 0x7E
                },
                new() {
                    BizhawkIdentifier = "System Bus",
                    BizhawkAlternateIdentifier = "System Bus",
                    CustomPacketTransmitPosition = 0x10000,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x10000,
                    EventsOnly = true
                }
            },
            GetMapper = GBMapperDelegate
        },
        new()
        {
            IsBigEndian = false,
            BizhawkIdentifier = "SGB",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new() {
                    BizhawkIdentifier = "SGB CARTROM",
                    BizhawkAlternateIdentifier = "ROM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x7FFF,
                    EventsOnly = true
                },
                new() {
                    BizhawkIdentifier = "SGB CARTRAM",
                    BizhawkAlternateIdentifier = "CartRAM",
                    CustomPacketTransmitPosition = 0x0,
                    PhysicalStartingAddress = 0xA000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new() {
                    BizhawkIdentifier = "SGB WRAM",
                    BizhawkAlternateIdentifier = "WRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1,
                    PhysicalStartingAddress = 0xC000,
                    Length = 0x1FFF
                    //Length = 0x00400000
                },
                new() {
                    BizhawkIdentifier = "VRAM",
                    BizhawkAlternateIdentifier = "VRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0x8000,
                    Length = 0x1FFF
                },
                new() {
                    BizhawkIdentifier = "SGB HRAM",
                    BizhawkAlternateIdentifier = "HRAM",
                    CustomPacketTransmitPosition = 0x1FFF + 1 + 0x1FFFF + 1 + 0x1FFF + 1,
                    PhysicalStartingAddress = 0xFF80,
                    Length = 0x7E
                },
                new() {
                    BizhawkIdentifier = "SGB System Bus",
                    BizhawkAlternateIdentifier = "System Bus",
                    CustomPacketTransmitPosition = 0x10000,
                    PhysicalStartingAddress = 0x0000,
                    Length = 0x10000,
                    EventsOnly = true
                }
            },
            GetMapper = GBMapperDelegate
        },
        new() {
            IsBigEndian = true,
            BizhawkIdentifier = "GBA",
            MemoryLayout = new PlatformMemoryLayoutEntry[]
            {
                new() {
                    BizhawkIdentifier = "EWRAM",
                    BizhawkAlternateIdentifier = "EWRAM",
                    CustomPacketTransmitPosition = 0,
                    PhysicalStartingAddress = 0x02000000,
                    Length = 0x00040000
                },
                new() {
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
        new()
        {
            IsBigEndian = true,
            BizhawkIdentifier = "NDS",
            FrameSkipDefault = 15,
            MemoryLayout = new PlatformMemoryLayoutEntry[] {
                new() {
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