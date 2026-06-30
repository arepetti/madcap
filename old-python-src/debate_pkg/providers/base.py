"""
LLMProvider — the seam that hides which LLM backend is in use.

Subclass this and wire your instance into `debate.py` to switch backends.
Subclasses are the only files in the codebase that should import a
specific chat-completion client (OpenAI, Anthropic, ...) or care about
process management for a local server.
"""


class LLMProvider:
    """Interface every backend implements."""

    def bootstrap(self):
        """
        Pre-flight: make sure the backend is reachable, models are present,
        credentials are valid. May print informational output and prompt the
        user. Return True to proceed with startup, False to abort.

        Default: no-op for backends that need no preparation.
        """
        return True

    def make_client(self, role, temperature):
        """
        Return an autogen ChatCompletionClient configured for the given role
        (one of "answerer", "critic", "judge") at the given temperature.
        """
        raise NotImplementedError

    def model_for(self, role):
        """Pretty model name shown in !personas and !stats."""
        raise NotImplementedError

    def effective_context_size(self):
        """
        Context window in tokens, used by !stats to compute fill percentages.
        For backends where this is per-model, pick the smallest in play (or
        the active role's, if you want stricter accounting).
        """
        raise NotImplementedError
