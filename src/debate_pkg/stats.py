"""
SessionStats: per-session counters, wall times, and token buckets.

Owned by DebateContext, mutated by DebatePipeline, read by the !stats
command. Cleared by !new (DebateContext.clear_session calls reset()).

Token counting uses the same tiktoken heuristic as the per-actor !stats
table, so the two views are comparable. Each `add_tokens` call accepts
the request payload and the reply text actually transmitted on that
call; the system-prompt and conversation-history overhead the model
also processes on every turn is intentionally not counted here (the
per-actor "context fill" table covers that separately).
"""

from .tokens import count_tokens


class SessionStats:
    """Per-session counters, wall times, and token buckets."""
    __slots__ = (
        "questions",
        "clarifications",
        "debate_rounds",
        "wall_time_total",
        "last_wall_time_total",
        "wall_time_post_rephrase",
        "last_wall_time_post_rephrase",
        "tokens_rephrase",
        "tokens_answerer",
        "tokens_critic",
        "tokens_verdict",
        "tokens_profile",
    )

    def __init__(self):
        self.reset()

    @property
    def tokens_total(self):
        return (
            self.tokens_rephrase
            + self.tokens_answerer
            + self.tokens_critic
            + self.tokens_verdict
            + self.tokens_profile
        )

    def add_tokens(self, category, *texts):
        """Count tokens across `texts` and add to the `tokens_<category>` bucket."""
        n = sum(count_tokens(t) for t in texts if t)
        attr = f"tokens_{category}"
        setattr(self, attr, getattr(self, attr) + n)

    def reset(self):
        self.questions = 0
        self.clarifications = 0
        self.debate_rounds = 0
        self.wall_time_total = 0.0
        self.last_wall_time_total = 0.0
        self.wall_time_post_rephrase = 0.0
        self.last_wall_time_post_rephrase = 0.0
        self.tokens_rephrase = 0
        self.tokens_answerer = 0
        self.tokens_critic = 0
        self.tokens_verdict = 0
        self.tokens_profile = 0
