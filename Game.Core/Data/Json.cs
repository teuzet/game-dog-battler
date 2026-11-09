using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Math;

namespace Game.Core.Data
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>A small JSON tree. Numbers stay as text until asked for, so
    /// they become Fixed exactly and never pass through a float. No package:
    /// the same code runs in .NET and in Unity.</summary>
    public sealed class JsonValue
    {
        public JsonKind Kind { get; private set; }
        private string _text = "";
        private bool _bool;
        private List<JsonValue>? _items;
        private List<KeyValuePair<string, JsonValue>>? _fields;

        public static JsonValue Null() => new JsonValue { Kind = JsonKind.Null };
        public static JsonValue Bool(bool v) => new JsonValue { Kind = JsonKind.Bool, _bool = v };
        public static JsonValue Number(string text) => new JsonValue { Kind = JsonKind.Number, _text = text };
        public static JsonValue Str(string v) => new JsonValue { Kind = JsonKind.String, _text = v };
        public static JsonValue Array() => new JsonValue { Kind = JsonKind.Array, _items = new List<JsonValue>() };
        public static JsonValue Object() => new JsonValue { Kind = JsonKind.Object, _fields = new List<KeyValuePair<string, JsonValue>>() };

        public IReadOnlyList<JsonValue> Items => _items ?? (IReadOnlyList<JsonValue>)System.Array.Empty<JsonValue>();
        public IReadOnlyList<KeyValuePair<string, JsonValue>> Fields =>
            _fields ?? (IReadOnlyList<KeyValuePair<string, JsonValue>>)System.Array.Empty<KeyValuePair<string, JsonValue>>();

        public JsonValue? this[string key]
        {
            get
            {
                if (_fields == null) return null;
                foreach (var kv in _fields) if (kv.Key == key) return kv.Value;
                return null;
            }
        }

        public void Set(string key, JsonValue v)
        {
            if (_fields == null) throw new InvalidOperationException("not an object");
            for (int i = 0; i < _fields.Count; i++)
                if (_fields[i].Key == key) { _fields[i] = new KeyValuePair<string, JsonValue>(key, v); return; }
            _fields.Add(new KeyValuePair<string, JsonValue>(key, v));
        }

        public void Add(JsonValue v)
        {
            if (_items == null) throw new InvalidOperationException("not an array");
            _items.Add(v);
        }

        public Fixed AsFixed() => Kind == JsonKind.Number ? Fixed.Parse(_text) : throw new FormatException($"not a number: {Kind}");
        public int AsInt() => Kind == JsonKind.Number ? int.Parse(_text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : throw new FormatException($"not a number: {Kind}");
        public double AsDouble() => double.Parse(_text, NumberStyles.Float, CultureInfo.InvariantCulture);
        public string AsString() => Kind == JsonKind.String ? _text : Kind == JsonKind.Number ? _text : throw new FormatException($"not a string: {Kind}");
        public bool AsBool() => Kind == JsonKind.Bool ? _bool : throw new FormatException($"not a bool: {Kind}");

        /// <summary>A deep copy with the fields of <paramref name="over"/> laid on top (objects merge, the rest replaces).</summary>
        public JsonValue MergedWith(JsonValue over)
        {
            if (Kind != JsonKind.Object || over.Kind != JsonKind.Object) return over.Copy();
            var res = Copy();
            foreach (var kv in over.Fields)
            {
                var mine = res[kv.Key];
                res.Set(kv.Key, mine == null ? kv.Value.Copy() : mine.MergedWith(kv.Value));
            }
            return res;
        }

        public JsonValue Copy()
        {
            var c = new JsonValue { Kind = Kind, _text = _text, _bool = _bool };
            if (_items != null) { c._items = new List<JsonValue>(); foreach (var i in _items) c._items.Add(i.Copy()); }
            if (_fields != null) { c._fields = new List<KeyValuePair<string, JsonValue>>(); foreach (var kv in _fields) c._fields.Add(new KeyValuePair<string, JsonValue>(kv.Key, kv.Value.Copy())); }
            return c;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            Write(sb);
            return sb.ToString();
        }

        public void Write(StringBuilder sb)
        {
            switch (Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(_bool ? "true" : "false"); break;
                case JsonKind.Number: sb.Append(_text); break;
                case JsonKind.String: Json.WriteString(sb, _text); break;
                case JsonKind.Array:
                    sb.Append('[');
                    for (int i = 0; i < Items.Count; i++) { if (i > 0) sb.Append(','); Items[i].Write(sb); }
                    sb.Append(']');
                    break;
                default:
                    sb.Append('{');
                    for (int i = 0; i < Fields.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        Json.WriteString(sb, Fields[i].Key);
                        sb.Append(':');
                        Fields[i].Value.Write(sb);
                    }
                    sb.Append('}');
                    break;
            }
        }
    }

    public static class Json
    {
        public static JsonValue Parse(string text)
        {
            int i = 0;
            var v = Value(text, ref i);
            Skip(text, ref i);
            if (i != text.Length) throw Error(text, i, "trailing characters");
            return v;
        }

        public static JsonValue Num(double v) => JsonValue.Number(v.ToString("0.######", CultureInfo.InvariantCulture));
        public static JsonValue Num(long v) => JsonValue.Number(v.ToString(CultureInfo.InvariantCulture));
        public static JsonValue Num(Fixed v) => JsonValue.Number(v.ToString());

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private static JsonValue Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw Error(s, i, "unexpected end");
            char c = s[i];
            if (c == '{')
            {
                i++;
                var o = JsonValue.Object();
                Skip(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    Skip(s, ref i);
                    string key = StringLit(s, ref i);
                    Skip(s, ref i);
                    Expect(s, ref i, ':');
                    o.Set(key, Value(s, ref i));
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    Expect(s, ref i, '}');
                    return o;
                }
            }
            if (c == '[')
            {
                i++;
                var a = JsonValue.Array();
                Skip(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i));
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    Expect(s, ref i, ']');
                    return a;
                }
            }
            if (c == '"') return JsonValue.Str(StringLit(s, ref i));
            if (Word(s, ref i, "true")) return JsonValue.Bool(true);
            if (Word(s, ref i, "false")) return JsonValue.Bool(false);
            if (Word(s, ref i, "null")) return JsonValue.Null();
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start) throw Error(s, i, $"unexpected '{c}'");
            return JsonValue.Number(s.Substring(start, i - start));
        }

        private static string StringLit(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            Expect(s, ref i, '"');
            return sb.ToString();
        }

        private static bool Word(string s, ref int i, string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c) throw Error(s, i, $"expected '{c}'");
            i++;
        }

        private static FormatException Error(string s, int i, string what)
        {
            int line = 1;
            for (int k = 0; k < i && k < s.Length; k++) if (s[k] == '\n') line++;
            return new FormatException($"JSON line {line}: {what}");
        }
    }
}
