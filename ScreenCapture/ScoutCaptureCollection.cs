using CaptureSampleCore;
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
    }

    public class ScoutCapture
    {
        [XmlElement("Key")]
        public string Key { get; set; }

        [XmlElement("Value")]
        public Thickness margins { get; set; }

        [XmlIgnore]
        public BasicCapture capture { get; set; }

        [XmlIgnore]
        public Pix lastPix { get; set; }

        [XmlIgnore]
        public BitmapImage lastImage { get; set; }

    }
}
