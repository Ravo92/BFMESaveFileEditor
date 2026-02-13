namespace BFMESaveFileEditor
{
    class SaveGameParser
    {
        public static SaveGameFile Parse(byte[] raw)
        {
            ArgumentNullException.ThrowIfNull(raw);

            if (raw.Length < 8)
            {
                throw new InvalidOperationException("File too small.");
            }

            if (!BinaryUtil.StartsWithAscii(raw, 0, "ALAE2STR"))
            {
                // The file header did not match the expected signature.
                // Parsing continues anyway because some variants may differ.
            }

            List<int> chunkOffsets = [];
            int cursor = 0;

            while (true)
            {
                int idx = BinaryUtil.FindAscii(raw, "CHUNK_", cursor);
                if (idx < 0)
                {
                    break;
                }

                chunkOffsets.Add(idx);
                cursor = idx + 6;
            }

            List<Chunk> chunks = [];

            for (int i = 0; i < chunkOffsets.Count; i++)
            {
                int start = chunkOffsets[i];
                int end = (i + 1 < chunkOffsets.Count) ? chunkOffsets[i + 1] : raw.Length;
                int length = end - start;

                string chunkName = BinaryUtil.ReadAsciiZ(raw, start, 128);
                chunkName = NormalizeChunkName(chunkName);

                if (string.IsNullOrWhiteSpace(chunkName))
                {
                    chunkName = "CHUNK_?@0x" + start.ToString("X");
                }

                List<Entry> extractedEntries;
                if (IsBinaryPayloadChunk(chunkName))
                {
                    // These chunks contain structured binary payload between the chunk name and SG_EOF.
                    extractedEntries = ExtractBinaryPayloadEntries(raw, start, end);
                }
                else
                {
                    // Default behavior: heuristically extract strings across the chunk region.
                    extractedEntries = ExtractEntries(raw, start, end);
                }

                Chunk chunk = new()
                {
                    Name = chunkName,
                    Offset = start,
                    Length = length,
                    Entries = extractedEntries
                };

                chunks.Add(chunk);
            }

            for (int i = chunks.Count - 1; i >= 0; i--)
            {
                if (string.Equals(chunks[i].Name, "GLOBAL_SCIENCES", StringComparison.OrdinalIgnoreCase))
                {
                    chunks.RemoveAt(i);
                }
            }

            Chunk sciencesChunk = BuildGlobalSciencesChunk(chunks);
            if (sciencesChunk.Entries != null && sciencesChunk.Entries.Count > 0)
            {
                chunks.Insert(0, sciencesChunk);
            }

            return new SaveGameFile(raw, chunks);
        }

        private static string SanitizeExtractedToken(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }

            string trimmed = s.Trim();

            if (IsChunkToken(trimmed))
            {
                // Keep only [A-Za-z0-9_] after CHUNK_ style tokens.
                // This removes trailing junk such as "'", "|", etc.
                System.Text.StringBuilder builder = new(trimmed.Length);
                for (int i = 0; i < trimmed.Length; i++)
                {
                    char c = trimmed[i];
                    if (char.IsLetterOrDigit(c) || c == '_')
                    {
                        builder.Append(c);
                    }
                    else
                    {
                        break;
                    }
                }

                string cleaned = builder.ToString();
                cleaned = NormalizeKolbSuffix(cleaned);
                return cleaned;
            }

            // Map/path tokens: remove leading commas that come from delimiters in the binary stream.
            if (trimmed.StartsWith(",maps\\", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(",maps/", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.TrimStart(',', ' ');
            }

            return trimmed;
        }

        private static bool IsChunkToken(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return false;
            }

            return s.StartsWith("CHUNK_", StringComparison.OrdinalIgnoreCase);
        }

        private static List<Entry> ExtractEntries(byte[] raw, int start, int end)
        {
            List<Entry> entries = [];

            string? currentOwner = null;
            int currentOwnerIndex = -1;

            int i = start;

            while (i < end)
            {
                if (((i & 1) == 0) && i + 3 < end && raw[i] >= 32 && raw[i] <= 126 && raw[i + 1] == 0x00)
                {
                    int j = i;
                    int charCount = 0;

                    while (j + 1 < end)
                    {
                        byte b0 = raw[j];
                        byte b1 = raw[j + 1];

                        if (b1 != 0x00) break;
                        if (b0 < 32 || b0 > 126) break;

                        charCount++;
                        j += 2;

                        if (charCount > 512)
                        {
                            break;
                        }
                    }

                    bool hasTerminator = (j + 1 < end && raw[j] == 0x00 && raw[j + 1] == 0x00);

                    if (charCount >= 4 && hasTerminator)
                    {
                        System.Text.StringBuilder stringBuilder = new(charCount);
                        int k = i;

                        for (int c = 0; c < charCount; c++)
                        {
                            stringBuilder.Append((char)raw[k]);
                            k += 2;
                        }

                        string s = stringBuilder.ToString();
                        s = SanitizeExtractedToken(s);

                        if (!IsValidAsciiRun(s))
                        {
                            i++;
                            continue;
                        }

                        int size = (charCount * 2) + 2;

                        if (string.Equals(s, "SG_EOF", StringComparison.OrdinalIgnoreCase))
                        {
                            i += size;
                            continue;
                        }

                        if (IsHeroOwner(s))
                        {
                            currentOwner = s;
                            currentOwnerIndex++;

                            Entry ownerEntry = new()
                            {
                                Type = EntryType.Unknown,
                                Label = "Hero",
                                Offset = i,
                                Size = size,
                                DisplayValue = s,
                                Owner = null,
                                OwnerIndex = -1
                            };

                            entries.Add(ownerEntry);

                            i += size;
                            continue;
                        }

                        bool isProperty = IsPropertyString(s);

                        Entry e = new()
                        {
                            Type = EntryType.Unknown,
                            Label = isProperty ? "Upgrade" : GuessLabel(s),
                            Offset = i,
                            Size = size,
                            DisplayValue = s,
                            Owner = isProperty ? currentOwner : null,
                            OwnerIndex = isProperty ? currentOwnerIndex : -1
                        };

                        entries.Add(e);

                        i += size;
                        continue;
                    }
                }

                if (raw[i] >= 32 && raw[i] <= 126)
                {
                    int j = i;

                    while (j < end && raw[j] >= 32 && raw[j] <= 126)
                    {
                        j++;
                    }

                    int len = j - i;

                    if (len >= 4)
                    {
                        string s = System.Text.Encoding.ASCII.GetString(raw, i, len);
                        s = SanitizeExtractedToken(s);

                        if (IsValidAsciiRun(s))
                        {
                            bool isZ = (j < end && raw[j] == 0x00);
                            int size = isZ ? (len + 1) : len;

                            if (string.Equals(s, "SG_EOF", StringComparison.OrdinalIgnoreCase))
                            {
                                i += size;
                                continue;
                            }

                            if (IsHeroOwner(s))
                            {
                                currentOwner = s;
                                currentOwnerIndex++;

                                Entry ownerEntry = new()
                                {
                                    Type = isZ ? EntryType.StringAsciiZ : EntryType.Unknown,
                                    Label = "Hero",
                                    Offset = i,
                                    Size = size,
                                    DisplayValue = s,
                                    Owner = null,
                                    OwnerIndex = -1
                                };

                                entries.Add(ownerEntry);

                                i += size;
                                continue;
                            }

                            bool isProperty = IsPropertyString(s);

                            Entry e = new()
                            {
                                Type = isZ ? EntryType.StringAsciiZ : EntryType.Unknown,
                                Label = isProperty ? "Upgrade" : GuessLabel(s),
                                Offset = i,
                                Size = size,
                                DisplayValue = s,
                                Owner = isProperty ? currentOwner : null,
                                OwnerIndex = isProperty ? currentOwnerIndex : -1
                            };

                            entries.Add(e);

                            i += size;
                            continue;
                        }
                    }

                    i++;
                    continue;
                }

                i++;
            }

            return entries;
        }

        private static Chunk BuildGlobalSciencesChunk(List<Chunk> chunks)
        {
            List<Entry> sciences = [];
            HashSet<int> seenOffsets = [];

            for (int c = 0; c < chunks.Count; c++)
            {
                Chunk chunk = chunks[c];

                if (string.Equals(chunk.Name, "GLOBAL_SCIENCES", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (int i = 0; i < chunk.Entries.Count; i++)
                {
                    Entry e = chunk.Entries[i];

                    if (e.DisplayValue != null &&
                        e.DisplayValue.StartsWith("SCIENCE_", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!seenOffsets.Add(e.Offset))
                        {
                            continue;
                        }

                        Entry copy = new()
                        {
                            Type = e.Type,
                            Label = "Science",
                            Offset = e.Offset,
                            Size = e.Size,
                            DisplayValue = e.DisplayValue,
                            Owner = null,
                            OwnerIndex = -1
                        };

                        sciences.Add(copy);
                    }
                }
            }

            Chunk result = new()
            {
                Name = "GLOBAL_SCIENCES",
                Offset = -1,
                Length = sciences.Count,
                Entries = sciences
            };

            return result;
        }

        private static string NormalizeChunkName(string dataset)
        {
            if (string.IsNullOrEmpty(dataset))
            {
                return string.Empty;
            }

            dataset = dataset.Trim();

            System.Text.StringBuilder stringBuilder = new(dataset.Length);

            for (int i = 0; i < dataset.Length; i++)
            {
                char character = dataset[i];
                if (char.IsLetterOrDigit(character) || character == '_')
                {
                    stringBuilder.Append(character);
                }
                else
                {
                    break;
                }
            }

            string name = stringBuilder.ToString();

            // If the chunk id has a 1-character suffix after KOLB (e.g. KOLBE / KOLBO),
            // remove it to keep a stable chunk key for mapping and display.
            name = NormalizeKolbSuffix(name);

            return name;
        }

        private static string NormalizeKolbSuffix(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return s;
            }

            int idx = s.LastIndexOf("KOLB", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return s;
            }

            // Only trim if KOLB is at the end minus exactly one extra character.
            // Example: "...KOLBE" -> "...KOLB"
            if (idx + 4 == s.Length - 1)
            {
                char suffix = s[^1];
                if (char.IsLetter(suffix))
                {
                    return s[..^1];
                }
            }

            return s;
        }


        private static bool IsValidAsciiRun(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s.Length < 4) return false;
            if (s.Length > 512) return false;

            // Chunk-like tokens must be strict: only [A-Za-z0-9_]
            if (IsChunkToken(s))
            {
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (!(char.IsLetterOrDigit(c) || c == '_'))
                    {
                        return false;
                    }
                }

                return true;
            }

            int weird = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '\\' || c == '/' || c == '.' || c == '-' || c == ' '))
                {
                    weird++;
                }
            }

            return weird <= (s.Length / 4);
        }

        private static bool IsHeroOwner(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;

            if (s.StartsWith("Upgrade_", StringComparison.OrdinalIgnoreCase)) return false;
            if (s.StartsWith("SCIENCE_", StringComparison.OrdinalIgnoreCase)) return false;

            if (s.Contains('\\', StringComparison.OrdinalIgnoreCase)) return false;
            if (s.Contains('/', StringComparison.OrdinalIgnoreCase)) return false;
            if (s.Contains(".map", StringComparison.OrdinalIgnoreCase)) return false;

            if (s.StartsWith("Fellowship", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.StartsWith("Campaign", StringComparison.OrdinalIgnoreCase)) return true;

            if (s.Contains('_')) return false;
            if (s.Length < 6 || s.Length > 64) return false;

            if (!char.IsLetter(s[0])) return false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!(char.IsLetterOrDigit(c)))
                {
                    return false;
                }
            }

            int upperCount = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsUpper(s[i])) upperCount++;
            }

            return upperCount >= 2;
        }

        private static bool IsPropertyString(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            return s.StartsWith("Upgrade_", StringComparison.OrdinalIgnoreCase);
        }

        private static string GuessLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "String";

            if (value.StartsWith("Fellowship", StringComparison.OrdinalIgnoreCase)) return "Hero";
            if (value.StartsWith("Campaign", StringComparison.OrdinalIgnoreCase)) return "Hero";
            if (value.StartsWith("Upgrade_", StringComparison.OrdinalIgnoreCase)) return "Upgrade";
            if (value.StartsWith("SCIENCE_", StringComparison.OrdinalIgnoreCase)) return "Science";
            if (value.Contains(".map", StringComparison.OrdinalIgnoreCase)) return "Map";
            if (value.Contains("maps\\", StringComparison.OrdinalIgnoreCase)) return "Path";

            return "String";
        }

        private static bool IsBinaryPayloadChunk(string chunkName)
        {
            if (string.IsNullOrWhiteSpace(chunkName))
            {
                return false;
            }

            return string.Equals(chunkName, "CHUNK_LivingWorldLogicKOLB", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(chunkName, "CHUNK_GameStateMapKOLB", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(chunkName, "CHUNK_GameStateKOLB", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(chunkName, "CHUNK_GameLogicKOLB", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(chunkName, "CHUNK_AudioKOLB", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetChunkPayloadRange(byte[] raw, int chunkStart, int chunkEnd, out int payloadStart, out int payloadEnd)
        {
            payloadStart = 0;
            payloadEnd = 0;

            // Find the real end of the chunk name in the raw data (null-terminated ASCII).
            if (!TryGetNullTerminatedAsciiRange(raw, chunkStart, chunkEnd, 256, out int nameStart, out int nameEndExclusive))
            {
                return false;
            }

            int afterName = nameEndExclusive + 1; // skip '\0'
            if (afterName >= chunkEnd)
            {
                return false;
            }

            int eof = BinaryUtil.FindAscii(raw, "SG_EOF", afterName);
            if (eof < 0 || eof > chunkEnd)
            {
                return false;
            }

            payloadStart = afterName;
            payloadEnd = eof;
            return payloadStart < payloadEnd;
        }

        private static List<Entry> ExtractBinaryPayloadEntries(byte[] raw, int chunkStart, int chunkEnd)
        {
            List<Entry> entries = [];

            if (!TryGetChunkPayloadRange(raw, chunkStart, chunkEnd, out int payloadStart, out int payloadEnd))
            {
                return entries;
            }

            int indexU32 = 0;
            int indexU16 = 0;
            int indexByte = 0;
            int indexF32 = 0;
            int indexStr = 0;

            int i = payloadStart;

            while (i < payloadEnd)
            {
                // 1) ASCII-Z strings (common inside payloads)
                if (raw[i] >= 32 && raw[i] <= 126)
                {
                    int j = i;
                    while (j < payloadEnd && raw[j] >= 32 && raw[j] <= 126)
                    {
                        j++;
                    }

                    int len = j - i;
                    bool isZ = (j < payloadEnd && raw[j] == 0x00);

                    if (len >= 4 && isZ)
                    {
                        string s = System.Text.Encoding.ASCII.GetString(raw, i, len);

                        if (IsValidAsciiRun(s))
                        {
                            Entry e = new()
                            {
                                Type = EntryType.StringAsciiZ,
                                Label = "String_" + indexStr.ToString(),
                                Offset = i,
                                Size = len + 1,
                                DisplayValue = s
                            };

                            entries.Add(e);

                            indexStr++;
                            i += (len + 1);
                            continue;
                        }
                    }
                }

                // 2) Float32 heuristic: if value is finite and not an extreme magnitude
                if (i + 4 <= payloadEnd)
                {
                    float f = BitConverter.ToSingle(raw, i);
                    if (!float.IsNaN(f) && !float.IsInfinity(f))
                    {
                        float af = Math.Abs(f);
                        if (af > 0.000001f && af < 1000000.0f)
                        {
                            Entry e = new()
                            {
                                Type = EntryType.Float32,
                                Label = "F32_" + indexF32.ToString(),
                                Offset = i,
                                Size = 4,
                                DisplayValue = f.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            };

                            entries.Add(e);

                            indexF32++;
                            i += 4;
                            continue;
                        }
                    }
                }

                // 3) UInt16 heuristic: if the upper 16 bits of the next dword are zero, a UInt16 is often more meaningful
                if (i + 4 <= payloadEnd && raw[i + 2] == 0x00 && raw[i + 3] == 0x00)
                {
                    ushort u16 = (ushort)(raw[i] | (raw[i + 1] << 8));

                    Entry e = new()
                    {
                        Type = EntryType.UInt16,
                        Label = "U16_" + indexU16.ToString(),
                        Offset = i,
                        Size = 2,
                        DisplayValue = "0x" + u16.ToString("X4") + " (" + u16.ToString() + ")"
                    };

                    entries.Add(e);

                    indexU16++;
                    i += 2;
                    continue;
                }

                // 4) UInt32 default
                if (i + 4 <= payloadEnd)
                {
                    uint u32 = (uint)(raw[i] | (raw[i + 1] << 8) | (raw[i + 2] << 16) | (raw[i + 3] << 24));

                    string text;
                    if (u32 == 0)
                    {
                        text = "false (0x00000000)";
                    }
                    else if (u32 == 1)
                    {
                        text = "true (0x00000001)";
                    }
                    else
                    {
                        text = "0x" + u32.ToString("X8") + " (" + u32.ToString() + ")";
                    }

                    Entry e = new()
                    {
                        Type = EntryType.UInt32,
                        Label = "U32_" + indexU32.ToString(),
                        Offset = i,
                        Size = 4,
                        DisplayValue = text
                    };

                    entries.Add(e);

                    indexU32++;
                    i += 4;
                    continue;
                }

                // 5) Trailing bytes
                byte b = raw[i];

                Entry eb = new()
                {
                    Type = EntryType.Byte,
                    Label = "B_" + indexByte.ToString(),
                    Offset = i,
                    Size = 1,
                    DisplayValue = "0x" + b.ToString("X2") + " (" + b.ToString() + ")"
                };

                entries.Add(eb);

                indexByte++;
                i++;
            }

            return entries;
        }

        private static bool TryGetNullTerminatedAsciiRange(byte[] raw, int start, int end, int maxScanBytes, out int stringStart, out int stringEndExclusive)
        {
            stringStart = 0;
            stringEndExclusive = 0;

            if (start < 0 || start >= end || start >= raw.Length)
            {
                return false;
            }

            int limit = Math.Min(end, Math.Min(raw.Length, start + maxScanBytes));

            int i = start;
            while (i < limit)
            {
                if (raw[i] == 0x00)
                {
                    stringStart = start;
                    stringEndExclusive = i;
                    return i > start;
                }

                // Only allow reasonably printable ASCII in chunk name area
                if (raw[i] < 32 || raw[i] > 126)
                {
                    return false;
                }

                i++;
            }

            return false;
        }

    }
}