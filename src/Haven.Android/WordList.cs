// Where:    src/Haven.Android/WordList.cs
// What:     Embedded offline frequency word-list (~500 common English words) and
//           the suggestion engine used by the Haven Keyboard.
// How:      Words are stored lowercase and roughly frequency-ordered; earlier
//           entries win ranking ties. The suggestor performs prefix matching for
//           completions plus edit-distance-1 correction (insert/delete/substitute).
// Why:      The keyboard must work fully offline with zero AI and zero network.
//           A tiny static list gives useful completions without any learning,
//           history or personalisation store — nothing the user types is ever
//           written to disk by this file's engine.
// Maintenance: Keep every entry lowercase a-z only. Order matters (earlier =
//           higher rank). Do not add telemetry or persistence around this list.

namespace Haven.Android;

/// <summary>
/// Static embedded dictionary of common English words used for keyboard
/// suggestions. Purely local data; no network, storage or logging involved.
/// </summary>
internal static class HavenKeyboardWordList
{
    /// <summary>Common English words, roughly ordered from most to less frequent.</summary>
    internal static readonly string[] Words =
    [
        "the", "be", "to", "of", "and", "a", "in", "that", "have", "it",
        "for", "not", "on", "with", "he", "as", "you", "do", "at", "this",
        "but", "his", "by", "from", "they", "we", "say", "her", "she", "or",
        "an", "will", "my", "one", "all", "would", "there", "their", "what", "so",
        "up", "out", "if", "about", "who", "get", "which", "go", "me", "when",
        "make", "can", "like", "time", "no", "just", "him", "know", "take", "person",
        "into", "year", "your", "good", "some", "could", "them", "see", "other", "than",
        "then", "now", "look", "only", "come", "its", "over", "think", "also", "back",
        "after", "use", "two", "how", "our", "work", "first", "well", "way", "even",
        "new", "want", "because", "any", "these", "give", "day", "most", "is", "are",
        "was", "were", "been", "has", "had", "each", "tell", "does", "set", "three",
        "still", "small", "large", "point", "end", "read", "need", "land", "home", "hand",
        "big", "high", "little", "world", "own", "under", "last", "never", "old", "off",
        "again", "city", "life", "here", "both", "between", "must", "mean", "become", "hold",
        "eye", "open", "keep", "follow", "stop", "meet", "often", "short", "better", "best",
        "during", "however", "before", "move", "right", "boy", "girl", "man", "woman", "child",
        "school", "study", "learn", "teach", "student", "teacher", "book", "word", "write", "letter",
        "send", "message", "call", "phone", "computer", "internet", "website", "line", "page", "water",
        "food", "eat", "drink", "sleep", "walk", "run", "play", "game", "sport", "music",
        "song", "dance", "art", "picture", "draw", "color", "red", "blue", "green", "yellow",
        "black", "white", "money", "price", "market", "shop", "buy", "sell", "pay", "cost",
        "free", "job", "career", "company", "office", "boss", "team", "group", "friend", "family",
        "mother", "father", "brother", "sister", "son", "daughter", "parent", "baby", "house", "room",
        "door", "window", "floor", "wall", "table", "chair", "bed", "kitchen", "street", "road",
        "car", "bus", "train", "plane", "boat", "bike", "trip", "travel", "country", "state",
        "town", "village", "place", "address", "north", "south", "east", "west", "morning", "afternoon",
        "evening", "night", "today", "tomorrow", "yesterday", "week", "month", "hour", "minute", "second",
        "clock", "date", "calendar", "meeting", "event", "party", "holiday", "vacation", "guest", "visit",
        "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday", "january", "february", "march",
        "april", "may", "june", "july", "august", "september", "october", "november", "december", "spring",
        "summer", "autumn", "winter", "rain", "snow", "wind", "cloud", "sun", "moon", "star",
        "sky", "tree", "plant", "flower", "leaf", "grass", "river", "lake", "sea", "ocean",
        "mountain", "hill", "valley", "forest", "field", "stone", "sand", "fire", "air", "light",
        "heat", "cold", "warm", "hot", "cool", "body", "head", "face", "arm", "leg",
        "heart", "mouth", "tooth", "nose", "ear", "hair", "skin", "blood", "brain", "voice",
        "breath", "strength", "health", "doctor", "nurse", "hospital", "medicine", "pill", "sick", "pain",
        "hurt", "heal", "strong", "tired", "happy", "sad", "angry", "afraid", "brave", "calm",
        "kind", "polite", "funny", "smart", "quick", "slow", "early", "late", "soon", "long",
        "wide", "narrow", "deep", "heavy", "soft", "hard", "smooth", "rough", "clean", "dirty",
        "fresh", "sweet", "bitter", "salt", "sugar", "bread", "milk", "egg", "meat", "fish",
        "rice", "fruit", "apple", "orange", "banana", "grape", "lemon", "cherry", "potato", "carrot",
        "onion", "soup", "salad", "cup", "glass", "bottle", "plate", "bowl", "spoon", "fork",
        "knife", "bag", "box", "clothes", "shirt", "pants", "dress", "skirt", "shoe", "boot",
        "hat", "cap", "coat", "pocket", "silver", "gold", "iron", "wood", "paper", "pen",
        "pencil", "key", "lock", "rope", "wire", "tool", "machine", "engine", "wheel", "chain",
        "hammer", "screw", "brush", "sponge", "bucket", "ladder", "nail", "glue", "scissors", "candle",
        "story", "truth", "joke", "idea", "question", "answer", "reason", "result", "plan", "choice",
        "chance", "luck", "hope", "fear", "love", "peace", "power", "control", "rule", "law",
        "duty", "trouble", "mistake", "problem", "solution", "example", "fact", "number", "figure", "left",
        "count", "measure", "size", "shape", "test", "check", "start", "finish", "help", "thanks",
    ];
}

