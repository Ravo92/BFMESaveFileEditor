namespace BFMESaveFileEditor.Classes
{
    static class ChunkNameMap
    {
        internal static readonly Dictionary<string, string> ChunkNameDictionary = new(StringComparer.OrdinalIgnoreCase)
            {
                { "CHUNK_LivingWorldLogicKOLB", "World Logic" },
                { "CHUNK_GameStateMapKOLB",    "Map State" },

                { "CHUNK_GameStateKOLB",        "Game State" },
                { "CHUNK_GameLogicKOLB",        "Game Logic" },

                { "CHUNK_CampaignKOLB",        "Heroes" },

                { "CHUNK_AudioKOLB",            "Audio State" },

                { "GLOBAL_SCIENCES",            "Global Powers / Spells" }
            };
    }
}
