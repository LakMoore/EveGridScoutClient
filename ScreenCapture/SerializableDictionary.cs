using System.Collections.Generic;
using System.Windows;
using System.Xml.Serialization;

namespace GridScout
{
    [XmlRoot("Dictionary")]
    public class SerializableDictionary
    {
        [XmlElement("Entry")]
        public List<DictionaryEntry> Entries { get; set; } = new List<DictionaryEntry>();
    }

    public class DictionaryEntry
    {
        [XmlElement("Key")]
        public string Key { get; set; }

        [XmlElement("Value")]
        public Thickness Value { get; set; }
    }
}
