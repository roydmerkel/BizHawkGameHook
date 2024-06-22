using BizHawk.Client.Common;
using BizHawk.Client.EmuHawk;
using BizHawk.Common;
using BizHawk.Common.StringExtensions;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Nintendo.Gameboy;
using BizHawk.Emulation.Cores.Nintendo.GBHawk;
using BizHawk.Emulation.Cores.Nintendo.Sameboy;
using GameHook.Integrations.BizHawk;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using static GameHook.Integrations.BizHawk.BizHawkInterface;
using static GameHookIntegration.SharedPlatformConstants;
using static GameHookIntegration.SharedPlatformConstants.PlatformMapper;

namespace GameHookIntegration;

[ExternalTool("GameHook.Integrations.BizHawk")]
public sealed class GameHookIntegrationForm : ToolFormBase, IToolForm, IExternalToolForm, IDisposable
{
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

    private readonly object GameHook_EventLock = new();
    private IDictionary<EventAddress, IDictionary<EventType, IMemoryCallback[]>> GameHook_EventCallbacks = new Dictionary<EventAddress, IDictionary<EventType, IMemoryCallback[]>>();
    private IDictionary<ulong, IDictionary<EventType, EventAddress>> GameHook_SerialToEvent = new Dictionary<ulong, IDictionary<EventType, EventAddress>>();
    private IDictionary<string, SharedPlatformConstants.PlatformMemoryLayoutEntry> scopeToEntry = new Dictionary<string, SharedPlatformConstants.PlatformMemoryLayoutEntry>();
    private SharedPlatformConstants.PlatformMapper? Mapper = null;
    private Queue<EventOperation> eventOperationsQueue = new();
    private readonly Queue<WriteCall> writeCallsQueue = new();

    private readonly Dictionary<MemoryDomain, Dictionary<long, byte>> InstantWriteMap;
    private IDictionary<EventAddress, IDictionary<long, IDictionary<MemoryDomain, IDictionary<long, byte>>>> InstantReadCurStateMap = new Dictionary<EventAddress, IDictionary<long, IDictionary<MemoryDomain, IDictionary<long, byte>>>>();
    private IDictionary<EventAddress, IDictionary<long, IDictionary<MemoryDomain, IDictionary<long, byte>>>> InstantReadNewStateMap = new Dictionary<EventAddress, IDictionary<long, IDictionary<MemoryDomain, IDictionary<long, byte>>>>();
    private InstantReadEvents InstantReadValues = new();
    private bool InstantReadValuesSet = false;

    private readonly PipeServer<EventOperation>? eventsPipe;
    private readonly PipeServer<WriteCall>? writeCallsPipe = null;
    private readonly PipeServer<InstantReadEvents>? instantReadValuesPipe = null;

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

        InstantWriteMap = new();

        eventsPipe = new("GAMEHOOK_BIZHAWK_EVENTS.pipe", x => EventOperation.Deserialize(x));
        eventsPipe.PipeReadEvent += OnEventRead;

        writeCallsPipe = new("GAMEHOOK_BIZHAWK_WRITE.pipe", x => WriteCall.Deserialize(x));
        writeCallsPipe.PipeReadEvent += OnWriteRead;

        instantReadValuesPipe = new("GAMEHOOK_BIZHAWK_INSTANT_READ.pipe", x => InstantReadEvents.Deserialize(x));
        InstantReadValuesSet = false;

