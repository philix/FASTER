﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System.Runtime.CompilerServices;
using FASTER.core;
using FASTER.common;
using System;

namespace FASTER.server
{
    /// <summary>
    /// Serializer for SpanByte. Used only on server-side.
    /// </summary>
    public unsafe sealed class SpanByteKeySerializer : IKeyInputSerializer<SpanByte, SpanByte>
    {
        readonly SpanByteVarLenStruct settings;

        /// <summary>
        /// Constructor
        /// </summary>
        public SpanByteKeySerializer()
        {
            settings = new SpanByteVarLenStruct();
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref SpanByte ReadKeyByRef(ref byte* src)
        {
            ref var ret = ref Unsafe.AsRef<SpanByte>(src);
            src += settings.GetLength(ref ret);
            return ref ret;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref SpanByte ReadInputByRef(ref byte* src)
        {
            ref var ret = ref Unsafe.AsRef<SpanByte>(src);
            src += settings.GetLength(ref ret);
            return ref ret;
        }

        /// <inheritdoc />
        public bool Match(ref SpanByte k, bool asciiKey, ref SpanByte pattern, bool asciiPattern)
        {
            if (asciiKey && asciiPattern)
            {
                return GlobMatching(pattern.ToPointer(), pattern.LengthWithoutMetadata, k.ToPointer(), k.LengthWithoutMetadata);
            }

            if (pattern.LengthWithoutMetadata > k.LengthWithoutMetadata)
                return false;
            return pattern.AsReadOnlySpan().SequenceEqual(k.AsReadOnlySpan().Slice(0, pattern.LengthWithoutMetadata));
        }

        /* Glob-style pattern matching. */
        private static bool GlobMatching(byte* pattern, int patternLen, byte* key, int stringLen, bool nocase = false)
        {
            while (patternLen > 0 && stringLen > 0)
            {
                switch (pattern[0])
                {
                    case (byte)'*':
                        while (patternLen > 0 && pattern[1] == '*')
                        {
                            pattern++;
                            patternLen--;
                        }
                        if (patternLen == 1)
                            return true; /* match */
                        while (stringLen > 0)
                        {
                            if (GlobMatching(pattern + 1, patternLen - 1, key, stringLen, nocase))
                                return true; /* match */
                            key++;
                            stringLen--;
                        }
                        return false; /* no match */

                    case (byte)'?':
                        key++;
                        stringLen--;
                        break;

                    case (byte)'[':
                        {
                            bool not, match;
                            pattern++;
                            patternLen--;
                            not = (pattern[0] == '^');
                            if (not)
                            {
                                pattern++;
                                patternLen--;
                            }
                            match = false;
                            while (true)
                            {
                                if (pattern[0] == '\\' && patternLen >= 2)
                                {
                                    pattern++;
                                    patternLen--;
                                    if (pattern[0] == key[0])
                                        match = true;
                                }
                                else if (pattern[0] == ']')
                                {
                                    break;
                                }
                                else if (patternLen == 0)
                                {
                                    pattern--;
                                    patternLen++;
                                    break;
                                }
                                else if (patternLen >= 3 && pattern[1] == '-')
                                {
                                    int start = pattern[0];
                                    int end = pattern[2];
                                    int c = key[0];
                                    if (start > end)
                                    {
                                        int t = start;
                                        start = end;
                                        end = t;
                                    }
                                    if (nocase)
                                    {
                                        start = char.ToLower((char)start);
                                        end = char.ToLower((char)end);
                                        c = char.ToLower((char)c);
                                    }
                                    pattern += 2;
                                    patternLen -= 2;
                                    if (c >= start && c <= end)
                                        match = true;
                                }
                                else
                                {
                                    if (!nocase)
                                    {
                                        if (pattern[0] == key[0])
                                            match = true;
                                    }
                                    else
                                    {
                                        if (char.ToLower((char)pattern[0]) == char.ToLower((char)key[0]))
                                            match = true;
                                    }
                                }
                                pattern++;
                                patternLen--;
                            }

                            if (not)
                                match = !match;
                            if (!match)
                                return false; /* no match */
                            key++;
                            stringLen--;
                            break;
                        }

                    case (byte)'\\':
                        if (patternLen >= 2)
                        {
                            pattern++;
                            patternLen--;
                        }
                        goto default;

                    /* fall through */
                    default:
                        if (!nocase)
                        {
                            if (pattern[0] != key[0])
                                return false; /* no match */
                        }
                        else
                        {
                            if (char.ToLower((char)pattern[0]) != char.ToLower((char)key[0]))
                                return false; /* no match */
                        }
                        key++;
                        stringLen--;
                        break;
                }
                pattern++;
                patternLen--;
                if (stringLen == 0)
                {
                    while (*pattern == '*')
                    {
                        pattern++;
                        patternLen--;
                    }
                    break;
                }
            }
            if (patternLen == 0 && stringLen == 0)
                return true;
            return false;
        }
    }
}