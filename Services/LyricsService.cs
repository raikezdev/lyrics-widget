using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LyricsWidget.Models;

namespace LyricsWidget.Services
{
    public class LyricsService
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private readonly ConcurrentDictionary<string, List<LyricLine>> _cache = new();
        private static readonly Regex LrcRegex = new(@"^\[(\d{1,2}):(\d{2})(?:\.(\d{1,3}))?\](.*)$", RegexOptions.Compiled);

        static LyricsService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "LyricsWidget/1.0 (Windows NT; C# WPF)");
        }

        public async Task<List<LyricLine>> GetLyricsAsync(string rawTitle, string rawArtist, TimeSpan duration, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rawTitle))
                return new List<LyricLine>();

            var (title, artist) = ParseTrackAndArtist(rawTitle, rawArtist);

            string cacheKey = $"{CleanString(artist)}_{CleanString(title)}_{(int)duration.TotalSeconds}";
            if (_cache.TryGetValue(cacheKey, out var cachedLyrics))
            {
                return cachedLyrics;
            }

            var lyrics = await FetchLyricsWithFallbacksAsync(title, artist, rawTitle, duration, ct);
            if (lyrics != null && lyrics.Count > 0)
            {
                _cache[cacheKey] = lyrics;
            }

            return lyrics ?? new List<LyricLine>();
        }

        private async Task<List<LyricLine>> FetchLyricsWithFallbacksAsync(string title, string artist, string rawTitle, TimeSpan duration, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist))
            {
                var exact = await TryGetExactAsync(title, artist, duration, ct);
                if (exact.Count > 0) return exact;

                var swapped = await TryGetExactAsync(artist, title, duration, ct);
                if (swapped.Count > 0) return swapped;

                var targeted = await TrySearchTargetedAsync(title, artist, duration, ct);
                if (targeted.Count > 0) return targeted;

                var targetedSwapped = await TrySearchTargetedAsync(artist, title, duration, ct);
                if (targetedSwapped.Count > 0) return targetedSwapped;
            }

            var searchQueries = new List<string>();

            if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(title))
            {
                searchQueries.Add($"{artist} {title}");
                searchQueries.Add($"{title} {artist}");
            }

            if (!string.IsNullOrWhiteSpace(title) && title.Length >= 2)
            {
                searchQueries.Add(title);
            }

            var segments = rawTitle.Split(new[] { " - ", " | ", " • ", " / " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var seg in segments)
            {
                string cleanSeg = CleanJunkWords(seg);
                if (cleanSeg.Length >= 3 && !searchQueries.Contains(cleanSeg, StringComparer.OrdinalIgnoreCase))
                {
                    searchQueries.Add(cleanSeg);
                }
            }

            string cleanRaw = CleanJunkWords(rawTitle);
            if (!string.IsNullOrWhiteSpace(cleanRaw) && !searchQueries.Contains(cleanRaw, StringComparer.OrdinalIgnoreCase))
            {
                searchQueries.Add(cleanRaw);
            }

            foreach (var q in searchQueries)
            {
                if (ct.IsCancellationRequested) break;
                var results = await TrySearchAsync(q, duration, ct);
                if (results.Count > 0)
                {
                    return results;
                }
            }

            if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(title))
            {
                var ovh = await TryFetchFromLyricsOvhAsync(artist, title, duration, ct);
                if (ovh.Count > 0) return ovh;

                var ovhSwapped = await TryFetchFromLyricsOvhAsync(title, artist, duration, ct);
                if (ovhSwapped.Count > 0) return ovhSwapped;
            }

            return new List<LyricLine>();
        }

        private async Task<List<LyricLine>> TryFetchFromLyricsOvhAsync(string artist, string title, TimeSpan duration, CancellationToken ct)
        {
            try
            {
                string cleanArtist = CleanJunkWords(artist).Trim();
                string cleanTitle = CleanJunkWords(title).Trim();
                if (string.IsNullOrWhiteSpace(cleanArtist) || string.IsNullOrWhiteSpace(cleanTitle)) return new List<LyricLine>();

                string url = $"https://api.lyrics.ovh/v1/{Uri.EscapeDataString(cleanArtist)}/{Uri.EscapeDataString(cleanTitle)}";
                using var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("lyrics", out var lyricsProp))
                    {
                        string plainText = lyricsProp.GetString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(plainText))
                        {
                            return ParsePlainLyrics(plainText, duration);
                        }
                    }
                }
            }
            catch { }
            return new List<LyricLine>();
        }

        private async Task<List<LyricLine>> TryGetExactAsync(string title, string artist, TimeSpan duration, CancellationToken ct)
        {
            try
            {
                if (duration.TotalSeconds > 10)
                {
                    string urlWithDuration = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}&duration={(int)duration.TotalSeconds}";
                    var res = await FetchFromUrlAsync(urlWithDuration, ct);
                    if (res.Count > 0) return res;
                }
                string urlWithoutDuration = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
                var res2 = await FetchFromUrlAsync(urlWithoutDuration, ct);
                if (res2.Count > 0) return res2;
            }
            catch { }

            return new List<LyricLine>();
        }

        private async Task<List<LyricLine>> TrySearchTargetedAsync(string title, string artist, TimeSpan targetDuration, CancellationToken ct)
        {
            try
            {
                string url = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
                using var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    var results = JsonSerializer.Deserialize<List<LrcLibRecord>>(content);
                    if (results != null && results.Count > 0)
                    {
                        var best = PickBestRecord(results, targetDuration);
                        if (best != null)
                        {
                            if (!string.IsNullOrWhiteSpace(best.SyncedLyrics))
                            {
                                return ParseLrc(best.SyncedLyrics);
                            }
                            if (!string.IsNullOrWhiteSpace(best.PlainLyrics))
                            {
                                return ParsePlainLyrics(best.PlainLyrics);
                            }
                        }
                    }
                }
            }
            catch { }
            return new List<LyricLine>();
        }

        private async Task<List<LyricLine>> FetchFromUrlAsync(string url, CancellationToken ct)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    var record = JsonSerializer.Deserialize<LrcLibRecord>(content);
                    if (record != null && !string.IsNullOrWhiteSpace(record.SyncedLyrics))
                    {
                        return ParseLrc(record.SyncedLyrics);
                    }
                    if (record != null && !string.IsNullOrWhiteSpace(record.PlainLyrics))
                    {
                        return ParsePlainLyrics(record.PlainLyrics);
                    }
                }
            }
            catch { }
            return new List<LyricLine>();
        }

        private async Task<List<LyricLine>> TrySearchAsync(string query, TimeSpan targetDuration, CancellationToken ct)
        {
            try
            {
                string cleanQuery = CleanJunkWords(query).Trim();
                if (string.IsNullOrWhiteSpace(cleanQuery) || cleanQuery.Length < 2) return new List<LyricLine>();

                string url = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(cleanQuery)}";
                using var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    var results = JsonSerializer.Deserialize<List<LrcLibRecord>>(content);
                    if (results != null && results.Count > 0)
                    {
                        var best = PickBestRecord(results, targetDuration);
                        if (best != null)
                        {
                            if (!string.IsNullOrWhiteSpace(best.SyncedLyrics))
                            {
                                return ParseLrc(best.SyncedLyrics);
                            }
                            if (!string.IsNullOrWhiteSpace(best.PlainLyrics))
                            {
                                return ParsePlainLyrics(best.PlainLyrics);
                            }
                        }
                    }
                }
            }
            catch { }

            return new List<LyricLine>();
        }

        private static LrcLibRecord? PickBestRecord(List<LrcLibRecord> records, TimeSpan targetDuration)
        {
            var withSynced = records.Where(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics)).ToList();
            var candidates = withSynced.Count > 0 ? withSynced : records.Where(r => !string.IsNullOrWhiteSpace(r.PlainLyrics)).ToList();

            if (candidates.Count == 0) return null;

            return candidates.OrderBy(r =>
            {
                double score = 0;
                if (!string.IsNullOrWhiteSpace(r.SyncedLyrics)) score -= 1000;
                if (r.HasWordSync) score -= 200;

                if (targetDuration.TotalSeconds > 10 && r.Duration.HasValue && r.Duration.Value > 0)
                {
                    double diff = Math.Abs(r.Duration.Value - targetDuration.TotalSeconds);
                    score += Math.Min(600, diff * 1.5);
                }
                return score;
            }).FirstOrDefault();
        }

        public async Task<List<LyricSearchResult>> SearchSongsDetailedAsync(string query, CancellationToken ct = default)
        {
            var list = new List<LyricSearchResult>();
            string cleanQuery = CleanJunkWords(query).Trim();
            if (string.IsNullOrWhiteSpace(cleanQuery)) return list;

            try
            {
                string url = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(cleanQuery)}";
                using var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    var records = JsonSerializer.Deserialize<List<LrcLibRecord>>(content);
                    if (records != null)
                    {
                        foreach (var r in records.Take(15))
                        {
                            list.Add(new LyricSearchResult
                            {
                                Id = r.Id,
                                Title = r.TrackName ?? r.Name ?? cleanQuery,
                                Artist = r.ArtistName ?? "Unknown Artist",
                                Duration = TimeSpan.FromSeconds(r.Duration ?? 0),
                                HasSyncedLyrics = !string.IsNullOrWhiteSpace(r.SyncedLyrics),
                                SyncedLyrics = r.SyncedLyrics,
                                PlainLyrics = r.PlainLyrics
                            });
                        }
                    }
                }
            }
            catch { }
            if (list.Count == 0)
            {
                var (pTitle, pArtist) = ParseTrackAndArtist(query, string.Empty);
                var ovh = await TryFetchFromLyricsOvhAsync(pArtist, pTitle, TimeSpan.Zero, ct);
                if (ovh.Count > 0)
                {
                    list.Add(new LyricSearchResult
                    {
                        Id = 999999,
                        Title = string.IsNullOrWhiteSpace(pTitle) ? cleanQuery : pTitle,
                        Artist = string.IsNullOrWhiteSpace(pArtist) ? "Lyrics.ovh" : pArtist,
                        Duration = TimeSpan.FromMinutes(3.5),
                        HasSyncedLyrics = false,
                        PlainLyrics = string.Join("\n", ovh.Select(x => x.Text))
                    });
                }
            }

            return list;
        }

        public static (string Title, string Artist) ParseTrackAndArtist(string rawTitle, string rawArtist)
        {
            string title = rawTitle ?? string.Empty;
            string artist = rawArtist ?? string.Empty;

            if (title.StartsWith("Mix - ", StringComparison.OrdinalIgnoreCase))
                title = title.Substring(6).Trim();
            else if (title.StartsWith("My Mix - ", StringComparison.OrdinalIgnoreCase))
                title = title.Substring(9).Trim();
            else if (title.StartsWith("YouTube Mix - ", StringComparison.OrdinalIgnoreCase))
                title = title.Substring(14).Trim();
            else if (title.StartsWith("Playlist - ", StringComparison.OrdinalIgnoreCase))
                title = title.Substring(11).Trim();

            if (artist.EndsWith("VEVO", StringComparison.OrdinalIgnoreCase))
            {
                artist = artist.Substring(0, artist.Length - 4).Trim();
            }
            if (artist.EndsWith("- Topic", StringComparison.OrdinalIgnoreCase))
            {
                artist = artist.Substring(0, artist.Length - 7).Trim();
            }

            bool isGenericArtist = string.IsNullOrWhiteSpace(artist) ||
                                   artist.Equals("YouTube", StringComparison.OrdinalIgnoreCase) ||
                                   artist.Equals("YouTube Music", StringComparison.OrdinalIgnoreCase) ||
                                   artist.Equals("Google Chrome", StringComparison.OrdinalIgnoreCase) ||
                                   artist.Equals("Chrome", StringComparison.OrdinalIgnoreCase) ||
                                   artist.Equals("Brave", StringComparison.OrdinalIgnoreCase) ||
                                   artist.Equals("Microsoft Edge", StringComparison.OrdinalIgnoreCase) ||
                                   artist.Equals("Edge", StringComparison.OrdinalIgnoreCase) ||
                                   artist.Equals("Media", StringComparison.OrdinalIgnoreCase);

            if (title.Contains(" - "))
            {
                var parts = title.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string candidateArtist = CleanJunkWords(parts[0]).Trim();
                    string candidateTitle = CleanJunkWords(parts[1]).Trim();

                    if (isGenericArtist ||
                        candidateArtist.Replace(" ", "").Equals(artist.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                        artist.Contains("VEVO", StringComparison.OrdinalIgnoreCase))
                    {
                        artist = candidateArtist;
                        title = candidateTitle;
                    }
                    else
                    {
                        title = candidateTitle;
                    }
                }
            }
            else if (title.Contains(" | "))
            {
                var parts = title.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    title = CleanJunkWords(parts[0]).Trim();
                    if (isGenericArtist)
                    {
                        artist = CleanJunkWords(parts[1]).Trim();
                    }
                }
            }
            else
            {
                title = CleanJunkWords(title).Trim();
            }

            if (!isGenericArtist)
            {
                artist = CleanJunkWords(artist).Trim();
            }

            return (title, artist);
        }

        public static string CleanJunkWords(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            string cleaned = Regex.Replace(input, @"\s*[\(\[](official\s*(music\s*)?(video|audio|visualizer|lyric\s*video|track)?|audio|lyrics|hd|4k|remaster(ed)?|deluxe|explicit|bonus\s*track|sub\s*hd|custom\s*made|hindi|english|live|mv).*?[\)\]]", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s*[\(\[](feat|ft)\.?\s+[^\)\]]+[\)\]]", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s+(feat|ft)\.?\s+.*$", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\b(official video|official audio|official music video|lyric video|music video|full video|full audio|20sec|sub hd|custom made|mv)\b", "", RegexOptions.IgnoreCase);

            cleaned = cleaned.Trim(' ', '"', '\'', '-', '|', '[', ']');

            return cleaned.Trim();
        }

        public static List<LyricLine> ParseLrc(string lrcContent)
        {
            var lines = new List<LyricLine>();
            if (string.IsNullOrWhiteSpace(lrcContent))
                return lines;

            var rawLines = lrcContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var rawLine in rawLines)
            {
                var match = LrcRegex.Match(rawLine.Trim());
                if (match.Success)
                {
                    int minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    int seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    int milliseconds = 0;

                    if (match.Groups[3].Success)
                    {
                        string msStr = match.Groups[3].Value;
                        if (msStr.Length == 2)
                            milliseconds = int.Parse(msStr, CultureInfo.InvariantCulture) * 10;
                        else if (msStr.Length == 3)
                            milliseconds = int.Parse(msStr, CultureInfo.InvariantCulture);
                        else if (msStr.Length == 1)
                            milliseconds = int.Parse(msStr, CultureInfo.InvariantCulture) * 100;
                    }

                    var timestamp = new TimeSpan(0, 0, minutes, seconds, milliseconds);
                    string text = match.Groups[4].Value.Trim();

                    lines.Add(new LyricLine
                    {
                        Timestamp = timestamp,
                        Text = string.IsNullOrEmpty(text) ? "♪" : text
                    });
                }
            }

            lines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                TimeSpan duration;
                if (i + 1 < lines.Count)
                {
                    duration = lines[i + 1].Timestamp - line.Timestamp;
                    if (duration > TimeSpan.FromSeconds(5.5)) duration = TimeSpan.FromSeconds(4.0);
                    if (duration < TimeSpan.FromSeconds(1.0)) duration = TimeSpan.FromSeconds(2.0);
                }
                else
                {
                    duration = TimeSpan.FromSeconds(3.5);
                }

                var words = line.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0)
                {
                    double totalWeight = words.Sum(w => Math.Max(2, w.Length));
                    double currentSec = 0;

                    for (int w = 0; w < words.Length; w++)
                    {
                        var word = words[w];
                        double wordWeight = Math.Max(2, word.Length);
                        double wordFraction = wordWeight / totalWeight;

                        line.Words.Add(new WordTiming
                        {
                            Word = word,
                            Timestamp = line.Timestamp + TimeSpan.FromSeconds(currentSec)
                        });

                        currentSec += wordFraction * duration.TotalSeconds;
                    }
                }
            }

            return lines;
        }

        public static List<LyricLine> ParsePlainLyrics(string plain, TimeSpan duration = default)
        {
            var lines = new List<LyricLine>();
            var rawLines = plain.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (rawLines.Length == 0) return lines;

            double stepSeconds = 4.0;
            if (duration.TotalSeconds > 15)
            {
                stepSeconds = Math.Clamp((duration.TotalSeconds - 10) / rawLines.Length, 2.0, 7.0);
            }

            int idx = 0;
            foreach (var r in rawLines)
            {
                string cleanLine = r.Trim();
                if (string.IsNullOrWhiteSpace(cleanLine)) continue;

                var line = new LyricLine
                {
                    Timestamp = TimeSpan.FromSeconds(idx * stepSeconds),
                    Text = cleanLine
                };

                var words = line.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                double wordStep = words.Length > 0 ? (stepSeconds / words.Length) : 0.4;
                for (int w = 0; w < words.Length; w++)
                {
                    line.Words.Add(new WordTiming
                    {
                        Word = words[w],
                        Timestamp = line.Timestamp + TimeSpan.FromSeconds(w * wordStep)
                    });
                }

                lines.Add(line);
                idx++;
            }
            return lines;
        }

        private static string CleanString(string input)
        {
            return (input ?? string.Empty).Trim().ToLowerInvariant();
        }

        private class LrcLibRecord
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("trackName")]
            public string? TrackName { get; set; }

            [JsonPropertyName("artistName")]
            public string? ArtistName { get; set; }

            [JsonPropertyName("albumName")]
            public string? AlbumName { get; set; }

            [JsonPropertyName("duration")]
            public double? Duration { get; set; }

            [JsonPropertyName("instrumental")]
            public bool Instrumental { get; set; }

            [JsonPropertyName("hasWordSync")]
            public bool HasWordSync { get; set; }

            [JsonPropertyName("plainLyrics")]
            public string? PlainLyrics { get; set; }

            [JsonPropertyName("syncedLyrics")]
            public string? SyncedLyrics { get; set; }
        }
    }

    public class LyricSearchResult
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public bool HasSyncedLyrics { get; set; }
        public string? SyncedLyrics { get; set; }
        public string? PlainLyrics { get; set; }
    }
}