        Log.EnableDomain("Info");
        Log.EnableDomain("Debug");
    }

    private void OnWriteRead(object sender, PipeBase<WriteCall>.PipeReadArgs e)
    {
        writeCallsQueue.Enqueue(e.Arg);
    }

    private void OnEventRead(object sender, PipeBase<EventOperation>.PipeReadArgs e)
    {
        lock(GameHook_EventLock)
        {
            eventOperationsQueue.Enqueue(e.Arg);
        }
    }

    private void ProcessEventOperation(EventOperation e)
    {
        if (Mapper?.GetBankFunctionAndCallbackDomain == null)
        {
            throw new Exception("Callbacks aren't supported yet on this platform.");
        }
        if (Debuggable == null || !Debuggable.MemoryCallbacksAvailable())
        {
            throw new Exception("Callbacks aren't supported yet on this platform.");
        }
        switch (e.OpType)
        {
            case EventOperationType.EventOperationType_Clear:
                {
                    Debuggable?.MemoryCallbacks?.Clear();
                    GameHook_EventCallbacks = new Dictionary<EventAddress, IDictionary<EventType, IMemoryCallback[]>>();
                    GameHook_SerialToEvent = new Dictionary<ulong, IDictionary<EventType, EventAddress>>();
                    InstantReadCurStateMap = new Dictionary<EventAddress, IDictionary<long, IDictionary<MemoryDomain, IDictionary<long, byte>>>>();
                    InstantReadNewStateMap = new Dictionary<EventAddress, IDictionary<long, IDictionary<MemoryDomain, IDictionary<long, byte>>>>();
                    InstantReadValues = new();
                    InstantReadValuesSet = false;
                    break;
                }
            case EventOperationType.EventOperationType_Add:
                {
                    if (Platform == null)
                    {
                        throw new NullReferenceException(nameof(Platform));
                    }
                    if (e.EventAddress == null)
                        throw new NullReferenceException(nameof(e.EventAddress));
                    EventType evEventType = e.EventType;
                    ulong? serial = e.EventSerial;
                    if (serial == null)
                        throw new NullReferenceException(nameof(serial));
                    ulong serialValue = serial.Value;
                    EventAddress ev = e.EventAddress;
                    bool instantaneous = ev.Instantaneous;
                    if ((ev.EventType & (EventType.EventType_SoftReset | EventType.EventType_HardReset)) != 0)
                    {
                        if ((ev.EventType & EventType.EventType_HardReset) != 0)
                        {
                            HardReset = new HardResetCallbackDelegate(() =>
                            {
                                Log.Note("Info", $"_hardreset");
                                return;
                            });
                        }
                        if ((ev.EventType & EventType.EventType_SoftReset) != 0)
                        {
                            SoftReset = new SoftResetCallbackDelegate(() =>
                            {
                                Log.Note("Info", $"_softreset");
                                return;
                            });
                        }
                    }
                    else
                    {
                        // get overrides, and bits into a format that's usable
                        ushort eventBank = ev.Bank;
                        string eventName = ev.Name;
                        int length = (ev.Length > 0) ? ev.Length : 1;
                        int size = (ev.Size > 0) ? ev.Size : 1;
                        long eventAddress = ev.Address;
                        string eventBits = ev.Bits;
                        List<int> eventBitsList = new();
                        if (eventBits != null && eventBits != "")
                        {
                            foreach (var subset in eventBits.Split(','))
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
                                        eventBitsList.AddRange(Enumerable.Range(start, end - start + 1));
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
                                        eventBitsList.AddRange(Enumerable.Range(start, end - start + 1));
                                    }
                                    else
                                    {
                                        throw new Exception($"unexpected range: {subset}");
                                    }
                                }
                            }
                        }
                        var untransformedOverrides = ev.EventAddressRegisterOverrides;
                        Tuple<string, int>[] overrides = untransformedOverrides.Select(x => new Tuple<string, int>(x.Register, Convert.ToInt32(x.Value))).ToArray();

                        // get list of unique addressess to break on.
                        List<long> breakAddresses = new();
                        for (var i = eventAddress; i < eventAddress + (length * size); i++)
                        {
                            breakAddresses.Add(i);
                        }
                        breakAddresses = breakAddresses.Distinct().ToList();
                        breakAddresses.Sort();

                        Log.Note("Debug", "breakAddresses:");
                        foreach (var breakAddress in breakAddresses)
                        {
                            Log.Note("Debug", $"0x{breakAddress:X}");
                        }

                        // group each events by address.
                        EventType eventEventType = ev.EventType;

                        EventType[] eventTypes = new EventType[] { EventType.EventType_Read, EventType.EventType_Write, EventType.EventType_Execute };
                        MemoryCallbackType[] callbacktypes = new MemoryCallbackType[] { MemoryCallbackType.Read, MemoryCallbackType.Write, MemoryCallbackType.Execute };
                        List<MemoryCallbackType> breakMemoryCallbacks = new();
                        List<EventType> breakEventTypes = new();
                        int callbackTypeIdx = -1;
                        foreach (var eventType in eventTypes)
                        {
                            callbackTypeIdx++;
                            if ((eventEventType & eventType) != 0)
                            {
                                breakMemoryCallbacks.Add(callbacktypes[callbackTypeIdx]);
                                breakEventTypes.Add(eventType);
                            }
                        }

                        Dictionary<long, List<Tuple<long, int[]?>>> breakAddressessTypeBankEventAddressess = new();

                        foreach (var breakAddress in breakAddresses)
                        {
                            if (eventAddress <= breakAddress && eventAddress + (length * size) > breakAddress)
                            {
                                if (!breakAddressessTypeBankEventAddressess.ContainsKey(breakAddress))
                                {
                                    breakAddressessTypeBankEventAddressess.Add(breakAddress, new List<Tuple<long, int[]?>>());
                                }

                                int[]? bits = null;
                                if (eventBitsList != null && eventBitsList.Count >= 1)
                                {
                                    long offset = breakAddress - eventAddress;
                                    int bitOffsetStart = Convert.ToInt32(offset * 8);
                                    int bitOffsetEnd = bitOffsetStart + 8;
                                    bits = eventBitsList.Where(x => x >= bitOffsetStart && x < bitOffsetEnd).Select(x => x - bitOffsetStart).ToArray();
                                }
                                breakAddressessTypeBankEventAddressess[breakAddress].Add(new Tuple<long, int[]?>(breakAddress - eventAddress, bits));
                            }
                        }

                        if (instantaneous)
                        {
                            if (!InstantReadCurStateMap.ContainsKey(ev))
                            {
                                InstantReadCurStateMap.Add(ev, new Dictionary<long, IDictionary<MemoryDomain, IDictionary<long, byte>>>());
                                InstantReadNewStateMap.Add(ev, new Dictionary<long, IDictionary<MemoryDomain, IDictionary<long, byte>>>());
                            }
                            if(!InstantReadValues.ContainsKey(ev))
                            {
                                InstantReadValues.Add(ev, new List<byte[]>());
                            }
                        }

                        // setup callbacks for addressess.
                        foreach (var breakAddressTypeBankEventAddress in breakAddressessTypeBankEventAddressess)
                        {
                            bool found = false;
                            var address = breakAddressTypeBankEventAddress.Key;

                            if (instantaneous)
                            {
                                if (!InstantReadCurStateMap[ev].ContainsKey(address))
                                    InstantReadCurStateMap[ev].Add(address, new Dictionary<MemoryDomain, IDictionary<long, byte>>());
                                if (!InstantReadNewStateMap[ev].ContainsKey(address))
                                    InstantReadNewStateMap[ev].Add(address, new Dictionary<MemoryDomain, IDictionary<long, byte>>());
                            }

                            string identifierDomain = "";
                            long domainAddress = 0;
                            MemoryDomain? identifierMemoryDomain = null;
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
                                    identifierMemoryDomain = MemoryDomains?[identifierDomain] ?? MemoryDomains?[identifierDomain] ?? throw new Exception("unsupported adress");
                                    found = true;
                                    domainAddress = address - i.PhysicalStartingAddress;
                                }
                            }
                            if (!found)
                            {
                                throw new Exception($"Unsupported memory address 0x{address:X}");
                            }
                            else if(identifierMemoryDomain == null)
                            {
                                throw new Exception($"Unsupported memory address 0x{address:X}");
                            }

                            if (instantaneous)
                            {
                                if (!InstantReadCurStateMap[ev][address].ContainsKey(identifierMemoryDomain))
                                    InstantReadCurStateMap[ev][address].Add(identifierMemoryDomain, new Dictionary<long, byte>());
                                if (!InstantReadNewStateMap[ev][address].ContainsKey(identifierMemoryDomain))
                                    InstantReadNewStateMap[ev][address].Add(identifierMemoryDomain, new Dictionary<long, byte>());
                                byte value = identifierMemoryDomain.PeekByte(domainAddress);
                                if (!InstantReadCurStateMap[ev][address][identifierMemoryDomain].ContainsKey(domainAddress))
                                    InstantReadCurStateMap[ev][address][identifierMemoryDomain].Add(domainAddress, value);
                                else
                                    InstantReadCurStateMap[ev][address][identifierMemoryDomain][domainAddress] = value;
                                if (!InstantReadNewStateMap[ev][address][identifierMemoryDomain].ContainsKey(domainAddress))
                                    InstantReadNewStateMap[ev][address][identifierMemoryDomain].Add(domainAddress, value);
                                else
                                    InstantReadNewStateMap[ev][address][identifierMemoryDomain][domainAddress] = value;
                            }

                            foreach (var eventTypeEventAddresses in breakAddressTypeBankEventAddress.Value)
                            {
                                foreach (var eventType in breakMemoryCallbacks)
                                {
                                    if (!GameHook_EventCallbacks.ContainsKey(ev))
                                    {
                                        GameHook_EventCallbacks.Add(ev, new Dictionary<EventType, IMemoryCallback[]>());
                                    }
                                    if (!GameHook_EventCallbacks[ev].ContainsKey(evEventType))
                                    {
                                        GameHook_EventCallbacks[ev].Add(evEventType, Array.Empty<IMemoryCallback>());
                                    }
                                    Log.Note("Info", $"Adding event --> {address:X}, {eventType}: BizHawkGameHook_{address:X}_{eventType}.");
                                    IMemoryCallback callback = new MemoryCallback(identifierDomain,
                                                            eventType,
                                                            $"BizHawkGameHook_{address:X}_{eventType}",
                                                            new MemoryCallbackDelegate(
                                                                (address, value, flags) =>
                                                                {
                                                                    ushort[] banks = new ushort[] { ushort.MaxValue, Convert.ToUInt16(GetBank()) };
                                                                    foreach (var bank in banks)
                                                                    {
                                                                        if (eventBank == bank)
                                                                        {
                                                                            var eventOffset = eventTypeEventAddresses.Item1;
                                                                            var eventBits = eventTypeEventAddresses.Item2;
                                                                            if (overrides != null)
                                                                            {
                                                                                foreach (var i in overrides)
                                                                                {
                                                                                    Log.Note("Debug", $"Overriding: {i.Item1} with 0x{i.Item2:X}");
                                                                                    Debuggable.SetCpuRegister(i.Item1, i.Item2);
                                                                                }
                                                                            }
                                                                            if (eventType == MemoryCallbackType.Execute)
                                                                            {
                                                                                Log.Note("Debug", $"BizHawkGameHook_{address:X}_{eventType}, eventOffset: {eventOffset}, name: {eventName}, bank: {bank:X}, bits: {string.Join(",", eventBits ?? (new int[0]))}");
                                                                            }
                                                                            else if (eventType == MemoryCallbackType.Read)
                                                                            {
                                                                                Log.Note("Debug", $"BizHawkGameHook_{address:X}_{eventType}, eventOffset: {eventOffset}, name: {eventName}, bank: {bank:X}, bits: {string.Join(",", eventBits ?? (new int[0]))}");
                                                                                byte newByte = 0;

                                                                                MemoryDomain domain = MemoryDomains![identifierDomain] ?? throw new Exception("unexpted memory domain");
                                                                                if (InstantWriteMap.ContainsKey(domain) && InstantWriteMap[domain].ContainsKey(domainAddress))
                                                                                {
                                                                                    newByte = InstantWriteMap[domain][domainAddress];

                                                                                    if (eventBits != null && eventBits.Length > 0)
                                                                                    {
                                                                                        byte oldByte = domain!.PeekByte(domainAddress);
                                                                                        Log.Note("Debug", $"oldByte: {oldByte:X}, domainAddress: {domainAddress:X}");

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
                                                                                    Log.Note("Debug", $"newByte: {newByte:X}, domainAddress: {domainAddress:X}");
                                                                                    domain!.PokeByte(domainAddress, newByte);
                                                                                }
                                                                            }
                                                                            else if(eventType == MemoryCallbackType.Write)
                                                                            {
                                                                                Log.Note("Debug", $"BizHawkGameHook_{address:X}_{eventType}, eventOffset: {eventOffset}, name: {eventName}, bank: {bank:X}, bits: {string.Join(",", eventBits ?? (new int[0]))}");

                                                                                if (instantaneous)
                                                                                {
                                                                                    uint newValue = value;
                                                                                    byte newByte = Convert.ToByte(newValue);

                                                                                    MemoryDomain domain = MemoryDomains![identifierDomain] ?? throw new Exception("unexpted memory domain");
                                                                                    if (eventBits != null && eventBits.Length > 0)
                                                                                    {
                                                                                        byte oldByte = domain!.PeekByte(domainAddress);
                                                                                        Log.Note("Debug", $"oldByte: {oldByte:X}, domainAddress: {domainAddress:X}");

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
                                                                                    Log.Note("Debug", $"INSTANTANEOUS READ: newByte: {newByte:X}, domainAddress: {domainAddress:X}");
                                                                                    byte curState = InstantReadCurStateMap[ev][address][identifierMemoryDomain][domainAddress];
                                                                                    byte newState = InstantReadNewStateMap[ev][address][identifierMemoryDomain][domainAddress];

                                                                                    if(curState != newState)
                                                                                    {
                                                                                        var addressess = InstantReadNewStateMap[ev].Keys.OrderBy(x => x).ToArray();
                                                                                        // if we are trying to rewrite the same byte twice, then these are new values.
                                                                                        if (newState != newByte)
                                                                                        {
                                                                                            // rewriting the same byte more then once, the value structure has changed record it to the
                                                                                            // list of changes this frame.
                                                                                            List<byte> curValue = new();
                                                                                            
                                                                                            foreach (var curAddress in addressess)
                                                                                            {
                                                                                                foreach (var domainAddressValue in InstantReadNewStateMap[ev][curAddress][identifierMemoryDomain])
                                                                                                {
                                                                                                    var curDomainAddress = domainAddressValue.Key;
                                                                                                    var curDomainValue = domainAddressValue.Value;
                                                                                                    curValue.Add(curDomainValue);
                                                                                                }
                                                                                            }
                                                                                            InstantReadValues[ev].Add(curValue.ToArray());
                                                                                            foreach (var curAddress in addressess)
                                                                                            {
                                                                                                var domainAddressess = InstantReadNewStateMap[ev][curAddress][identifierMemoryDomain].Keys;
                                                                                                foreach (var curDomainAddress in domainAddressess)
                                                                                                {
                                                                                                    InstantReadCurStateMap[ev][curAddress][identifierMemoryDomain][curDomainAddress] = InstantReadNewStateMap[ev][curAddress][identifierMemoryDomain][curDomainAddress];
                                                                                                }
                                                                                            }
                                                                                        }
                                                                                        InstantReadCurStateMap[ev][address][identifierMemoryDomain][domainAddress] = newByte;
                                                                                        InstantReadNewStateMap[ev][address][identifierMemoryDomain][domainAddress] = newByte;
                                                                                        // if all values are changed then this is a new value.
                                                                                        bool allChanged = true;
                                                                                        foreach (var curAddress in addressess)
                                                                                        {
                                                                                            foreach (var curDomainAddress in InstantReadNewStateMap[ev][curAddress][identifierMemoryDomain].Keys)
                                                                                            {
                                                                                                if (InstantReadCurStateMap[ev][curAddress][identifierMemoryDomain][curDomainAddress] != InstantReadNewStateMap[ev][curAddress][identifierMemoryDomain][curDomainAddress])
                                                                                                {
                                                                                                    allChanged = false;
                                                                                                }
                                                                                            }
                                                                                        }
                                                                                        if(allChanged)
                                                                                        {
                                                                                            List<byte> curValue = new();
                                                                                            foreach (var curAddress in addressess)
                                                                                            {
                                                                                                foreach (var domainAddressValue in InstantReadNewStateMap[ev][curAddress][identifierMemoryDomain])
                                                                                                {
                                                                                                    var curDomainAddress = domainAddressValue.Key;
                                                                                                    var curDomainValue = domainAddressValue.Value;
                                                                                                    curValue.Add(curDomainValue);
                                                                                                }
                                                                                            }
                                                                                            InstantReadValues[ev].Add(curValue.ToArray());
                                                                                            foreach (var curAddress in addressess)
                                                                                            {
                                                                                                var domainAddressess = InstantReadNewStateMap[ev][curAddress][identifierMemoryDomain].Keys;
                                                                                                foreach (var curDomainAddress in domainAddressess)
                                                                                                {
                                                                                                    InstantReadCurStateMap[ev][curAddress][identifierMemoryDomain][curDomainAddress] = InstantReadNewStateMap[ev][curAddress][identifierMemoryDomain][curDomainAddress];
                                                                                                }
                                                                                            }
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                    return;
                                                                }
                                                            ),
                                                            Convert.ToUInt32(domainAddress),
                                                            null);
                                    Debuggable!.MemoryCallbacks!.Add(callback);
                                    GameHook_EventCallbacks[ev][evEventType] = GameHook_EventCallbacks[ev][evEventType].Append(callback).ToArray();
                                }
                            }
                        }
                    }
                    if (!GameHook_SerialToEvent.ContainsKey(serialValue))
                        GameHook_SerialToEvent.Add(serialValue, new Dictionary<EventType, EventAddress>());
                    GameHook_SerialToEvent[serialValue].Add(evEventType, ev);

                    break;
                }
            case EventOperationType.EventOperationType_Remove:
                {
                    ulong? serial = e.EventSerial;
                    if (serial == null || !GameHook_SerialToEvent.ContainsKey(serial.Value))
                        throw new NullReferenceException(nameof(serial));
                    EventType evEventType = e.EventType;
                    ulong serialValue = serial.Value;
                    EventAddress ev = GameHook_SerialToEvent[serialValue][evEventType];
                    if ((ev.EventType & (EventType.EventType_SoftReset | EventType.EventType_HardReset)) != 0)
                    {
                        if ((ev.EventType & EventType.EventType_SoftReset) != 0)
                        {
                            if (SoftReset == null)
                            {
                                throw new NullReferenceException(nameof(serial));
                            }
                            else
                            {
                                SoftReset = null;
                            }
                        }
                        if ((ev.EventType & EventType.EventType_HardReset) != 0)
                        {
                            if (HardReset == null)
                            {
                                throw new NullReferenceException(nameof(serial));
                            }
                            else
                            {
                                HardReset = null;
                            }
                        }
                    }
                    else
                    {
                        if (GameHook_EventCallbacks == null || !GameHook_EventCallbacks.ContainsKey(ev))
                            throw new NullReferenceException(nameof(serial));
                        IList<MemoryCallbackDelegate> delegates = new List<MemoryCallbackDelegate>();
                        foreach (var op in GameHook_EventCallbacks[ev][evEventType])
                        {
                            delegates.Add(op.Callback);
                        }
                        Debuggable.MemoryCallbacks.RemoveAll(delegates);
                        GameHook_EventCallbacks[ev].Remove(evEventType);
                        if (GameHook_EventCallbacks[ev].Count <= 0)
                            GameHook_EventCallbacks.Remove(ev);
                    }
                    GameHook_SerialToEvent[serialValue].Remove(evEventType);
                    if (GameHook_SerialToEvent[serialValue].Count <= 0)
                        GameHook_SerialToEvent.Remove(serialValue);

                    if (ev.Instantaneous)
                    {
                        InstantReadCurStateMap.Remove(ev);
                        InstantReadNewStateMap.Remove(ev);
                        InstantReadValues.Remove(ev);
                    }
                    break;
                }
            default:
            case EventOperationType.EventOperationType_Undefined:
                {
                    throw new Exception($"Unexpected event operation type: {e.OpType}");
                }
        }
    }

    private void WriteData(WriteCall data)
    {
        if (Platform == null)
        {
            return;
        }

        //MemoryDomains.First().PokeByte();
        if (data.Active)
        {
            UInt32 address = Convert.ToUInt32(data.Address);
            UInt32 baseAddress = address;
            PlatformMemoryLayoutEntry? platformMemoryLayoutEntry = Platform.FindMemoryLayout(address) ?? throw new Exception("unsupported address");
            MemoryDomain domain = MemoryDomains?[platformMemoryLayoutEntry.BizhawkIdentifier] ?? MemoryDomains?[platformMemoryLayoutEntry.BizhawkAlternateIdentifier] ?? throw new Exception("unsupported adress");
            address -= Convert.ToUInt32(platformMemoryLayoutEntry.PhysicalStartingAddress);

            byte[] bytes = data.WriteByte;
            for (int i = 0; i < bytes.Length; i++)
            {
                domain.PokeByte(address + i, bytes[i]);
            }

            if (data.Frozen)
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
    }

    public override void Restart()
    {
        if (MemoryDomains == null)
        {
            return;
        }

        Log.Note("Debug", "Callback domains:");
        if(Debuggable != null && Debuggable.MemoryCallbacks != null && Debuggable.MemoryCallbacks.AvailableScopes != null)
        {
            foreach(var i in Debuggable.MemoryCallbacks.AvailableScopes)
            {
                Log.Note("Debug", $"scope: {i}");
            }
        }
        foreach(var i in MemoryDomains)
        {
            if (i != null)
            {
                Log.Note("Debug", $"domain: {i.Name}");
                Log.Note("Debug", $"size: 0x{i.Size:X}");
            }
        }
        foreach(var i in Debuggable?.GetCpuFlagsAndRegisters() ?? new Dictionary<string, RegisterValue>())
        {
            Log.Note("Debug", i.Key + ":" + i.Value.Value.ToString());
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

        scopeToEntry = new Dictionary<string, SharedPlatformConstants.PlatformMemoryLayoutEntry>();
        if (Platform != null)
        {
            foreach (var i in Platform.MemoryLayout)
            {
                if (!scopeToEntry.ContainsKey(i.BizhawkAlternateIdentifier))
                    scopeToEntry.Add(i.BizhawkAlternateIdentifier, i);
                if (!scopeToEntry.ContainsKey(i.BizhawkIdentifier))
                    scopeToEntry.Add(i.BizhawkIdentifier, i);
            }
        }
        GameHook_EventCallbacks = new Dictionary<EventAddress, IDictionary<EventType, IMemoryCallback[]>>();
        GameHook_SerialToEvent = new Dictionary<ulong, IDictionary<EventType, EventAddress>>();
        InstantReadCurStateMap = new Dictionary<EventAddress, IDictionary<long, IDictionary<MemoryDomain, IDictionary<long, byte>>>>();
        InstantReadNewStateMap = new Dictionary<EventAddress, IDictionary<long, IDictionary<MemoryDomain, IDictionary<long, byte>>>>();
        InstantReadValues = new();
        InstantReadValuesSet = false;
        eventOperationsQueue = new();

        if (BoardInfo != null && BoardInfo.BoardName != null && Platform != null &&
            Platform.GetMapper != null)
        {
            var boardName = BoardInfo.BoardName;
            var mapper = Platform.GetMapper(Platform, Emulator, MemoryDomains, Debuggable, boardName);

            Log.Note("Info", $"BoardInfo: {boardName}");
            if (mapper != null && mapper.GetMapperName != null)
            {
                Log.Note("Info", $"Mapper: {mapper.GetMapperName()}");
            }
            else
            {
                Log.Note("Info", $"Mapper: null");
            }

            if (mapper != null)
            {
                mapper.InitState?.Invoke(mapper, Platform);
                mapper.InitMapperDetection?.Invoke();
            }

            Mapper = Platform.GetMapper(Platform, Emulator, MemoryDomains, Debuggable, BoardInfo?.BoardName);
        }

        if (Debuggable != null && Debuggable.MemoryCallbacksAvailable())
        {
            Debuggable?.MemoryCallbacks?.Clear();
        }
    }

    protected override void UpdateAfter()
    {
        if (MemoryDomains == null)
        {
            return;
        }

        lock (GameHook_EventLock)
        {
            // check for uncommitted diffs, and add a value for them.
            foreach(EventAddress eventAddress in InstantReadCurStateMap.Keys)
            {
                bool hasDiffs = false;
                long[] addressess = InstantReadCurStateMap[eventAddress].Keys.OrderBy(x => x).ToArray();
                foreach (long address in addressess)
                {
                    foreach(MemoryDomain domain in InstantReadCurStateMap[eventAddress][address].Keys)
                    {
                        foreach(long domainAddress in InstantReadCurStateMap[eventAddress][address][domain].Keys)
                        {
                            if (InstantReadCurStateMap[eventAddress][address][domain][domainAddress] != InstantReadNewStateMap[eventAddress][address][domain][domainAddress])
                            {
                                hasDiffs = true;
                                break;
                            }
                        }
                        if(hasDiffs)
                        {
                            break;
                        }
                    }
                    if(hasDiffs)
                    {
                        break;
                    }
                }
                if(hasDiffs)
                {
                    // assemble a value and replace cur state with new state.
                    List<byte> curValue = new();
                    foreach (var curAddress in addressess)
                    {
                        MemoryDomain[] memoryDomains = InstantReadCurStateMap[eventAddress][curAddress].Keys.ToArray();
                        foreach (var domainAddressValue in InstantReadNewStateMap[eventAddress][curAddress][memoryDomains.First()])
                        {
                            var curDomainAddress = domainAddressValue.Key;
                            var curDomainValue = domainAddressValue.Value;
                            curValue.Add(curDomainValue);
                        }
                    }
                    InstantReadValues[eventAddress].Add(curValue.ToArray());
                    foreach (var curAddress in addressess)
                    {
                        MemoryDomain[] memoryDomains = InstantReadCurStateMap[eventAddress][curAddress].Keys.ToArray();
                        foreach (MemoryDomain memoryDomain in memoryDomains)
                        {
                            var domainAddressess = InstantReadNewStateMap[eventAddress][curAddress][memoryDomain].Keys;
                            foreach (var curDomainAddress in domainAddressess)
                            {
                                InstantReadCurStateMap[eventAddress][curAddress][memoryDomain][curDomainAddress] = InstantReadNewStateMap[eventAddress][curAddress][memoryDomain][curDomainAddress];
                            }
                        }
                    }
                }
            }
            // check for diffs that didn't get a callback event (should not happen.)
            foreach (EventAddress eventAddress in InstantReadCurStateMap.Keys)
            {
                bool hasDiffs = false;
                long[] addressess = InstantReadCurStateMap[eventAddress].Keys.OrderBy(x => x).ToArray();
                foreach (long address in addressess)
                {
                    foreach (MemoryDomain domain in InstantReadCurStateMap[eventAddress][address].Keys)
                    {
                        foreach (long domainAddress in InstantReadCurStateMap[eventAddress][address][domain].Keys)
                        {
                            byte newByte = domain.PeekByte(domainAddress);
                            if (InstantReadNewStateMap[eventAddress][address][domain][domainAddress] != newByte)
                            {
                                hasDiffs = true;
                                InstantReadNewStateMap[eventAddress][address][domain][domainAddress] = newByte;
                            }
                        }
                    }
                }
                if (hasDiffs)
                {
                    // assemble a value and replace cur state with new state.
                    List<byte> curValue = new();
                    foreach (var curAddress in addressess)
                    {
                        MemoryDomain[] memoryDomains = InstantReadCurStateMap[eventAddress][curAddress].Keys.ToArray();
                        foreach (var domainAddressValue in InstantReadNewStateMap[eventAddress][curAddress][memoryDomains.First()])
                        {
                            var curDomainAddress = domainAddressValue.Key;
                            var curDomainValue = domainAddressValue.Value;
                            curValue.Add(curDomainValue);
                        }
                    }
                    InstantReadValues[eventAddress].Add(curValue.ToArray());
                }
            }
        }

        // add and delete events.
        lock (GameHook_EventLock)
        {
            EventOperation e;
            while(eventOperationsQueue.Count > 0)
            {
                e = eventOperationsQueue.Dequeue();
                ProcessEventOperation(e);
            }
        }

        // write the previous frame's events for the remaining events to the pipe.
        lock (GameHook_EventLock)
        {
            if (InstantReadValuesSet && InstantReadValues.Keys.Count > 0 && InstantReadValues.Values.Any(x => x.Count > 0))
            {
                instantReadValuesPipe.Write(InstantReadValues);
                EventAddress[] keys = InstantReadValues.Keys.ToArray();
                InstantReadValues = new();
                foreach (EventAddress key in keys)
                {
                    InstantReadValues.Add(key, new List<byte[]>());
                }
            }
        }

        lock (GameHook_EventLock)
        {
            // update hashes to match current values.
            Tuple<EventAddress, long, MemoryDomain, long>[] InstantReadStateMapKeys = InstantReadCurStateMap
                .Select(x => x.Value
                    .Select(y => y.Value.Select(z => z.Value
                        .Select(a => new Tuple<EventAddress, long, MemoryDomain, long>(x.Key, y.Key, z.Key, a.Key)
                        ).ToArray()).SelectMany(x => x).ToArray()
                    ).SelectMany(x => x).ToArray()
                ).SelectMany(x => x).ToArray();
            
            foreach(var i in InstantReadStateMapKeys)
            {
                EventAddress eventAddress = i.Item1;
                long address = i.Item2;
                MemoryDomain memoryDomain = i.Item3;
                long domainAddress = i.Item4;

                byte newByte = memoryDomain.PeekByte(domainAddress);
                InstantReadCurStateMap[eventAddress][address][memoryDomain][domainAddress] = InstantReadNewStateMap[eventAddress][address][memoryDomain][domainAddress] = newByte;
            }

            foreach (var i in InstantReadCurStateMap)
            {
                EventAddress eventAddress = i.Key;

                // assemble a value and replace cur state with new state.
                List<byte> curValue = new();
                foreach (var j in i.Value)
                {
                    long curAddress = j.Key;
                    MemoryDomain[] memoryDomains = j.Value.Keys.ToArray();
                    foreach (var k in j.Value[memoryDomains.First()])
                    {
                        var curDomainAddress = k.Key;
                        var curDomainValue = k.Value;
                        curValue.Add(curDomainValue);
                    }
                }
                InstantReadValues[eventAddress].Add(curValue.ToArray());
            }
            InstantReadValuesSet = true;
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

            WriteCall data;
            while (writeCallsQueue.Count > 0)
            {
                data = writeCallsQueue.Dequeue();
                WriteData(data);
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
                        Log.Note("Debug", "camera: rom bank: " + cameraMapper.ROM_bank.ToString()
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
                        Log.Note("Debug", "HUC1: rom bank: " + huc1Mapper.ROM_bank.ToString()
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
                        Log.Note("Debug", "HUC3: rom bank: " + huc3Mapper.ROM_bank.ToString()
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
                        Log.Note("Debug", "MBC1: rom bank: " + mbc1Mapper.ROM_bank.ToString()
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
                        Log.Note("Debug", "MBC1M: rom bank: " + mbc1mMapper.ROM_bank.ToString()
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
                        Log.Note("Debug", "MBC2: rom bank: " + mbc2Mapper.ROM_bank.ToString()
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
                        Log.Note("Debug", "MBC3: rom bank: " + mbc3Mapper.ROM_bank.ToString()
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
                        Log.Note("Debug", "MBC5: rom bank: " + mbc5Mapper.ROM_bank.ToString()
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
                        Log.Note("Debug", "MBC7: rom bank: " + mbc7Mapper.ROM_bank.ToString()
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
                        Log.Note("Debug", "RM8: rom bank: " + mapperRM8.ROM_bank.ToString());
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
                    //    Log.Note("Debug", "Sachen1: rom bank: " + mapperSachen1.ROM_bank.ToString()
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
                    //    Log.Note("Debug", "Sachen2: rom bank: " + mapperSachen2.ROM_bank.ToString()
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
                            Log.Note("Debug", "TAMA5: rom bank: " + mapperTAMA5.ROM_bank.ToString()
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
                            Log.Note("Debug", "WT: rom bank: " + mapperWT.ROM_bank.ToString());
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