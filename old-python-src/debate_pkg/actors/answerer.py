"""Answerer — keeps full memory across rounds."""

from .base import Actor


class Answerer(Actor):
    role = "answerer"
    display_name = "Answerer"

    def temperature(self):
        return self.ctx.config.answerer_temp
