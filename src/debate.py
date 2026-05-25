"""
Entry point for the multi-agent debate system.

Overall logic:
  1. Build the LLM provider (default: local Ollama) and let it bootstrap.
     This is the only place the choice of backend is wired in.
  2. Run the interactive setup wizard to gather: persona, temperatures,
     and whether to build an Answerer profile.
  3. Build the DebateContext (shared state bag) and the actors.
  4. Hand off to the chat loop, which dispatches commands and questions.

All heavy logic lives in the debate_pkg/ subpackage. This file only wires
things together.
"""

import asyncio
import sys

from debate_pkg.chat import run_chat_loop
from debate_pkg.context import DebateContext
from debate_pkg.providers import OllamaProvider
from debate_pkg.setup import run_setup_wizard
from debate_pkg.ui import print_error
from debate_pkg.actors.answerer import Answerer
from debate_pkg.actors.critic import Critic
from debate_pkg.actors.judge import Judge
from debate_pkg.actors.system_actor import SystemActor


def main():
    # 1. Provider lifecycle. Swap this line to switch backends.
    provider = OllamaProvider()
    if not provider.bootstrap():
        sys.exit(1)

    # 2. Interactive setup.
    try:
        config = run_setup_wizard()
    except (KeyboardInterrupt, EOFError):
        print()
        sys.exit(0)

    # 3. Build context and actors.
    ctx = DebateContext(config=config, provider=provider)
    ctx.system = SystemActor(ctx)
    ctx.answerer = Answerer(ctx)
    ctx.critic = Critic(ctx)
    ctx.judge = Judge(ctx)

    # 4. Run the chat loop.
    try:
        asyncio.run(run_chat_loop(ctx))
    except KeyboardInterrupt:
        print()


if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        print_error(f"fatal: {e!r}")
        sys.exit(1)
