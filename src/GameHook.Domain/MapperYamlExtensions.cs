namespace GameHook.Domain;

using System;

public static class MapperYamlExtensions
{
    public static ulong ParseIntOrHex(this string type, string value)
    {
        try
        {
            return Convert.ToUInt64(value);
        }
        catch
        {
            return Convert.ToUInt64(value, 16);
        }
    }

    public static bool TryParseHex(this string type, string value, out ulong result)
    {
        result = 0;
        try
        {
            result = Convert.ToUInt64(value, 16);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryParseBool(this string value, out bool result)
    {
        result = false;
        if(value != null)
        {
            switch (value.ToLower())
            {
                case "true":
                case "yes":
                case "t":
                    result = true;
                    return true;
                case "false":
                case "no":
                case "f":
                    result = false;
                    return true;
                default:
                    if (long.TryParse(value, out long lValue))
                    {
                        result = (lValue != 0);
                        return true;
                    }
                    else if (ulong.TryParse(value, out ulong ulValue))
                    {
                        result = (ulValue != 0);
                        return true;
                    }
                    else if (double.TryParse(value, out double dValue))
                    {
                        result = (dValue != 0.0 && dValue != -0.0);
                        return true;
                    }
                    else if (decimal.TryParse(value, out decimal dcValue))
                    {
                        result = (dcValue != 0.0m && dcValue != -0.0m);
                        return true;
                    }
                    return false;
            }
        }
        else
        {
            return false;
        }
    }

    public static bool ParseBool(this string value)
    {
        if(value == null)
        {
            throw new ArgumentNullException();
        }
        else
        {
            switch (value.ToLower())
            {
                case "true":
                case "yes":
                case "t":
                    return true;
                case "false":
                case "no":
                case "f":
                    return false;
                default:
                    if (long.TryParse(value, out long lValue))
                    {
                        return (lValue != 0);
                    }
                    else if (ulong.TryParse(value, out ulong ulValue))
                    {
                        return (ulValue != 0);
                    }
                    else if (double.TryParse(value, out double dValue))
                    {
                        return (dValue != 0.0 && dValue != -0.0);
                    }
                    else if (decimal.TryParse(value, out decimal dcValue))
                    {
                        return (dcValue != 0.0m && dcValue != -0.0m);
                    }
                    throw new FormatException();
            }
        }
    }
}