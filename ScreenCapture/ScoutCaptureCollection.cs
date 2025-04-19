using CaptureCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Xml.Serialization;
using Tesseract;
using NAudio.Wave;

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

        public ScoutCapture CreateNew(string key)
        {
            return new ScoutCapture()
            {
                Key = key,
                Margins = new Thickness()
            };
        }

        public ScoutCapture Get(string key)
        {
            return Entries.Find(x => x.Key == key);
        }

        public ScoutCapture GetOrDefault(string key)
        {
            if (Entries == null || Entries.Count == 0) return CreateNew(key);
            var entry = Entries.Find(x => x.Key == key);
            return entry ?? CreateNew(key);
        }

        public void Add(ScoutCapture entry)
        {
            Entries.Add(entry);
        }

        public void Remove(string key)
        {
            Entries.RemoveAll(x => x.Key == key);
        }

        public async Task<ScoutCapture> GetNextInOrder(ScoutCapture entry)
        {
            var index = Entries.IndexOf(entry);
            var startIndex = index;
            var loopCount = 0;
            ScoutCapture temp;
            do
            {
                index++;
                if (index >= Entries.Count) index = 0;
                temp = Entries[index];

                // if we've been all the way round there could be a problem, breath a little
                if (index == startIndex && loopCount > Entries.Count)
                {
                    await Task.Delay(200);
                }
                loopCount++;
            } while (
                temp == null 
                || temp.Capture == null 
                || temp.Capture.GetItem() == null
                || temp.IsMinimized
            );
            return temp;
        }

    }

    public class ScoutCapture
    {
        [XmlIgnore]
        private readonly object syncLock = new object();

        [XmlIgnore]
        private bool _isMinimized;

        [XmlElement("Key")]
        public string Key { get; set; }

        [XmlElement("Value")]
        public Thickness Margins { get; set; }

        [XmlIgnore]
        public BasicCapture Capture { get; set; }

        [XmlIgnore]
        public WasapiLoopbackCapture AudioCapture { get; set; }

        [XmlIgnore]
        public Pix LastCapture { get; set; }

        [XmlIgnore]
        public long LastReportTime { get; set; }

        [XmlIgnore]
        public BitmapImage LastImage { get; set; }

        // Synchornised

        [XmlIgnore]
        public Boolean IsMinimized { 
            get {
                lock (syncLock)
                {
                    return _isMinimized;
                }
            }
            set {
                lock (syncLock)
                {
                    _isMinimized = value;
                }
            }
        }

        public void StopCapture()
        {
            if (AudioCapture != null)
            {
                AudioCapture.StopRecording();
                AudioCapture.Dispose();
                AudioCapture = null;
            }
            if (Capture != null)
            {
                Capture.Dispose();
                Capture = null;
            }
        }
    }
}
