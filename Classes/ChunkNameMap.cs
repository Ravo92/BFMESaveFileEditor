namespace BFMESaveFileEditor.Classes
{
    static class ChunkNameMap
    {
        internal static readonly Dictionary<string, string> ChunkNameDictionary = new(StringComparer.OrdinalIgnoreCase)
            {
                { "CHUNK_LivingWorldLogicKOLBE", "World / Map State" },
                { "CHUNK_GameStateMapKOLBu",    "World / Map State (Details)" },

                { "CHUNK_GameStateKOLB",        "Game State" },
                { "CHUNK_GameLogicKOLB",        "Game Logic" },

                { "CHUNK_CampaignKOLBH",        "Campaign / Heroes" },

                { "CHUNK_AudioKOLB",            "Audio State" },

                { "GLOBAL_SCIENCES",            "Global Powers / Spells" }
            };
    }
}
