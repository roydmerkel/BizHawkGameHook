using GameHook.Domain.Interfaces;
using System.Xml.Linq;

namespace GameHook.Domain;

public static class MapperXmlExtensions
{
    static string ReplaceStart(this string input, string oldValue, string newValue)
    {
        if (input.StartsWith(oldValue))
        {
            return string.Concat(newValue, input.AsSpan(oldValue.Length));
        }
        else
        {
            return input;
        }
    }

    static string ReplaceEnd(this string input, string oldValue, string newValue)
    {
        if (input.EndsWith(oldValue))
        {
            return string.Concat(input.AsSpan(0, input.Length - oldValue.Length), newValue);
        }
        else
        {
            return input;
        }
    }

    public static string GetAttributeValue(this XElement el, string name) =>
        el.Attribute(name)?.Value ?? throw new Exception($"Node does not have required '{name}' attribute. {el}");

    public static string? GetOptionalAttributeValue(this XElement el, string name) =>
        el.Attribute(name)?.Value;

    public static int? GetOptionalAttributeValueAsInt(this XElement el, string name) =>
        el.Attribute(name) != null ? int.Parse(el.GetAttributeValue(name)) : null;

    public static bool? GetOptionalAttributeValueAsBool(this XElement el, string name)
    {
        XAttribute? xAttribute = el.Attribute(name);

        if(xAttribute != null)
        {
            string value = el.GetAttributeValue(name);

            if(value != null)
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
            else
            {
                return null;
            }
        }
        else
        {
            return null;
        }
    }

    public static EventType? GetOptionalAttributeValueAsEventType(this XElement el, string name)
    {
        XAttribute? xAttribute = el.Attribute(name);

        if (xAttribute != null)
        {
            string value = el.GetAttributeValue(name);

            if (value != null)
            {
                switch (value.ToLower())
                {
                    case "read":
                        return EventType.EventType_Read;
                    case "write":
                        return EventType.EventType_Write;
                    case "execute":
                        return EventType.EventType_Execute;
                    case "read_execute":
                        return EventType.EventType_ReadExecute;
                    case "read_write":
                        return EventType.EventType_ReadWrite;
                    case "write_execute":
                        return EventType.EventType_WriteExecute;
                    case "read_write_execute":
                    case "access":
                        return EventType.EventType_ReadWriteExecute;
                    default:
                        throw new FormatException();
                }
            }
            else
            {
                return null;
            }
        }
        else
        {
            return null;
        }
    }

    public static EventType GetAttributeValueAsEventType(this XElement el, string name) =>
        el.GetOptionalAttributeValueAsEventType(name) ?? throw new Exception($"Node does not have required '{name}' attribute. {el}");

    public static bool IsArray(this XElement el)
    {
        var childElements = el.Elements().Select(x => x.GetOptionalAttributeValue("name") ?? string.Empty).ToArray();

        // Check if all child elements are numbers
        if (childElements.Any(e => int.TryParse(e, out _)) == false) {
            return false;
        }

        // Check if numbers are in sequence
        var sortedElements = childElements.OrderBy(e => int.Parse(e)).ToList();
        for (var i = 1; i < sortedElements.Count; i++)
        {
            var current = int.Parse(sortedElements[i]);
            var previous = int.Parse(sortedElements[i - 1]);

            if (current != previous + 1) return false;
        }

        return true;
    }

    public static bool IsParentAnArray(this XElement el)
    {
        return el.Parent?.IsArray() ?? false;
    }
    
    public static string? GetElementActualName(this XElement el)
    {
        if (el.Name.LocalName is "property" || el.Name.LocalName is "class")
        {
            return el.GetAttributeValue("name").Replace("-", string.Empty);
        }
        else if (el.Name.LocalName is "group")
        {
            return el.GetAttributeValue("name").Replace("-", string.Empty);
        }
        else
        {
            return el.Name.LocalName.Replace("-", string.Empty);
        }
    }

    static string? GetElementPathName(this XElement el)
    {
        if (el.Name.LocalName is "class")
        {
            return el.GetAttributeValue("name");
        }
        else if(el.Name.LocalName is "group")
        {
            return el.GetAttributeValue("name");
        }
        else
        {
            return el.Name.LocalName;
        }
    }

    public static string GetElementPath(this XElement el)
    {
        var elementName = el.Attribute("name")?.Value;
        
        return el
                   .AncestorsAndSelf().InDocumentOrder().Reverse()
                   .Aggregate("", (s, xe) => xe.GetElementPathName() + "." + s)
                   .ReplaceStart("mapper.properties.", string.Empty).ReplaceEnd(".property.", string.Empty) +
               $".{elementName}";
    }
}