"""ProfileEntry, similarity scoring, and stylistic-note filtering."""

import re

# Below this Jaccard score, two notes are treated as different.
PROFILE_SIMILARITY_THRESHOLD = 0.5

# A note must be observed at least this many times before the Critic sees it.
PROFILE_MIN_COUNT_TO_SURFACE = 2

# Hard cap on candidate notes; oldest pending entry is evicted first.
MAX_PROFILE_ENTRIES = 10


class ProfileEntry:
    """A candidate Answerer-tendency note with an observation count."""
    __slots__ = ("text", "count")

    def __init__(self, text):
        self.text = text
        self.count = 1


_STOPWORDS = {
    "the", "a", "an", "to", "of", "in", "on", "at", "for", "with", "and", "or",
    "but", "is", "are", "was", "were", "be", "been", "being", "have", "has",
    "had", "do", "does", "did", "will", "would", "could", "should", "may",
    "might", "can", "tends", "tend", "he", "his", "him", "it", "its", "they",
    "them", "their", "this", "that", "these", "those", "as", "if", "than",
    "then", "so", "such", "very", "more", "most", "less", "least", "by",
    "from", "into", "about", "over", "under", "again",
}


def _tokens(text):
    words = re.findall(r"[a-z][a-z\-]+", text.lower())
    return {w for w in words if w not in _STOPWORDS and len(w) > 2}


def similarity(a, b):
    ta, tb = _tokens(a), _tokens(b)
    if not ta or not tb:
        return 0.0
    return len(ta & tb) / len(ta | tb)


_STYLISTIC_KEYWORDS = frozenset({
    "length", "lengthy", "long", "longer", "short", "shorter", "brief",
    "verbose", "concise", "terse", "wordy", "rambling",
    "format", "formatting", "formatted", "structure", "structured",
    "bullet", "bullets", "list", "lists", "heading", "headings",
    "header", "headers", "section", "sections", "paragraph", "paragraphs",
    "indent", "indentation", "whitespace", "newline", "newlines",
    "markdown", "prose",
    "tone", "register", "style", "stylistic", "voice", "phrasing",
    "wording", "vocabulary", "diction", "language",
    "casual", "formal", "informal", "playful", "serious",
    "friendly", "polite", "blunt",
    "emoji", "emojis", "emoticon", "emoticons",
    "punctuation", "capitalization", "uppercase", "lowercase",
    "bold", "italic", "italics",
    "readable", "readability", "presentation", "layout",
    "organize", "organized", "organization",
})


def is_stylistic(note):
    note_tokens = re.findall(r"[a-z][a-z\-]+", note.lower())
    for tok in note_tokens:
        if tok in _STYLISTIC_KEYWORDS:
            return True, tok
    return False, None