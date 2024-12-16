using GameHook.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace GameHook.Domain.GameHookProperties
{
    public class BinaryCodedDecimalProperty(ILogger logger, IGameHookInstance instance, PropertyAttributes variables) : GameHookProperty(logger, instance, variables), IGameHookProperty
    {
        protected override byte[] FromValue(string value)
        {
            throw new NotImplementedException();
        }

        protected override object? ToValue(byte[] data)
        {
            int result = 0;

            foreach (byte bcd in data)
            {
                result *= 100;
                result += 10 * (bcd >> 4);
                result += bcd & 0xf;
            }

            return result;
        }
    }
}
