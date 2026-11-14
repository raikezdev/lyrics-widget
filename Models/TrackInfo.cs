using System;

namespace LyricsWidget.Models
{
    public class TrackInfo
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string AlbumTitle { get; set; } = string.Empty;
        public byte[]? AlbumArtBytes { get; set; }
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;
        public TimeSpan Position { get; set; } = TimeSpan.Zero;
        public bool IsPlaying { get; set; }
        public string SourceApp { get; set; } = string.Empty;

        public bool IsEmpty => string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Artist);
    }
}
