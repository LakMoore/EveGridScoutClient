using System.IO;
using System.Xml.Serialization;

namespace GridScout2
{
    internal class GameClientCache
    { 
        private static List<GameClient> _uiRootCache = [];

        public static void LoadCache()
        {
            string xmlString = Properties.Settings.Default.uiRootAddressCache;
            if (string.IsNullOrEmpty(xmlString))
            {
                return;
            }
            XmlSerializer serializer = new(typeof(List<GameClient>), []);
            using var reader = new StringReader(xmlString);
            if (serializer.Deserialize(reader) is List<GameClient> serializableDictionary)
            {
                _uiRootCache = serializableDictionary;
            }
        }

        public static void SaveCache()
        {
            XmlSerializer serializer = new(typeof(List<GameClient>), []);
            using var writer = new StringWriter();
            serializer.Serialize(writer, _uiRootCache);
            Properties.Settings.Default.uiRootAddressCache = writer.ToString();
            Properties.Settings.Default.Save(); // Persist the changes
        }

        // get a game client from the cache, or null if not found
        public static GameClient GetGameClient(int processId)
        {
            GameClient? gameClient = _uiRootCache.FirstOrDefault(x => x.processId == processId);

            // if not found, make a new one
            if (gameClient == null)
            {
                gameClient = new GameClient() { processId = processId };
                _uiRootCache.Add(gameClient);
            }

            return gameClient;
        } 
    }
}
