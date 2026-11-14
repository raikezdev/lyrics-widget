using System;
using System.Collections.Generic;

namespace LyricsWidget.Models
{
    public class WordTiming
    {
        public string Word { get; set; } = string.Empty;
        public TimeSpan Timestamp { get; set; }
    }

    public class LyricLine
    {
        public TimeSpan Timestamp { get; set; }
        public string Text { get; set; } = string.Empty;
        public List<WordTiming> Words { get; set; } = new();
    }
}
