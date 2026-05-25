"""
Actor — base class for any participant whose system prompt may include
template placeholders rendered from DebateContext.

Subclasses provide:
  - a role name (used to locate the persona file and the provider's model)
  - a temperature() method
  - optionally a render_system_prompt() method (default loads the persona
    file with no placeholder substitution)

The base class handles:
  - building the underlying AutoGen AssistantAgent
  - invalidating/rebuilding when template inputs change
  - resetting model context (for `!new`)
  - returning the agent for the pipeline (team or out-of-band `send`)
"""

from autogen_agentchat.agents import AssistantAgent
from autogen_agentchat.messages import TextMessage
from autogen_core import CancellationToken

from ..personas import load_persona


class Actor:
    """Base class for LLM-backed actors."""

    # Subclasses override these:
    role = ""             # used for persona file lookup and provider lookup
    display_name = ""     # used as agent.name and in printing

    def __init__(self, ctx):
        self.ctx = ctx
        self._agent = None

    def temperature(self):
        """Subclasses pull from ctx.config."""
        raise NotImplementedError

    def render_system_prompt(self):
        """
        Default: load the persona file as-is (no placeholder substitution).
        Subclasses with templates override this.
        """
        return load_persona(self.ctx.config.persona_name, self.role)

    def _make_client(self):
        return self.ctx.provider.make_client(self.role, self.temperature())

    def agent(self):
        """Return the underlying AssistantAgent, building it if necessary."""
        if self._agent is None:
            self._agent = AssistantAgent(
                name=self.display_name,
                model_client=self._make_client(),
                system_message=self.render_system_prompt(),
            )
        return self._agent

    def invalidate(self):
        """Force a rebuild on next agent() call. Used for per-round actors."""
        self._agent = None

    async def reset_memory(self):
        """Wipe conversation buffer (called on `!new`). Safe even if not built."""
        if self._agent is not None:
            await self._agent.on_reset(CancellationToken())

    async def send(self, user_text):
        """
        Send a single user-sourced message to this actor's agent (outside any
        team chat) and return its stripped reply. Used by the pipeline to
        drive out-of-band phases like rephrase and profile-note extraction.
        """
        msg = TextMessage(content=user_text, source="user")
        response = await self.agent().on_messages(
            [msg], cancellation_token=CancellationToken()
        )
        return response.chat_message.content.strip()