"""
Critic — rebuilt fresh each round, primed via system prompt with:
  - prior rephrased questions
  - active Answerer profile (notes seen >= threshold)
"""

from .base import Actor
from ..personas import (
    load_persona,
    render_prior_rephrased,
    render_answerer_profile,
)


class Critic(Actor):
    role = "critic"
    display_name = "Critic"

    def temperature(self):
        return self.ctx.config.critic_temp

    def render_system_prompt(self):
        template = load_persona(self.ctx.config.persona_name, self.role)
        return (
            template
            .replace("{prior_rephrased}", render_prior_rephrased(self.ctx.prior_rephrased))
            .replace("{answerer_profile}", render_answerer_profile(self.ctx.active_profile()))
        )