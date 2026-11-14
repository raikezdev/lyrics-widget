using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LyricsWidget.Services
{
    public static class CapCutFormatter
    {
        private static readonly Dictionary<string, string> EmojiMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "girl", "👧" },
            { "girls", "👧" },
            { "baby", "🗣️" },
            { "babe", "💛" },
            { "love", "💛" },
            { "heart", "💖" },
            { "lights", "✨" },
            { "light", "✨" },
            { "blinded", "🕶️" },
            { "touch", "👆" },
            { "trust", "🤝" },
            { "night", "🌙" },
            { "sun", "☀️" },
            { "city", "🏙️" },
            { "road", "🛣️" },
            { "drive", "🚗" },
            { "cadillac", "🚗" },
            { "car", "🚗" },
            { "eat", "🍴" },
            { "vegetables", "🥭" },
            { "time", "⏳" },
            { "fire", "🔥" },
            { "hot", "🔥" },
            { "ice", "❄️" },
            { "cold", "❄️" },
            { "phone", "📱" },
            { "call", "📞" },
            { "money", "💸" },
            { "rain", "🌧️" },
            { "dance", "💃" },
            { "music", "🎵" },
            { "sleep", "😴" },
            { "star", "⭐" },
            { "wheel", "🚗" },
            { "recline", "💺" },
            { "sit", "🧘" },
            { "still", "✨" },
            { "happy", "😊" },
            { "sad", "😢" },
            { "pain", "💔" },
            { "rich", "💰" },
            { "eyes", "👀" },
            { "look", "👀" },
            { "see", "👁️" },
            { "bitch", "💅" },
            { "shit", "💥" },
            { "streets", "🏙️" },
            { "king", "👑" },
            { "world", "🌍" },
            { "sky", "🌌" }
        };

        public class CapCutStack
        {
            public string TopText { get; set; } = string.Empty;
            public string CenterText { get; set; } = string.Empty;
            public string BottomText { get; set; } = string.Empty;
            public string TopEmoji { get; set; } = string.Empty;
            public string BottomEmoji { get; set; } = string.Empty;
        }

        public static CapCutStack FormatCapCutStack(string fullLine)
        {
            var stack = new CapCutStack();
            if (string.IsNullOrWhiteSpace(fullLine)) return stack;

            var words = fullLine.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return stack;

            if (words.Length == 1)
            {
                stack.CenterText = words[0];
                stack.BottomEmoji = FindEmoji(words[0]);
                return stack;
            }

            if (words.Length == 2)
            {
                stack.TopText = words[0];
                stack.CenterText = words[1];
                stack.TopEmoji = FindEmoji(words[0]);
                stack.BottomEmoji = FindEmoji(words[1]);
                return stack;
            }

            if (words.Length == 3)
            {
                stack.TopText = words[0];
                stack.CenterText = words[1];
                stack.BottomText = words[2];
                stack.TopEmoji = FindEmoji(words[0]);
                stack.BottomEmoji = FindEmoji(words[2]);
                return stack;
            }

            int lastIndex = words.Length - 1;
            int midCount = Math.Max(1, (words.Length - 1) / 2);

            var topWords = new List<string>();
            var midWords = new List<string>();

            for (int i = 0; i < words.Length - 1; i++)
            {
                if (i < words.Length - 1 - midCount)
                {
                    topWords.Add(words[i]);
                }
                else
                {
                    midWords.Add(words[i]);
                }
            }

            stack.TopText = string.Join(" ", topWords);
            stack.CenterText = string.Join(" ", midWords);
            stack.BottomText = words[lastIndex];

            stack.TopEmoji = FindEmoji(stack.TopText);
            stack.BottomEmoji = FindEmoji(stack.BottomText);

            return stack;
        }

        public static string FormatTerminalWords(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return string.Empty;
            var words = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(".", words).ToLowerInvariant();
        }

        public static string FindWordEmoji(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return string.Empty;
            string clean = Regex.Replace(word, @"[^\w]", "");
            if (EmojiMap.TryGetValue(clean, out var emoji)) return emoji;
            return string.Empty;
        }

        private static string FindEmoji(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var cleanWords = Regex.Replace(text, @"[^\w\s]", "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var w in cleanWords)
            {
                if (EmojiMap.TryGetValue(w, out var emoji))
                {
                    return emoji;
                }
            }

            return string.Empty;
        }
    }
}
