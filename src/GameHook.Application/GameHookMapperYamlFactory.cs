﻿using GameHook.Domain;
using GameHook.Domain.GameHookProperties;
using GameHook.Domain.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Tokens;
using YamlDotNet.Serialization;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace GameHook.Application
{
    public record YamlRoot
    {
        public YamlRoot()
        {
            meta = new();
            properties = new Dictionary<object, object>();
            macros = new Dictionary<string, IDictionary<object, dynamic>>();
            glossary = new Dictionary<string, IDictionary<string, dynamic>>();
        }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        public YamlMeta meta { get; init; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        public IDictionary<object, object> properties { get; init; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        public IDictionary<string, IDictionary<object, dynamic>> macros { get; init; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        public IDictionary<string, IDictionary<string, dynamic>> glossary { get; init; }
    }

    public record YamlMeta
    {
        public YamlMeta()
        {
            schemaVersion = 0;
            id = Guid.Empty;
            gameName = "";
            gamePlatform = "";
        }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        public int schemaVersion { get; init; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        public Guid id { get; init; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        public string gameName { get; init; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        public string gamePlatform { get; init; }
    }

    public record MacroEntry
    {
        public MacroEntry()
        {
            type = "";
            address = null;
            macro = "";
            reference = null;
            length = null;
        }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        public string type { get; init; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        public int? address { get; init; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        public string macro { get; init; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        public string? reference { get; init; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        public int? length { get; init; }
    }

    public record MacroPointer
    {
        public string? Address { get; set; }
    }

    public static partial class GameHookMapperYamlFactory
    {
        public static YamlRoot Deserialize(string mapperContents)
        {
            var deserializer = new DeserializerBuilder().Build();
            var data = deserializer.Deserialize<YamlRoot>(mapperContents);

            return data;
        }

        private static readonly string[] ConvertYamlToXMLIgnoreAttrs = ["offset", "address", "macro"];
        private static readonly string[] ConvertYamlToXMLIgnoreMacroParentAttrs = ["type", "class"];
        public static void ConvertYamlToXMLIter(XmlDocument doc, dynamic root, XmlElement xmlRoot, bool inMacros, bool inClass)
        {
            if(root is IDictionary<object, dynamic> || 
                root is IDictionary<string, IDictionary<object, dynamic>>)
            {
                foreach (var kv in root)
                {
                    object key = kv.Key;
                    dynamic value = kv.Value;

                    if(key is not null && key is string keyString && IsMerge(keyString))
                    {
                        if(value is IDictionary<dynamic, dynamic> valueDict)
                        {
                            if (!IsProperty(valueDict, inMacros, inClass))
                            {
                                throw new Exception("macro isn't a valid property or class");
                            }
                            else
                            {
                                KeyValuePair<object, dynamic>? macroKVQ = GetPropertyValueKVByKey(valueDict, "macro");
                                KeyValuePair<object, dynamic>? offsetKVQ = GetPropertyValueKVByKey(valueDict, "offset");
                                KeyValuePair<object, dynamic>? addressKVQ = GetPropertyValueKVByKey(valueDict, "address");
                                KeyValuePair<object, dynamic>? typeKVQ = GetPropertyValueKVByKey(valueDict, "type");

                                if(macroKVQ is null)
                                {
                                    throw new Exception("macro isn't defined");
                                } 
                                else if(offsetKVQ is null && addressKVQ is null)
                                {
                                    throw new Exception("address and offset are null");
                                }
                                else if(typeKVQ is null)
                                {
                                    throw new Exception("type is null");
                                }
                                else
                                {
                                    KeyValuePair<object, dynamic> macroKV = macroKVQ.Value;
                                    KeyValuePair<object, dynamic> typeKV = typeKVQ.Value;

                                    string macro;
                                    string? offset = null;
                                    string? address = null;
                                    string type;

                                    if (macroKV.Value is string macroVal)
                                    {
                                        macro = macroVal;
                                    }
                                    else
                                    {
                                        throw new Exception("unexpected macro type.");
                                    }

                                    if (typeKV.Value is string typeVal)
                                    {
                                        type = typeVal;
                                    }
                                    else
                                    {
                                        throw new Exception("unexpected macro type.");
                                    }

                                    if (offsetKVQ != null && offsetKVQ.Value.Value is string offsetVal)
                                    {
                                        offset = offsetVal;
                                    }
                                    else if (addressKVQ != null && addressKVQ.Value.Value is string addressVal)
                                    {
                                        address = addressVal;
                                    }
                                    else
                                    {
                                        throw new Exception("address and offset are unexpected types.");
                                    }

                                    if(type != "macro")
                                    {
                                        throw new Exception("unexpected type.");
                                    }

                                    XmlElement macroElem = doc.CreateElement("macro");
                                    XmlAttribute typeAttr = doc.CreateAttribute("type");
                                    typeAttr.Value = macro;
                                    macroElem.SetAttributeNode(typeAttr);

                                    if(offset != null)
                                    {
                                        XmlAttribute offsetAttr = doc.CreateAttribute("var", "address", "https://schemas.gamehook.io/attributes/var");
                                        offset = "".ParseIntOrHex(offset).ToString();
                                        string addr = "{address} + " + offset;
                                        addr = AddressRegex().Replace(addr, "{address}");
                                        offsetAttr.Value = addr;
                                        macroElem.SetAttributeNode(offsetAttr);
                                    }
                                    else if(address != null)
                                    {
                                        XmlAttribute addressAttr = doc.CreateAttribute("var", "address", "https://schemas.gamehook.io/attributes/var");
                                        address = "".ParseIntOrHex(address).ToString();
                                        addressAttr.Value = address;
                                        macroElem.SetAttributeNode(addressAttr);
                                    }
                                    xmlRoot.AppendChild(macroElem);
                                    ConvertYamlToXMLIter(doc, value, macroElem, inMacros, inClass);
                                }
                            }
                        }
                        else
                        {
                            throw new Exception("unexpected type.");
                        }
                    }
                    else if(IsProperty(value, inMacros, inClass))
                    {
                        if (value is IDictionary<dynamic, dynamic> valueDict)
                        {
                            if (key is not null && key is string kString)
                            {
                                XmlElement property = doc.CreateElement("property");
                                XmlAttribute nameAttr = doc.CreateAttribute("name");
                                nameAttr.Value = kString;
                                property.SetAttributeNode(nameAttr);

                                KeyValuePair<object, dynamic>? offsetKVQ = GetPropertyValueKVByKey(valueDict, "offset");
                                KeyValuePair<object, dynamic>? addressKVQ = GetPropertyValueKVByKey(valueDict, "address");

                                if (offsetKVQ != null && offsetKVQ.Value.Value is string offset)
                                {
                                    XmlAttribute offsetAttr = doc.CreateAttribute("address");
                                    offset = "".ParseIntOrHex(offset).ToString();
                                    string addr = "{address} + " + offset;
                                    addr = AddressRegex().Replace(addr, "{address}");
                                    offsetAttr.Value = addr;
                                    property.SetAttributeNode(offsetAttr);
                                }
                                else if (addressKVQ != null && addressKVQ.Value.Value is string address)
                                {
                                    XmlAttribute addressAttr = doc.CreateAttribute("address");
                                    address = "".ParseIntOrHex(address).ToString();
                                    addressAttr.Value = address;
                                    property.SetAttributeNode(addressAttr);
                                }

                                xmlRoot.AppendChild(property);
                                ConvertYamlToXMLIter(doc, value, property, inMacros, inClass);
                            }
                            else
                            {
                                throw new Exception("Key is null");
                            }
                        }
                        else
                        {
                            throw new Exception("unexpected type.");
                        }
                    }
                    else if(value is IDictionary<dynamic, dynamic> || value is IList<dynamic> || value is IList<object>)
                    {
                        if(key is null)
                        {
                            throw new Exception("missing key");
                        }
                        else if(key is string keyStr)
                        {
                            XmlElement elem = doc.CreateElement(keyStr);
                            xmlRoot.AppendChild(elem);
                            ConvertYamlToXMLIter(doc, value, elem, inMacros, inClass);
                        }
                        else
                        {
                            throw new Exception("key is not a string");
                        }
                    }
                    else
                    {
                        if (key is null)
                        {
                            throw new Exception("missing key");
                        }
                        else if (key is string keyStr)
                        {
                            if(!ConvertYamlToXMLIgnoreAttrs.Contains(keyStr) && ((xmlRoot.Name != "macro" && xmlRoot.Name != "class") || !ConvertYamlToXMLIgnoreMacroParentAttrs.Contains(keyStr)))
                            {
                                if (keyStr is not "position" || GetPropertyValueKVByKey((IDictionary<dynamic, dynamic>)root, "type") is null || (GetPropertyValueKVByKey((IDictionary<dynamic, dynamic>)root, "type")!.Value.Value.ToString() is not "nibble" && GetPropertyValueKVByKey((IDictionary<dynamic, dynamic>)root, "type")!.Value.Value.ToString() is not "bit"))
                                {
                                    XmlAttribute attr = doc.CreateAttribute(keyStr);
                                    if (keyStr == "length")
                                    {
                                        // length and size are swapped in YAML
                                        attr = doc.CreateAttribute("size");
                                    }
                                    else if (keyStr == "size")
                                    {
                                        // length and size are swapped in YAML
                                        attr = doc.CreateAttribute("length");
                                    }
                                    else if (keyStr == "preprocessor")
                                    {
                                        //BeforeReadValueExpression
                                        attr = doc.CreateAttribute("before-read-value-expression");
                                    }
                                    else if (keyStr == "postprocessor" || keyStr == "postprocessorReader")
                                    {
                                        //AfterReadValueExpression
                                        attr = doc.CreateAttribute("after-read-value-expression");
                                    }
                                    else if (keyStr == "postprocessorWriter" || keyStr == "afterWriteValueExpression")
                                    {
                                        //AfterWriteValueExpression
                                        attr = doc.CreateAttribute("after-write-value-expression");
                                    }
                                    else if (keyStr == "type")
                                    {
                                        switch (value.ToString())
                                        {
                                            case "bit":
                                                value = "bool";
                                                KeyValuePair<object, dynamic>? bitPositionKVQ = GetPropertyValueKVByKey((IDictionary<dynamic, dynamic>)root, "position");
                                                if (bitPositionKVQ != null)
                                                {
                                                    KeyValuePair<object, dynamic> bitPositionKV = bitPositionKVQ.Value;
                                                    XmlAttribute bitsAttr = doc.CreateAttribute("bits");
                                                    bitsAttr.Value = bitPositionKV.Value.ToString();
                                                    xmlRoot.SetAttributeNode(bitsAttr);
                                                }
                                                else
                                                {
                                                    throw new Exception("position is not defined for bit type.");
                                                }
                                                break;
                                            case "nibble":
                                                value = "int";
                                                KeyValuePair<object, dynamic>? nibblePositionKVQ = GetPropertyValueKVByKey((IDictionary<dynamic, dynamic>)root, "position");
                                                if (nibblePositionKVQ != null)
                                                {
                                                    KeyValuePair<object, dynamic> nibblePositionKV = nibblePositionKVQ.Value;
                                                    XmlAttribute bitsAttr = doc.CreateAttribute("bits");
                                                    bitsAttr.Value = nibblePositionKV.Value.ToString().ToLower() switch
                                                    {
                                                        "0" => "4-7",
                                                        "1" => "0-3",
                                                        "high" => "4-7",
                                                        "low" => "0-3",
                                                        _ => throw new Exception("unsupported position."),
                                                    };
                                                    xmlRoot.SetAttributeNode(bitsAttr);
                                                }
                                                else
                                                {
                                                    throw new Exception("position is not defined for nibble type.");
                                                }
                                                break;
                                        }
                                    }
                                    attr.Value = value.ToString();
                                    xmlRoot.SetAttributeNode(attr);
                                }
                            }
                        }
                        else
                        {
                            throw new Exception("key is not a string");
                        }
                    }
                }
            }
            else if(root is IList<dynamic>
                || root is IList<object>)
            {
                var rootList = (root is IList<dynamic> list) ? list : (IList<object>)root;
                for(int key = 0; key < rootList.Count; key++)
                {
                    dynamic value = rootList[key];
                    if(IsProperty(value, inMacros, inClass))
                    {
                        string name = key.ToString();

                        if (value is not null && value is IDictionary<dynamic, dynamic> dict)
                        {
                            if (dict.ContainsKey("type") && dict["type"] is string type && type == "macro")
                            {
                                // macro in list.
                                KeyValuePair<object, dynamic>? macroKVQ = GetPropertyValueKVByKey(dict, "macro");
                                KeyValuePair<object, dynamic>? offsetKVQ = GetPropertyValueKVByKey(dict, "offset");
                                KeyValuePair<object, dynamic>? addressKVQ = GetPropertyValueKVByKey(dict, "address");

                                if (macroKVQ is null)
                                {
                                    throw new Exception("macro isn't defined");
                                }
                                else if (offsetKVQ is null && addressKVQ is null)
                                {
                                    throw new Exception("address and offset are null");
                                }
                                else
                                {
                                    KeyValuePair<object, dynamic> macroKV = macroKVQ.Value;
                                    
                                    string macro;
                                    string? offset = null;
                                    string? address = null;

                                    if (macroKV.Value is string macroVal)
                                    {
                                        macro = macroVal;
                                    }
                                    else
                                    {
                                        throw new Exception("unexpected macro type.");
                                    }

                                    if (offsetKVQ != null && offsetKVQ.Value.Value is string offsetVal)
                                    {
                                        offset = offsetVal;
                                    }
                                    else if (addressKVQ != null && addressKVQ.Value.Value is string addressVal)
                                    {
                                        address = addressVal;
                                    }
                                    else
                                    {
                                        throw new Exception("address and offset are unexpected types.");
                                    }

                                    if (type != "macro")
                                    {
                                        throw new Exception("unexpected type.");
                                    }

                                    XmlElement macroElem = doc.CreateElement("macro");
                                    XmlAttribute typeAttr = doc.CreateAttribute("type");
                                    typeAttr.Value = macro;
                                    macroElem.SetAttributeNode(typeAttr);

                                    if (offset != null)
                                    {
                                        XmlAttribute offsetAttr = doc.CreateAttribute("var", "address", "https://schemas.gamehook.io/attributes/var");
                                        offset = "".ParseIntOrHex(offset).ToString();
                                        string addr = "{address} + " + offset;
                                        addr = AddressRegex().Replace(addr, "{address}");
                                        offsetAttr.Value = addr;
                                        macroElem.SetAttributeNode(offsetAttr);
                                    }
                                    else if (address != null)
                                    {
                                        XmlAttribute addressAttr = doc.CreateAttribute("var", "address", "https://schemas.gamehook.io/attributes/var");
                                        address = "".ParseIntOrHex(address).ToString();
                                        addressAttr.Value = address;
                                        macroElem.SetAttributeNode(addressAttr);
                                    }


                                    XmlElement macroParentElem = doc.CreateElement("group");
                                    XmlAttribute nameAttr = doc.CreateAttribute("name");
                                    nameAttr.Value = name;
                                    macroParentElem.SetAttributeNode(nameAttr);

                                    xmlRoot.AppendChild(macroParentElem);
                                    macroParentElem.AppendChild(macroElem);

                                    ConvertYamlToXMLIter(doc, value, macroElem, inMacros, inClass);
                                }
                            }
                            else
                            {
                                // property in list.
                                XmlElement property = doc.CreateElement("property");
                                XmlAttribute nameAttr = doc.CreateAttribute("name");
                                nameAttr.Value = name;
                                property.SetAttributeNode(nameAttr);

                                KeyValuePair<object, dynamic>? offsetKVQ = GetPropertyValueKVByKey(dict, "offset");
                                KeyValuePair<object, dynamic>? addressKVQ = GetPropertyValueKVByKey(dict, "address");

                                if (offsetKVQ != null && offsetKVQ.Value.Value is string offset)
                                {
                                    XmlAttribute offsetAttr = doc.CreateAttribute("address");
                                    offset = "".ParseIntOrHex(offset).ToString();
                                    string addr = "{address} + " + offset;
                                    addr = AddressRegex().Replace(addr, "{address}");
                                    offsetAttr.Value = addr;
                                    property.SetAttributeNode(offsetAttr);
                                }
                                else if (addressKVQ != null && addressKVQ.Value.Value is string address)
                                {
                                    XmlAttribute addressAttr = doc.CreateAttribute("address");
                                    address = "".ParseIntOrHex(address).ToString();
                                    addressAttr.Value = address;
                                    property.SetAttributeNode(addressAttr);
                                }

                                xmlRoot.AppendChild(property);
                                ConvertYamlToXMLIter(doc, value, property, inMacros, inClass);
                            }
                        }
                        else
                        {
                            throw new Exception("unexpected type, IsProperty should only be true for dictionaries.");
                        }
                    }
                    else if (value is IDictionary<object, dynamic> valueDict)
                    {
                        XmlElement elem;
                        if(valueDict.Any(x => x.Key is not null && x.Key is string xKey && xKey == "class" && x.Value is string))
                        {
                            elem = doc.CreateElement("class");
                            XmlAttribute name = doc.CreateAttribute("name");
                            name.Value = key.ToString();
                            elem.SetAttributeNode(name);

                            XmlAttribute type = doc.CreateAttribute("type");
                            type.Value = valueDict.Where(x => x.Key is not null && x.Key is string xKey && xKey == "class" && x.Value is string).Select(x => (string)x.Value).First();
                            elem.SetAttributeNode(type);
                        }
                        else
                        {
                            elem = doc.CreateElement("group");
                            XmlAttribute name = doc.CreateAttribute("name");
                            name.Value = key.ToString();
                            elem.SetAttributeNode(name);
                        }
                        xmlRoot.AppendChild(elem);
                        ConvertYamlToXMLIter(doc, value, elem, inMacros, inClass);
                    }
                    else if(value is IList<dynamic> || value is IList<object>)
                    {
                        Console.Out.WriteLine("list");
                    }
                    else
                    {
                        //ConvertYamlToXMLIter(doc, value, xmlRoot, inMacros, inClass);
                        throw new Exception("Unexpected element");
                    }
                }
            }
            else if(root is null)
            {
                throw new Exception("Unexpected null.");
            }
            else
            {
                throw new Exception("Unexpected type:" + root.GetType().FullName);
            }
        }

        private static void RenameNode(XmlNode oldNode, string newChildName)
        {
            var newnode = (oldNode?.OwnerDocument != null) ? oldNode.OwnerDocument.CreateNode(XmlNodeType.Element, newChildName, "") : null;

            List<XmlAttribute> attributeList = (oldNode != null && oldNode.Attributes != null) ? oldNode.Attributes.Cast<XmlAttribute>().ToList() : [];
            List<XmlAttribute> l = [];
            foreach (XmlAttribute attr in attributeList)
            {
                newnode!.Attributes!.Append(attr);
            }

            List<XmlNode> relocateChildrenList = (oldNode != null && oldNode.ChildNodes != null) ? oldNode.ChildNodes.Cast<XmlNode>().ToList() : [];
            foreach(XmlNode node in relocateChildrenList)
            {
                newnode!.AppendChild(node);
            }

            if (newnode != null && oldNode?.ParentNode != null)
            {
                oldNode.ParentNode.ReplaceChild(newnode, oldNode);
            }
        }

        public static XmlDocument ConvertYamlToXML(YamlRoot data)
        {
            XmlDocument xmlDocument = new();

            /* create the mapper root, munging the metadata */
            XmlAttribute xmlns_xsi = xmlDocument.CreateAttribute("xmlns", "xsi", "http://www.w3.org/2000/xmlns/");
            xmlns_xsi.Value = "http://www.w3.org/2001/XMLSchema-instance";

            XmlAttribute xsi_schemaLocation = xmlDocument.CreateAttribute("xsi", "schemaLocation", "http://www.w3.org/2001/XMLSchema-instance");
            xsi_schemaLocation.Value = "https://schemas.gamehook.io/mapper https://schemas.gamehook.io/mapper.xsd";

            XmlAttribute xmlns_varAttr = xmlDocument.CreateAttribute("xmlns", "var", "http://www.w3.org/2000/xmlns/");
            xmlns_varAttr.Value = "https://schemas.gamehook.io/attributes/var";

            XmlElement mapper = xmlDocument.CreateElement("mapper");

            mapper.SetAttribute("id", data.meta.id.ToString());
            mapper.SetAttribute("name", data.meta.gameName);
            mapper.SetAttribute("platform", data.meta.gamePlatform);
            mapper.SetAttributeNode(xmlns_xsi);
            mapper.SetAttributeNode(xsi_schemaLocation);
            mapper.SetAttributeNode(xmlns_varAttr);

            xmlDocument.AppendChild(mapper);

            /* append the base parents. */
            XmlElement macros = xmlDocument.CreateElement("macros");
            mapper.AppendChild(macros);
            XmlElement classes = xmlDocument.CreateElement("classes");
            mapper.AppendChild(classes);
            XmlElement properties = xmlDocument.CreateElement("properties");
            mapper.AppendChild(properties);
            XmlElement references = xmlDocument.CreateElement("references");
            mapper.AppendChild(references);

            /* process the macros */
            ConvertYamlToXMLIter(xmlDocument, data.macros, macros, true, false);

            /* process the properties */
            ConvertYamlToXMLIter(xmlDocument, data.properties, properties, false, false);

            foreach(KeyValuePair<string, IDictionary<string, dynamic>> glossaryDefinition in data.glossary)
            {
                string key = glossaryDefinition.Key;
                IDictionary<string, dynamic> value = glossaryDefinition.Value;
                string type = "";
                foreach(KeyValuePair<string, dynamic> glossaryItem in value)
                {
                    if (glossaryItem.Key == "type")
                    {
                        if (glossaryItem.Value is string posClass)
                        {
                            if(type != "")
                            {
                                throw new Exception("type already defined.");
                            }
                            else
                            {
                                type = posClass;
                            }
                        }
                        else
                        {
                            throw new Exception("Unexpected glossary type.");
                        }
                    }
                }

                XmlElement reference = xmlDocument.CreateElement(key);
                if(type != "")
                {
                    XmlAttribute typeAttribute = xmlDocument.CreateAttribute("type");
                    typeAttribute.Value = type;

                    reference.SetAttributeNode(typeAttribute);
                }
                references.AppendChild(reference);

                foreach (KeyValuePair<string, dynamic> glossaryItem in value)
                {
                    if (glossaryItem.Key != "type")
                    {
                        if (!"".TryParseHex(glossaryItem.Key, out ulong keyVal))
                        {
                            throw new Exception("key is not number of hex number.");
                        }
                        XmlElement entry = xmlDocument.CreateElement("entry");
                        reference.AppendChild(entry);

                        XmlAttribute entryKey = xmlDocument.CreateAttribute("key");
                        entryKey.Value = glossaryItem.Key;

                        entry.SetAttributeNode(entryKey);

                        if (glossaryItem.Value is not null)
                        {
                            XmlAttribute entryValue = xmlDocument.CreateAttribute("value");
                            entryValue.Value = glossaryItem.Value.ToString();
                            entry.SetAttributeNode(entryValue);
                        }
                    }
                }
            }

            /* fix up reference types. */
            XmlNodeList? referenceNodeList = xmlDocument.SelectNodes(@"//property[@type='reference']");
            List<XmlNode> referenceNodes = (referenceNodeList != null) ? referenceNodeList.Cast<XmlNode>().ToList() : [];

            foreach(XmlNode node in referenceNodes)
            {
                if(node == null || node.Attributes == null)
                {
                    throw new Exception($"reference not defined for property {node}");
                }
                else if (node.Attributes["reference"] == null)
                {
                    throw new Exception($"reference not defined for property {node}");
                }
                string reference = node!.Attributes["reference"]!.Value;
                XmlNodeList? originalGlossaryNodes = xmlDocument.SelectNodes(@"//references/" + reference);
                if (originalGlossaryNodes != null)
                {
                    foreach (XmlNode originalGlossaryNode in originalGlossaryNodes)
                    {
                        XmlAttribute? type = (originalGlossaryNode != null && originalGlossaryNode.Attributes != null && originalGlossaryNode.Attributes["type"] != null) ? originalGlossaryNode.Attributes["type"] : null;
                        if (type != null)
                        {
                            node!.Attributes["type"]!.Value = type.Value;
                        }
                        else
                        {
                            node!.Attributes["type"]!.Value = "string";
                        }
                    }
                }
            }

            /* fix up classes */
            /*List<XmlElement> macrosList = macros.ChildNodes.Cast<XmlElement>().ToList();
            foreach(XmlElement node in macrosList)
            {
                if(!node.Name.EndsWith("Macro"))
                {
                    classes.AppendChild(node);
                    XmlNodeList? toReplaceList = xmlDocument.SelectNodes(@"//macro[@type='" + node.Name + "']");

                    if(toReplaceList != null)
                    {
                        List<XmlElement> toReplace = toReplaceList.Cast<XmlElement>().ToList();
                        foreach(XmlElement replace in toReplace)
                        {
                            RenameNode(replace, "class");
                        }
                    }
                }
            }*/

            return xmlDocument;
        }

        public static IGameHookMapper LoadMapperFromFile(IGameHookInstance instance, string mapperContents)
        {
            var deserializer = new DeserializerBuilder().Build();
            var data = deserializer.Deserialize<YamlRoot>(mapperContents);
            XmlDocument document = ConvertYamlToXML(data);
            mapperContents = document.OuterXml;


            return GameHookMapperXmlFactory.LoadMapperFromFile(instance, mapperContents);
        }

        public static bool IsProperty(dynamic source, bool insideMacro, bool insideClass)
        {
            if (source is IDictionary<object, object> dictionary)
            {
                return IsProperty(dictionary, insideMacro, insideClass);
            }
            return false;
        }

        public static bool IsProperty(IDictionary<object, object> source, bool insideMacro, bool insideClass)
        {
            return insideMacro == false && source.ContainsKey("type") && (source.ContainsKey("address") || source.ContainsKey("preprocessor") || source.ContainsKey("staticValue"))
                || (insideMacro == true && source.ContainsKey("type") && source.ContainsKey("offset"))
                || (insideClass == true && source.ContainsKey("type") && (source.ContainsKey("offset") || source.ContainsKey("address") || source.ContainsKey("preprocessor") || source.ContainsKey("staticValue") || source.ContainsKey("classIdx")));
        }

        public static bool IsMerge(string childKey)
        {
            // Key is defined as a special command.
            if (childKey.StartsWith('_'))
            {
                var childKeyCharArray = childKey.ToCharArray();

                // Keys that contain only _ or _0, _1, etc. are considered merge operators.
                if (childKeyCharArray.Length == 1 || childKeyCharArray.Skip(1).All(char.IsDigit))
                {
                    // Setting child key to empty will force it to merge
                    // the transversed properties with it's parent.

                    return true;
                }
                else
                {
                    throw new Exception("Unknown mapper command.");
                }
            }
            return false;
        }

        public static KeyValuePair<object, object>? GetPropertyValueKVByKey(IDictionary<object, object> objValEnum, string key)
        {
            return objValEnum.Select< KeyValuePair<object, object>, KeyValuePair<object, object>?>(property => property)
                .Where(property => 
                    property != null && property.Value.Key != null &&
                    property!.Value.Key!.ToString() != null &&
                    property!.Value.Key!.ToString()!.Equals(key, StringComparison.CurrentCultureIgnoreCase))
                .DefaultIfEmpty(null)
                .FirstOrDefault();
        }

        [GeneratedRegex(@"^[{]address[}] \+ (0x)?0+$")]
        private static partial Regex AddressRegex();
    }
}