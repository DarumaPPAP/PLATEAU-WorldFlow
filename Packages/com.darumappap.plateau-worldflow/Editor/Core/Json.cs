using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DarumaPPAP.PlateauWorldFlow
{
    internal sealed class Json
    {
        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static Dictionary<string, object> Parse(string text)
        {
            try
            {
                var bytes = Utf8.GetBytes(text);
                var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 48, MaxStringContentLength = 1048576, MaxArrayLength = 65536, MaxNameTableCharCount = 1048576 };
                using var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, quotas);
                var value = Read(XElement.Load(reader));
                return value as Dictionary<string, object> ?? throw new FormatException("Expected a JSON object.");
            }
            catch (Exception error) when (error is XmlException || error is ArgumentException)
            {
                throw new FormatException("Invalid JSON.", error);
            }
        }

        private static object Read(XElement element)
        {
            switch ((string)element.Attribute("type"))
            {
                case "object":
                    var fields = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (var child in element.Elements())
                    {
                        var name = (string)child.Attribute("item") ?? child.Name.LocalName;
                        if (!fields.TryAdd(name, Read(child)))
                        {
                            throw new FormatException("Duplicate JSON property.");
                        }
                    }
                    return fields;
                case "array":
                    return element.Elements().Select(Read).ToArray();
                case "null":
                    return null;
                case "boolean":
                    return bool.Parse(element.Value);
                case "number":
                    if (int.TryParse(element.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int integer))
                    {
                        return integer;
                    }
                    return new Number(element.Value);
                default:
                    Utf8.GetByteCount(element.Value);
                    return element.Value;
            }
        }

        internal static Dictionary<string, object> Object(params (string key, object value)[] fields)
        {
            return fields.ToDictionary(field => field.key, field => field.value, StringComparer.Ordinal);
        }

        internal static string Write(object value)
        {
            var output = new StringBuilder();
            Append(output, value);
            return output.ToString();
        }

        private static void Append(StringBuilder output, object value)
        {
            switch (value)
            {
                case null:
                    output.Append("null");
                    break;
                case string text:
                    AppendString(output, text);
                    break;
                case int number:
                    output.Append(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case Number number:
                    output.Append(number.Text);
                    break;
                case bool boolean:
                    output.Append(boolean ? "true" : "false");
                    break;
                case Dictionary<string, object> fields:
                    output.Append('{');
                    bool firstField = true;
                    foreach (var field in fields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        if (!firstField)
                        {
                            output.Append(',');
                        }
                        firstField = false;
                        AppendString(output, field.Key);
                        output.Append(':');
                        Append(output, field.Value);
                    }
                    output.Append('}');
                    break;
                case IEnumerable items:
                    output.Append('[');
                    bool firstItem = true;
                    foreach (object item in items)
                    {
                        if (!firstItem)
                        {
                            output.Append(',');
                        }
                        firstItem = false;
                        Append(output, item);
                    }
                    output.Append(']');
                    break;
                default:
                    throw new ArgumentException("Unsupported JSON value.");
            }
        }

        private static void AppendString(StringBuilder output, string text)
        {
            output.Append('"');
            foreach (char character in text)
            {
                if (character == '"' || character == '\\')
                {
                    output.Append('\\').Append(character);
                }
                else if (char.IsControl(character))
                {
                    output.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    output.Append(character);
                }
            }
            output.Append('"');
        }

        internal static T Field<T>(Dictionary<string, object> fields, string key)
        {
            // JSON null and a missing or wrongly typed field have the same lookup result.
            return fields != null && fields.TryGetValue(key, out var value) && value is T result ? result : default;
        }

        private sealed class Number
        {
            internal string Text { get; }
            internal Number(string text) => Text = text;
        }
    }
}
