namespace BFMESaveFileEditor
{
    class SaveGameParser
    {
        public static SaveGameFile Parse(byte[] raw)
        {
            ArgumentNullException.ThrowIfNull(raw);

            if (raw.Length < 8) throw new InvalidOperationException("File too small.");

            if (!BinaryUtil.StartsWithAscii(raw, 0, "ALAE2STR"))
            {

            }

            List<int> chunkOffsets = [];
            int cursor = 0;

            while (true)
            {
                int idx = BinaryUtil.FindAscii(raw, "CHUNK_", cursor);
                if (idx < 0) break;

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

                Chunk chunk = new()
                {
                    Name = chunkName,
                    Offset = start,
                    Length = length,
                    Entries = ExtractEntries(raw, start, end)
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

        private static List<Entry> ExtractEntries(byte[] raw, int start, int end)
        {
            List<Entry> entries = [];

            string? currentOwner = null;
            int currentOwnerIndex = -1;

            int i = start;

            while (i < end)
            {
                if (i + 1 < end && raw[i] >= 32 && raw[i] <= 126 && raw[i + 1] == 0x00)
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
                    }

                    if (charCount >= 4)
                    {
                        System.Text.StringBuilder stringBuilder = new(charCount);
                        int k = i;

                        for (int c = 0; c < charCount; c++)
                        {
                            stringBuilder.Append((char)raw[k]);
                            k += 2;
                        }

                        string s = stringBuilder.ToString();
                        int size = charCount * 2;

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

                        if (IsValidAsciiRun(s))
                        {
                            bool isZ = (j < end && raw[j] == 0x00);
                            int size = isZ ? (len + 1) : len;

                            if (string.Equals(s, "SG_EOF", StringComparison.OrdinalIgnoreCase))
                            {
                                i += size;
                                continue;
                            }

                            // Hero-Owner?
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
            if (string.IsNullOrEmpty(dataset)) return string.Empty;

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

            return stringBuilder.ToString();
        }


        private static bool IsValidAsciiRun(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s.Length < 4) return false;
            if (s.Length > 512) return false;

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
    }
}