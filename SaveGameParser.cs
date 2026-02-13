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

            List<int> chunkStarts = CollectValidatedChunkStarts(raw);

            List<Chunk> chunks = [];

            for (int i = 0; i < chunkStarts.Count; i++)
            {
                int start = chunkStarts[i];
                int nextStart = (i + 1 < chunkStarts.Count) ? chunkStarts[i + 1] : raw.Length;

                int nameLen = GetChunkTokenLength(raw, start, raw.Length, 256);
                string rawChunkName = nameLen > 0 ? System.Text.Encoding.ASCII.GetString(raw, start, nameLen) : "";
                string chunkName = NormalizeChunkName(rawChunkName);


                if (string.IsNullOrWhiteSpace(chunkName))
                {
                    chunkName = "CHUNK_?@0x" + start.ToString("X");
                }

                int end = FindChunkEnd(raw, start, nextStart);

                if (end <= start)
                {
                    end = nextStart;
                }

                int length = end - start;

                List<Entry> extractedEntries;
                if (IsBinaryPayloadChunk(chunkName))
                {
                    extractedEntries = ExtractBinaryPayloadEntries(raw, start, end);
                }
                else
                {
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

            int nameLen = GetChunkTokenLength(raw, chunkStart, chunkEnd, 256);
            if (nameLen <= 0)
            {
                return false;
            }

            int afterName = chunkStart + nameLen;
            if (afterName >= chunkEnd)
            {
                return false;
            }

            int asciiEof = FindAsciiInRange(raw, "SG_EOF", afterName, chunkEnd);
            int utf16Eof = FindUtf16LeAsciiInRange(raw, "SG_EOF", afterName, chunkEnd);

            int eof;
            if (asciiEof >= 0 && utf16Eof >= 0)
            {
                eof = Math.Min(asciiEof, utf16Eof);
            }
            else if (asciiEof >= 0)
            {
                eof = asciiEof;
            }
            else
            {
                eof = utf16Eof;
            }

            payloadStart = afterName;
            payloadEnd = (eof >= 0) ? eof : chunkEnd;

            return payloadStart < payloadEnd;
        }

        private static List<Entry> ExtractBinaryPayloadEntries(byte[] raw, int chunkStart, int chunkEnd)
        {
            List<Entry> entries = [];

            if (!TryGetChunkPayloadRange(raw, chunkStart, chunkEnd, out int payloadStart, out int payloadEnd))
            {
                return entries;
            }

            int index = 0;
            int i = payloadStart;

            while (i < payloadEnd)
            {
                if (TryReadLenPrefixedUtf16Le(raw, i, payloadEnd, out string utf16Text, out int utf16Size))
                {
                    Entry e = new()
                    {
                        Type = EntryType.StringUtf16LeLen8,
                        Label = "StrU16_" + index.ToString(),
                        Offset = i,
                        Size = utf16Size,
                        DisplayValue = utf16Text
                    };

                    entries.Add(e);
                    index++;
                    i += utf16Size;
                    continue;
                }

                if (TryReadLenPrefixedAscii(raw, i, payloadEnd, out string asciiText, out int asciiSize))
                {
                    Entry e = new()
                    {
                        Type = EntryType.StringAsciiLen8,
                        Label = "StrA_" + index.ToString(),
                        Offset = i,
                        Size = asciiSize,
                        DisplayValue = asciiText
                    };

                    entries.Add(e);
                    index++;
                    i += asciiSize;
                    continue;
                }

                if (TryReadAsciiZ(raw, i, payloadEnd, out string zText, out int zSize))
                {
                    Entry e = new()
                    {
                        Type = EntryType.StringAsciiZ,
                        Label = "StrZ_" + index.ToString(),
                        Offset = i,
                        Size = zSize,
                        DisplayValue = zText
                    };

                    entries.Add(e);
                    index++;
                    i += zSize;
                    continue;
                }

                if (i + 4 <= payloadEnd)
                {
                    uint u32 = ReadU32LE(raw, i);
                    int i32 = unchecked((int)u32);
                    float f32 = BitConverter.ToSingle(raw, i);

                    string display = FormatDword(u32, i32, f32);

                    Entry e = new()
                    {
                        Type = EntryType.UInt32,
                        Label = "Dword_" + index.ToString(),
                        Offset = i,
                        Size = 4,
                        DisplayValue = display
                    };

                    entries.Add(e);
                    index++;
                    i += 4;
                    continue;
                }

                byte b = raw[i];

                Entry eb = new()
                {
                    Type = EntryType.Byte,
                    Label = "Byte_" + index.ToString(),
                    Offset = i,
                    Size = 1,
                    DisplayValue = "0x" + b.ToString("X2") + " (" + b.ToString() + ")"
                };

                entries.Add(eb);
                index++;
                i += 1;
            }

            return entries;
        }

        private static List<int> CollectValidatedChunkStarts(byte[] raw)
        {
            List<int> starts = [];
            int cursor = 0;

            while (true)
            {
                int idx = BinaryUtil.FindAscii(raw, "CHUNK_", cursor);
                if (idx < 0)
                {
                    break;
                }

                int nameLen = GetChunkTokenLength(raw, idx, raw.Length, 128);
                if (nameLen >= 10)
                {
                    string name = System.Text.Encoding.ASCII.GetString(raw, idx, nameLen);

                    if (name.StartsWith("CHUNK_", StringComparison.OrdinalIgnoreCase) &&
                        name.Contains("KOLB", StringComparison.OrdinalIgnoreCase))
                    {
                        starts.Add(idx);
                        cursor = idx + 6;
                        continue;
                    }
                }

                cursor = idx + 1;
            }

            starts.Sort();

            for (int i = starts.Count - 1; i > 0; i--)
            {
                if (starts[i] == starts[i - 1])
                {
                    starts.RemoveAt(i);
                }
            }

            return starts;
        }

        private static bool IsPlausibleChunkName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (!name.StartsWith("CHUNK_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                {
                    return false;
                }
            }

            return name.Length <= 128;
        }

        private static int FindChunkEnd(byte[] raw, int start, int endLimit)
        {
            // Prefer SG_EOF inside [start..endLimit). Support both ASCII and UTF-16LE variants.
            int ascii = FindAsciiInRange(raw, "SG_EOF", start, endLimit);
            int utf16 = FindUtf16LeAsciiInRange(raw, "SG_EOF", start, endLimit);

            int best;
            if (ascii >= 0 && utf16 >= 0)
            {
                best = Math.Min(ascii, utf16);
            }
            else if (ascii >= 0)
            {
                best = ascii;
            }
            else
            {
                best = utf16;
            }

            if (best < 0)
            {
                return endLimit;
            }

            int end = best + ((best == ascii) ? 5 : 10); // ASCII len=5, UTF-16LE len=10

            // Skip trailing 0x00 bytes after the marker, but do not cross endLimit
            while (end < endLimit && end < raw.Length && raw[end] == 0x00)
            {
                end++;
            }

            return end;
        }

        private static int FindAsciiInRange(byte[] raw, string needle, int start, int end)
        {
            byte[] n = System.Text.Encoding.ASCII.GetBytes(needle);

            int max = Math.Min(end, raw.Length) - n.Length;
            for (int i = Math.Max(0, start); i <= max; i++)
            {
                bool ok = true;
                for (int j = 0; j < n.Length; j++)
                {
                    if (raw[i + j] != n[j])
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindUtf16LeAsciiInRange(byte[] raw, string needle, int start, int end)
        {
            // UTF-16LE encoding of ASCII letters: 'S' 00 'G' 00 ...
            byte[] ascii = System.Text.Encoding.ASCII.GetBytes(needle);
            byte[] n = new byte[ascii.Length * 2];

            for (int i = 0; i < ascii.Length; i++)
            {
                n[(i * 2) + 0] = ascii[i];
                n[(i * 2) + 1] = 0x00;
            }

            int max = Math.Min(end, raw.Length) - n.Length;
            for (int i = Math.Max(0, start); i <= max; i++)
            {
                bool ok = true;
                for (int j = 0; j < n.Length; j++)
                {
                    if (raw[i + j] != n[j])
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int GetChunkTokenLength(byte[] raw, int start, int end, int maxLen)
        {
            if (start < 0 || start >= raw.Length)
            {
                return 0;
            }

            int limit = Math.Min(raw.Length, Math.Min(end, start + maxLen));
            int i = start;

            while (i < limit)
            {
                byte b = raw[i];

                bool isAscii = b >= 32 && b <= 126;
                if (!isAscii)
                {
                    break;
                }

                char c = (char)b;
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                {
                    break;
                }

                i++;
            }

            return i - start;
        }

        private static bool TryReadLenPrefixedAscii(byte[] raw, int offset, int end, out string text, out int size)
        {
            text = "";
            size = 0;

            if (offset >= end)
            {
                return false;
            }

            int len = raw[offset];
            if (len < 4 || len > 80)
            {
                return false;
            }

            if (offset + 1 + len > end)
            {
                return false;
            }

            for (int i = 0; i < len; i++)
            {
                byte b = raw[offset + 1 + i];
                if (b < 32 || b > 126)
                {
                    return false;
                }
            }

            string s = System.Text.Encoding.ASCII.GetString(raw, offset + 1, len);

            if (!IsValidAsciiRun(s))
            {
                return false;
            }

            text = s;
            size = 1 + len;
            return true;
        }

        private static bool TryReadLenPrefixedUtf16Le(byte[] raw, int offset, int end, out string text, out int size)
        {
            text = "";
            size = 0;

            if (offset >= end)
            {
                return false;
            }

            int charCount = raw[offset];
            if (charCount < 4 || charCount > 120)
            {
                return false;
            }

            int byteCount = charCount * 2;
            if (offset + 1 + byteCount > end)
            {
                return false;
            }

            // Expect printable ASCII stored as UTF-16LE (high byte is 0x00).
            for (int i = 0; i < charCount; i++)
            {
                byte lo = raw[offset + 1 + (i * 2)];
                byte hi = raw[offset + 1 + (i * 2) + 1];

                if (hi != 0x00)
                {
                    return false;
                }

                if (lo < 32 || lo > 126)
                {
                    return false;
                }
            }

            char[] chars = new char[charCount];
            for (int i = 0; i < charCount; i++)
            {
                chars[i] = (char)raw[offset + 1 + (i * 2)];
            }

            string s = new string(chars);

            if (!IsValidAsciiRun(s))
            {
                return false;
            }

            text = s;
            size = 1 + byteCount;
            return true;
        }

        private static bool TryReadAsciiZ(byte[] raw, int offset, int end, out string text, out int size)
        {
            text = "";
            size = 0;

            if (offset >= end)
            {
                return false;
            }

            if (raw[offset] < 32 || raw[offset] > 126)
            {
                return false;
            }

            int j = offset;
            while (j < end && raw[j] >= 32 && raw[j] <= 126)
            {
                j++;
            }

            int len = j - offset;
            if (len < 4)
            {
                return false;
            }

            if (j >= end || raw[j] != 0x00)
            {
                return false;
            }

            string s = System.Text.Encoding.ASCII.GetString(raw, offset, len);

            if (!IsValidAsciiRun(s))
            {
                return false;
            }

            text = s;
            size = len + 1;
            return true;
        }

        private static uint ReadU32LE(byte[] raw, int offset)
        {
            return (uint)(raw[offset] | (raw[offset + 1] << 8) | (raw[offset + 2] << 16) | (raw[offset + 3] << 24));
        }

        private static string FormatDword(uint u32, int i32, float f32)
        {
            if (u32 == 0)
            {
                return "0 (false)";
            }

            if (u32 == 1)
            {
                return "1 (true)";
            }

            // Very common "1.0f" pattern in your dump: 0x3F800000
            if (u32 == 0x3F800000)
            {
                return "1.0f (0x3F800000)";
            }

            // If it looks like a reasonable float, show float-first.
            if (!float.IsNaN(f32) && !float.IsInfinity(f32))
            {
                float af = Math.Abs(f32);
                if (af >= 0.000001f && af <= 1000000.0f)
                {
                    string fs = f32.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return fs + "f | u32=0x" + u32.ToString("X8") + " (" + u32.ToString() + ") | i32=" + i32.ToString();
                }
            }

            // Default: show both numeric interpretations.
            return "u32=0x" + u32.ToString("X8") + " (" + u32.ToString() + ") | i32=" + i32.ToString();
        }
    }
}