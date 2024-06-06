using GameHook.Domain.Interfaces;

namespace GameHook.Domain.GameHookEvents
{
    public abstract partial class GameHookEvent : IGameHookEvent
    {
        private string? _memoryContainer { get; set; }
        private uint? _address { get; set; }
        private uint? _oldAddress { get; set; }
        private ushort? _bank { get; set; }
        private ushort? _oldBank { get; set; }
        private string? _addressString { get; set; }
        private string? _description { get; set; }
        private int? _length { get; set; }
        private int? _size { get; set; }
        private string? _bits { get; set; }

        public string? MemoryContainer
        {
            get { return _memoryContainer; }
            set
            {
                if (value == _memoryContainer) { return; }

                FieldsChanged.Add("memoryContainer");
                _memoryContainer = value;
            }
        }

        public uint? Address
        {
            get { return _address; }
            set
            {
                if (value == _address) { return; }

                _address = value;
                _addressString = value.ToString();

                IsMemoryAddressSolved = true;
                if (_address == null && _oldAddress != null)
                {
                    ClearEvent(this);
                    _oldAddress = _address;
                }
                else if (_address != null && _oldAddress == null)
                {
                    _oldAddress = _address;
                    SetEvent(this);
                }
                else if (_address != null && _oldAddress != null && _oldAddress != _address)
                {
                    ClearEvent(this);
                    _oldAddress = _address;
                    SetEvent(this);
                }

                FieldsChanged.Add("address");
            }
        }

        public string? AddressString
        {
            get { return _addressString; }
            set
            {
                if (value == _addressString) { return; }

                _addressString = value;

                IsMemoryAddressSolved = AddressMath.TrySolve(value, [], out var solvedAddress);

                if (IsMemoryAddressSolved == false)
                {
                    _address = null;
                }
                else
                {
                    _address = solvedAddress;
                }

                if (_address == null && _oldAddress != null)
                {
                    ClearEvent(this);
                    _oldAddress = _address;
                }
                else if (_address != null && _oldAddress == null)
                {
                    _oldAddress = _address;
                    SetEvent(this);
                }
                else if (_address != null && _oldAddress != null && _oldAddress != _address)
                {
                    ClearEvent(this);
                    _oldAddress = _address;
                    SetEvent(this);
                }

                FieldsChanged.Add("address");
            }
        }

        public string? Description
        {
            get => _description;
            set
            {
                if (_description == value) return;

                FieldsChanged.Add("description");
                _description = value;
            }
        }

        public ushort? Bank
        {
            get => _bank;
            set
            {
                if (value == _bank) { return; }

                _bank = value;

                if (_bank == null && _oldBank != null)
                {
                    if (_address != null)
                    {
                        ClearEvent(this);
                    }
                    _oldBank = _bank;
                }
                else if (_bank != null && _oldBank == null)
                {
                    _oldBank = _bank;
                    if (_address != null)
                    {
                        SetEvent(this);
                    }
                }
                else if (_bank != null && _oldBank != null && _oldBank != _bank)
                {
                    if (_address != null)
                    {
                        ClearEvent(this);
                    }
                    _oldBank = _bank;
                    if (_address != null)
                    {
                        SetEvent(this);
                    }
                }

                FieldsChanged.Add("bank");
            }
        }

        public int? Length
        {
            get => _length;
            set
            {
                if (_length == value) return;

                FieldsChanged.Add("length");
                _length = value;
            }
        }

        public int? Size
        {
            get => _size;
            set
            {
                if (_size == value) return;

                FieldsChanged.Add("size");
                _size = value;
            }
        }

        public string? Bits
        {
            get => _bits;
            set
            {
                if (_bits == value) return;

                FieldsChanged.Add("bits");
                _bits = value;
            }
        }
    }
}
