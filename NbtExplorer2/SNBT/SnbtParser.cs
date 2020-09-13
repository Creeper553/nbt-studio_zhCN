﻿using fNbt;
using Microsoft.SqlServer.Server;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NbtExplorer2.SNBT
{
    public class SnbtParser
    {
        private static readonly Regex DOUBLE_PATTERN_NOSUFFIX = new Regex("^([-+]?(?:[0-9]+[.]|[0-9]*[.][0-9]+)(?:e[-+]?[0-9]+)?)$", RegexOptions.IgnoreCase);
        private static readonly Regex DOUBLE_PATTERN = new Regex("^([-+]?(?:[0-9]+[.]?|[0-9]*[.][0-9]+)(?:e[-+]?[0-9]+)?d)$", RegexOptions.IgnoreCase);
        private static readonly Regex FLOAT_PATTERN = new Regex("^([-+]?(?:[0-9]+[.]?|[0-9]*[.][0-9]+)(?:e[-+]?[0-9]+)?f)$", RegexOptions.IgnoreCase);
        private static readonly Regex BYTE_PATTERN = new Regex("^([-+]?(?:0|[1-9][0-9]*)b)$", RegexOptions.IgnoreCase);
        private static readonly Regex LONG_PATTERN = new Regex("^([-+]?(?:0|[1-9][0-9]*)l)$", RegexOptions.IgnoreCase);
        private static readonly Regex SHORT_PATTERN = new Regex("^([-+]?(?:0|[1-9][0-9]*)s)$", RegexOptions.IgnoreCase);
        private static readonly Regex INT_PATTERN = new Regex("^([-+]?(?:0|[1-9][0-9]*))$");

        private readonly StringReader Reader;

        public static NbtTag Parse(string snbt)
        {
            var parser = new SnbtParser(snbt);
            return parser.Parse();
        }

        public static bool TryParse(string snbt, out NbtTag tag)
        {
            try
            {
                tag = Parse(snbt);
                return true;
            }
            catch (Exception)
            {
                tag = null;
                return false;
            }
        }

        private SnbtParser(string snbt)
        {
            snbt = snbt.TrimStart();
            Reader = new StringReader(snbt);
        }

        private NbtTag Parse()
        {
            return ReadValue();
        }

        private NbtTag ReadValue()
        {
            Reader.SkipWhitespace();
            char next = (char)Reader.Peek();
            if (next == '{')
                return ReadCompound();
            if (next == '[')
                return ReadListLike();
            return ReadTypedValue();
        }

        private NbtCompound ReadCompound()
        {
            Expect('{');
            Reader.SkipWhitespace();
            var compound = new NbtCompound();
            while (Reader.CanRead() && Reader.Peek() != '}')
            {
                string key = ReadKey();
                Expect(':');
                NbtTag value = ReadValue();
                value.Name = key;
                compound.Add(value);
                if (!ReadSeparator())
                    break;
            }
            Expect('}');
            return compound;
        }

        private bool ReadSeparator()
        {
            Reader.SkipWhitespace();
            if (Reader.CanRead() && Reader.Peek() == ',')
            {
                Reader.Read();
                Reader.SkipWhitespace();
                return true;
            }
            return false;
        }

        private string ReadKey()
        {
            Reader.SkipWhitespace();
            if (!Reader.CanRead())
                throw new FormatException($"Expected a key, but reached end of data");
            return Reader.ReadString();
        }

        private NbtTag ReadListLike()
        {
            if (Reader.CanRead(3) && !StringReader.IsQuote(Reader.Peek(1)) && Reader.Peek(2) == ';')
                return ReadArray();
            return ReadList();
        }

        private NbtTag ReadArray()
        {
            Expect('[');
            char type = Reader.Read();
            Reader.Read(); // skip semicolon
            Reader.SkipWhitespace();
            if (!Reader.CanRead())
                throw new FormatException($"Expected array to end, but reached end of data");
            if (type == 'B')
                return ReadArray(NbtTagType.Byte);
            if (type == 'L')
                return ReadArray(NbtTagType.Long);
            if (type == 'I')
                return ReadArray(NbtTagType.Int);
            throw new FormatException($"{type} is not a valid array type (B, L, or I)");
        }

        private NbtTag ReadArray(NbtTagType arraytype)
        {
            var list = new ArrayList();
            while (Reader.Peek() != ']')
            {
                var tag = ReadValue();
                if (arraytype == NbtTagType.Byte)
                    list.Add(tag.ByteValue);
                else if (arraytype == NbtTagType.Long)
                    list.Add(tag.LongValue);
                else
                    list.Add(tag.IntValue);
                if (!ReadSeparator())
                    break;
            }
            Expect(']');
            if (arraytype == NbtTagType.Byte)
                return new NbtByteArray(list.Cast<byte>().ToArray());
            else if (arraytype == NbtTagType.Long)
                return new NbtLongArray(list.Cast<long>().ToArray());
            else
                return new NbtIntArray(list.Cast<int>().ToArray());
        }

        private NbtList ReadList()
        {
            Expect('[');
            Reader.SkipWhitespace();
            if (!Reader.CanRead())
                throw new FormatException($"Expected list to end, but reached end of data");
            var list = new NbtList();
            while (Reader.Peek() != ']')
            {
                var tag = ReadValue();
                list.Add(tag);
                if (!ReadSeparator())
                    break;
            }
            Expect(']');
            return list;
        }

        private NbtTag ReadTypedValue()
        {
            Reader.SkipWhitespace();
            if (StringReader.IsQuote(Reader.Peek()))
                return new NbtString(Reader.ReadQuotedString());
            string str = Reader.ReadUnquotedString();
            if (str == "")
                throw new FormatException($"Expected typed value to be non-empty");
            return TypeTag(str);
        }

        private NbtTag TypeTag(string str)
        {
            try
            {
                string sub = str.Substring(0, str.Length - 1);
                if (FLOAT_PATTERN.IsMatch(str))
                    return new NbtFloat(float.Parse(sub, System.Globalization.NumberStyles.Float));
                if (BYTE_PATTERN.IsMatch(str))
                    return new NbtByte((byte)sbyte.Parse(sub));
                if (LONG_PATTERN.IsMatch(str))
                    return new NbtLong(long.Parse(sub));
                if (SHORT_PATTERN.IsMatch(str))
                    return new NbtShort(short.Parse(sub));
                if (INT_PATTERN.IsMatch(str))
                    return new NbtInt(int.Parse(str));
                if (DOUBLE_PATTERN.IsMatch(str))
                    return new NbtDouble(double.Parse(sub, System.Globalization.NumberStyles.Float));
                if (DOUBLE_PATTERN_NOSUFFIX.IsMatch(str))
                    return new NbtDouble(double.Parse(str, System.Globalization.NumberStyles.Float));
                if (String.Equals(str, "true", StringComparison.OrdinalIgnoreCase))
                    return new NbtByte(1);
                if (String.Equals(str, "false", StringComparison.OrdinalIgnoreCase))
                    return new NbtByte(0);
                if (String.Equals(str, "Infinityf", StringComparison.OrdinalIgnoreCase))
                    return new NbtFloat(float.PositiveInfinity);
                if (String.Equals(str, "-Infinityf", StringComparison.OrdinalIgnoreCase))
                    return new NbtFloat(float.NegativeInfinity);
                if (String.Equals(str, "NaNf", StringComparison.OrdinalIgnoreCase))
                    return new NbtFloat(float.NaN);
                if (String.Equals(str, "Infinityd", StringComparison.OrdinalIgnoreCase) || String.Equals(str, "Infinity", StringComparison.OrdinalIgnoreCase))
                    return new NbtDouble(double.PositiveInfinity);
                if (String.Equals(str, "-Infinityd", StringComparison.OrdinalIgnoreCase) || String.Equals(str, "-Infinity", StringComparison.OrdinalIgnoreCase))
                    return new NbtDouble(double.NegativeInfinity);
                if (String.Equals(str, "NaNd", StringComparison.OrdinalIgnoreCase) || String.Equals(str, "NaN", StringComparison.OrdinalIgnoreCase))
                    return new NbtDouble(double.NaN);
            }
            catch (FormatException)
            { }
            catch (OverflowException)
            { }
            return new NbtString(str);
        }

        private void Expect(char c)
        {
            Reader.SkipWhitespace();
            Reader.Expect(c);
        }
    }

    public class StringReader
    {
        private const char ESCAPE = '\\';
        private const char DOUBLE_QUOTE = '"';
        private const char SINGLE_QUOTE = '\'';
        private readonly string String;
        private int Cursor;

        public StringReader(string str)
        {
            String = str;
        }

        public static bool IsQuote(char c)
        {
            return c == DOUBLE_QUOTE || c == SINGLE_QUOTE;
        }

        public static bool UnquotedAllowed(char c)
        {
            return c >= '0' && c <= '9'
                || c >= 'A' && c <= 'Z'
                || c >= 'a' && c <= 'z'
                || c == '_' || c == '-'
                || c == '.' || c == '+';
        }

        public bool CanRead(int length = 1)
        {
            return Cursor + length <= String.Length;
        }

        public char Peek(int offset = 0)
        {
            return String[Cursor + offset];
        }

        public char Read()
        {
            char result = Peek();
            Cursor++;
            return result;
        }

        public string ReadString()
        {
            if (!CanRead())
                return String.Empty;
            char next = Peek();
            if (IsQuote(next))
            {
                Read();
                return ReadStringUntil(next);
            }
            return ReadUnquotedString();
        }

        public string ReadStringUntil(char end)
        {
            var result = new StringBuilder();
            bool escaped = false;
            while (CanRead())
            {
                char c = Read();
                if (escaped)
                {
                    if (c == end || c == ESCAPE)
                    {
                        result.Append(c);
                        escaped = false;
                    }
                    else
                    {
                        Cursor--;
                        throw new FormatException($"Tried to escape {c} at position {Cursor}, which is not {ESCAPE} or {end}");
                    }
                }
                else if (c == ESCAPE)
                    escaped = true;
                else if (c == end)
                    return result.ToString();
                else
                    result.Append(c);
            }
            throw new FormatException($"Expected the string to end with {end}, but reached end of data");
        }

        public string ReadUnquotedString()
        {
            int start = Cursor;
            while (CanRead() && UnquotedAllowed(Peek()))
            {
                Read();
            }
            return String.Substring(start, Cursor - start);
        }

        public string ReadQuotedString()
        {
            if (!CanRead())
                return String.Empty;
            char next = Peek();
            if (!IsQuote(next))
                throw new FormatException($"Expected the string to at position {Cursor} to be quoted, but got {next}");
            Read();
            return ReadStringUntil(next);
        }

        public void SkipWhitespace()
        {
            while (CanRead() && Char.IsWhiteSpace(Peek()))
            {
                Read();
            }
        }

        public void Expect(char c)
        {
            if (!CanRead())
                throw new FormatException($"Expected {c} at position {Cursor}, but reached end of data");
            char read = Read();
            if (read != c)
                throw new FormatException($"Expected {c} at position {Cursor}, but got {read}");
        }
    }
}