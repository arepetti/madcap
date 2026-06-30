"""
Judge — rebuilt fresh each round, primed via system prompt with the list
of prior rephrased questions for cross-round continuity.

The Judge no longer owns any orchestration. The per-question flow
(rephrase, debate, profile note) lives in `debate_pkg/pipeline.py`,
which drives this actor out-of-band via `Actor.send`.
"""

from .base import Actor
from ..personas import load_persona, render_prior_rephrased


class Judge(Actor):
    role = "judge"
    display_name = "Judge"

    def temperature(self):
        return self.ctx.config.judge_temp

    def render_system_prompt(self):
        template = load_persona(self.ctx.config.persona_name, self.role)
        return template.replace(
            "{prior_rephrased}",
            render_prior_rephrased(self.ctx.prior_rephrased),
        )
