"""
SystemActor — represents the program itself.

It is NOT part of the chat. It owns the DebateContext and serves as the
owner of session-wide actions that don't belong to any LLM actor:
  - clearing the session (!new)
  - listing personas
  - dumping stats

Treating it as an actor (alongside Answerer/Critic/Judge) gives the
command pattern a uniform target.
"""

class SystemActor:
    role = "system"
    display_name = "System"

    def __init__(self, ctx):
        self.ctx = ctx

    async def clear_session(self):
        await self.ctx.clear_session()