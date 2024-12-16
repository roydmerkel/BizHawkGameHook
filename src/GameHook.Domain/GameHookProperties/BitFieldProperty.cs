using GameHook.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections;

namespace GameHook.Domain.GameHookProperties
{
    public class BitFieldProperty(ILogger logger, IGameHookInstance instance, PropertyAttributes variables) : GameHookProperty(logger, instance, variables), IGameHookProperty
    {
        protected override byte[] FromValue(string value)
        {
            throw new NotImplementedException();
        }

        protected override object? ToValue(byte[] data)
        {
            var bitArray = new BitArray(data);

            var boolArray = new bool[bitArray.Length];
            bitArray.CopyTo(boolArray, 0);

            return boolArray;
        }
    }
}
