"""
DebateContext — the shared "bag" of session-wide state.

Each actor stores its own data on itself. Cross-actor data
(prior rephrased questions, profile entries, references to all actors)
lives here so any actor or command can read it.
"""

from .profile import (
    MAX_PROFILE_ENTRIES,
    PROFILE_MIN_COUNT_TO_SURFACE,
    PROFILE_SIMILARITY_THRESHOLD,
    ProfileEntry,
    similarity,
)
from .stats import SessionStats
from .ui import print_info


class DebateContext:
    """
    Holds shared state. Actors are attached after construction.
    """

    def __init__(self, config, provider):
        self.config = config
        self.provider = provider

        # Cross-round state.
        self.prior_rephrased = []
        self.profile_entries = []

        # Per-session metrics. Cleared by `!new` via clear_session().
        self.stats = SessionStats()

        # Actors (attached by main()).
        self.system = None
        self.answerer = None
        self.critic = None
        self.judge = None

    def active_profile(self):
        return [
            e.text for e in self.profile_entries
            if e.count >= PROFILE_MIN_COUNT_TO_SURFACE
        ]

    def pending_profile(self):
        return [
            e for e in self.profile_entries
            if e.count < PROFILE_MIN_COUNT_TO_SURFACE
        ]

    def record_profile_note(self, note):
        """
        Merge a new profile note into the profile entries, or insert it as a
        new candidate. Picks the most similar existing entry; if similarity
        clears the threshold, that entry's count is incremented. Otherwise a
        fresh ProfileEntry is appended, evicting the oldest single-occurrence
        candidate first when at capacity.
        """
        entries = self.profile_entries

        best_idx = -1
        best_sim = 0.0
        for i, entry in enumerate(entries):
            sim = similarity(note, entry.text)
            if sim > best_sim:
                best_sim = sim
                best_idx = i

        if best_idx >= 0 and best_sim >= PROFILE_SIMILARITY_THRESHOLD:
            entries[best_idx].count += 1
            entry = entries[best_idx]
            tag = (
                "now active"
                if entry.count == PROFILE_MIN_COUNT_TO_SURFACE
                else f"count={entry.count}"
            )
            print_info(f"[profile +1, {tag}] {entry.text}")
            return

        if len(entries) >= MAX_PROFILE_ENTRIES:
            victim = next(
                (i for i, e in enumerate(entries) if e.count == 1),
                0,
            )
            entries.pop(victim)
        entries.append(ProfileEntry(note))
        print_info(
            f"[profile new candidate, count=1, hidden until "
            f"count>={PROFILE_MIN_COUNT_TO_SURFACE}] {note}"
        )

    def all_actors(self):
        """Iterable of all LLM-backed actors. Useful for stats and reset."""
        return [
            a for a in (self.answerer, self.critic, self.judge)
            if a is not None
        ]

    async def clear_session(self):
        self.prior_rephrased.clear()
        self.profile_entries.clear()
        self.stats.reset()
        for actor in self.all_actors():
            await actor.reset_memory()
        # Critic and Judge are rebuilt per round; drop them.
        if self.critic:
            self.critic.invalidate()
        if self.judge:
            self.judge.invalidate()