/// <summary>
/// Suggestion engine over <see cref="HavenKeyboardWordList"/>. Provides prefix
/// completions and edit-distance-1 corrections entirely in memory. It keeps no
/// state between calls, so there is no personalisation, learning or history to
/// disable — including on secure fields and IME_FLAG_NO_PERSONALIZED_LEARNING input.
/// </summary>
internal sealed class HavenKeyboardSuggestor
{
    private readonly string[] _words;

    /// <summary>Creates a suggestor over the default embedded word list.</summary>
    internal HavenKeyboardSuggestor()
        : this(HavenKeyboardWordList.Words)
    {
    }

    /// <summary>Creates a suggestor over a custom word list (primarily for tests).</summary>
    internal HavenKeyboardSuggestor(string[] words)
    {
        ArgumentNullException.ThrowIfNull(words);
        _words = words;
    }

    /// <summary>
    /// Returns up to <paramref name="max"/> candidates for the typed word:
    /// prefix completions first (ranked by list position), then edit-distance-1
    /// corrections. The typed word itself is never returned.
    /// </summary>
    internal IReadOnlyList<string> Suggest(string word, int max)
    {
        if (string.IsNullOrEmpty(word) || max <= 0)
        {
            return [];
        }

        var query = word.ToLowerInvariant();
        var results = new List<string>(max);

        // Pass 1: prefix completions, ranked purely by word-list position.
        foreach (var candidate in _words)
        {
            if (results.Count >= max)
            {
                break;
            }
            if (candidate.Length > query.Length && candidate.StartsWith(query, StringComparison.Ordinal))
            {
                results.Add(candidate);
            }
        }

        // Pass 2: edit-distance-1 corrections when completion slots remain.
        foreach (var candidate in _words)
        {
            if (results.Count >= max)
            {
                break;
            }
            if (candidate.StartsWith(query, StringComparison.Ordinal))
            {
                continue;
            }
            if (!results.Contains(candidate) && IsEditDistanceOne(query, candidate))
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    /// <summary>
    /// Returns the highest-ranked pure prefix extension of the typed word, or
    /// null when none exists. This is the ONLY candidate considered confident
    /// enough for autocorrect-on-space; edit-distance corrections insert literally.
    /// </summary>
    internal string? TopPrefixCompletion(string word)
    {
        if (string.IsNullOrEmpty(word) || word.Length < 2)
        {
            return null;
        }

        var query = word.ToLowerInvariant();
        foreach (var candidate in _words)
        {
            if (candidate.Length > query.Length && candidate.StartsWith(query, StringComparison.Ordinal))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Re-applies the leading capitalisation of <paramref name="typed"/> onto a
    /// dictionary replacement so "Helo" corrects to "Hello" rather than "hello".
    /// </summary>
    internal static string PreserveCase(string typed, string replacement)
    {
        if (typed.Length > 0 && replacement.Length > 0 && char.IsUpper(typed[0]))
        {
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        }
        return replacement;
    }

    /// <summary>
    /// True when the candidate can be produced from the query by exactly one
    /// insertion, deletion or substitution (classic Levenshtein distance of 1).
    /// </summary>
    private static bool IsEditDistanceOne(string query, string candidate)
    {
        var a = query;
        var b = candidate;
        if (Math.Abs(a.Length - b.Length) > 1)
        {
            return false;
        }

        if (a.Length == b.Length)
        {
            var differences = 0;
            for (var index = 0; index < a.Length; index++)
            {
                if (a[index] != b[index] && ++differences > 1)
                {
                    return false;
                }
            }
            return differences == 1;
        }

        // Length differs by one: skipping exactly one character of the longer
        // string must yield the shorter string.
        if (a.Length < b.Length)
        {
            (a, b) = (b, a);
        }

        for (var index = 0; index < b.Length; index++)
        {
            if (a[index] != b[index])
            {
                return string.CompareOrdinal(a[(index + 1)..], b[index..]) == 0;
            }
        }

        // The longer string is the shorter one plus one trailing character.
        return true;
    }
}
