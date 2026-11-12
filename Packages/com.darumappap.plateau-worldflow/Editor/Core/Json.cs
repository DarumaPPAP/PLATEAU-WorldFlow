using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DarumaPPAP.PlateauWorldFlow
{
    /// <summary>
    /// SetupとRead-only契約で共有するJSONを読み書きし、整数とその他の数値表記を区別する。
    /// </summary>
    internal sealed class Json
    {
        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly Regex Tokens = new Regex(@"\G(?<value>""(?>[^""\\\x00-\x1f]|\\(?:[""\\/bfnrt]|u[0-9a-fA-F]{4}))*""|-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?|true|false|null|[{}\[\],:])", RegexOptions.CultureInvariant);

        /// <summary>
        /// 一つのJSONオブジェクトを復号する。
        /// </summary>
        /// <param name="text">復号するJSONテキスト。</param>
        /// <returns>オブジェクトのキーと値を格納した辞書。</returns>
        internal static Dictionary<string, object> Parse(string text)
        {
            try
            {
                return new Reader(text).ReadObject();
            }
            catch (EncoderFallbackException error)
            {
                throw new FormatException("Invalid Unicode in JSON.", error);
            }
        }

        internal static Dictionary<string, object> Object(params (string key, object value)[] fields)
        {
            return fields.ToDictionary(field => field.key, field => field.value, StringComparer.Ordinal);
        }

        /// <summary>
        /// 有限の浮動小数点数を、カルチャに依存しないJSON数値に変換する。
        /// </summary>
        /// <param name="value">有限の浮動小数点数。</param>
        /// <returns>JSONライターが数値として書き出せる値。</returns>
        internal static object Real(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return new Number(value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 契約データをJSONに書き出し、オブジェクトのキーを序数順にそろえる。
        /// </summary>
        /// <param name="value">書き出すJSON互換の値。</param>
        /// <returns>JSONテキスト。</returns>
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

        /// <summary>
        /// 指定したキーから、要求した型と一致する値を取得する。
        /// </summary>
        /// <typeparam name="T">読み取る値の型。</typeparam>
        /// <param name="fields">読み取り対象のJSONオブジェクト。</param>
        /// <param name="key">取得するキー。</param>
        /// <returns>値が欠落、null、または型不一致の場合は要求した型の既定値。</returns>
        internal static T Field<T>(Dictionary<string, object> fields, string key)
        {
            // null、欠落、型不一致のいずれも呼び出し元には既定値を返す。
            return fields != null && fields.TryGetValue(key, out var value) && value is T result ? result : default;
        }

        /// <summary>
        /// 整数として扱わないJSON数値の表記を保持し、読み書きでその表記を維持する。
        /// </summary>
        private sealed class Number
        {
            internal string Text { get; }
            internal Number(string text) => Text = text;
        }

        /// <summary>
        /// JSONの字句を順に読み、重複キーと深すぎる入れ子を拒否して一つのオブジェクトに復号する。
        /// </summary>
        private sealed class Reader
        {
            private readonly string _text;
            private int _position;
            private string _current;

            internal Reader(string text)
            {
                _text = text;
                Advance();
            }

            internal Dictionary<string, object> ReadObject()
            {
                var fields = ReadValue(0) as Dictionary<string, object>;
                if (fields == null || _current != null)
                {
                    throw new FormatException("Expected one JSON object.");
                }
                return fields;
            }

            private object ReadValue(int depth)
            {
                string value = _current ?? throw new FormatException("Missing JSON value.");
                if (depth > 48)
                {
                    throw new FormatException("JSON nesting is too deep.");
                }
                Advance();
                switch (value)
                {
                    case "{":
                        var fields = new Dictionary<string, object>(StringComparer.Ordinal);
                        if (!Take("}"))
                        {
                            do
                            {
                                string key = ReadString(_current);
                                Advance();
                                Require(":");
                                if (!fields.TryAdd(key, ReadValue(depth + 1)))
                                {
                                    throw new FormatException("Duplicate JSON property.");
                                }
                            } while (Take(","));
                            Require("}");
                        }
                        return fields;
                    case "[":
                        var items = new List<object>();
                        if (!Take("]"))
                        {
                            do
                            {
                                items.Add(ReadValue(depth + 1));
                            } while (Take(","));
                            Require("]");
                        }
                        return items.ToArray();
                    case "null": return null;
                    case "true": return true;
                    case "false": return false;
                }
                // 字句解析でエスケープ形式を検証済みなので、文字列として復号できる。
                if (value[0] == '"') return ReadString(value);
                // Plan契約で整数と小数・指数表記を区別できるようにする。
                if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int integer)) return integer;
                // 整数型に変換できない数値もJSONとして保持し、整数フィールドとの照合では区別する。
                if (value[0] == '-' || char.IsDigit(value[0])) return new Number(value);
                throw new FormatException("Expected a JSON value.");
            }

            private string ReadString(string token)
            {
                if (token == null || token[0] != '"')
                {
                    throw new FormatException("Expected a JSON string.");
                }
                var output = new StringBuilder(token.Length - 2);
                for (int i = 1; i < token.Length - 1; i++)
                {
                    char character = token[i];
                    if (character == '\\')
                    {
                        character = token[++i];
                        switch (character)
                        {
                            case 'b': character = '\b'; break;
                            case 'f': character = '\f'; break;
                            case 'n': character = '\n'; break;
                            case 'r': character = '\r'; break;
                            case 't': character = '\t'; break;
                            case 'u':
                                character = (char)int.Parse(token.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                                i += 4;
                                break;
                        }
                    }
                    output.Append(character);
                }
                string text = output.ToString();
                Utf8.GetByteCount(text);
                return text;
            }

            private void Advance()
            {
                while (_position < _text.Length && " \t\r\n".IndexOf(_text[_position]) >= 0)
                {
                    _position++;
                }
                // 末尾の空白は入力終端として扱う。
                if (_position == _text.Length)
                {
                    _current = null;
                    return;
                }
                var token = Tokens.Match(_text, _position);
                if (!token.Success)
                {
                    throw new FormatException("Invalid JSON token.");
                }
                _current = token.Groups["value"].Value;
                _position += token.Length;
            }

            private bool Take(string expected)
            {
                // 不一致時は呼び出し元が別の区切りとして照合できるようにする。
                if (_current != expected) return false;
                Advance();
                return true;
            }

            private void Require(string expected)
            {
                if (!Take(expected))
                {
                    throw new FormatException("Unexpected JSON delimiter.");
                }
            }
        }
    }
}
