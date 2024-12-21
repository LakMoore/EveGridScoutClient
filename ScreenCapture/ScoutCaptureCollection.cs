using CaptureCore;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Xml.Serialization;
using Tesseract;

namespace GridScout
{
    [XmlRoot("Dictionary")]
    public class ScoutCaptureCollection
    {
        [XmlElement("Entry")]
        public List<ScoutCapture> Entries { get; set; } = new List<ScoutCapture>();

        public bool ContainsKey(string key)
        {
            return Entries.Find(x => x.Key == key) != null;
        }

        public ScoutCapture Get(string key)
        {
            return Entries.Find(x => x.Key == key);
        }

        public void Add(ScoutCapture entry)
        {
            Entries.Add(entry);
        }

        public void Remove(string key)
        {
            Entries.RemoveAll(x => x.Key == key);
        }

        public ScoutCapture GetNextInOrder(ScoutCapture entry)
        {
            var index = Entries.IndexOf(entry);
            do
            {
                index++;
                if (index >= Entries.Count) index = 0;
            } while (Entries[index].Capture == null);
            return Entries[index];
        }

    }

    public class ScoutCapture
    {
        [XmlElement("Key")]
        public string Key { get; set; }

        [XmlElement("Value")]
        public Thickness Margins { get; set; }

        [XmlIgnore]
        public BasicCapture Capture { get; set; }

        [XmlIgnore]
        public Pix LastPix { get; set; }

        [XmlIgnore]
        public BitmapImage LastImage { get; set; }

    }
}
