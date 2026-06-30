"""
Interactive chat loop: read a line, dispatch commands, otherwise hand
the line to the DebatePipeline.

This module deliberately knows nothing about phases, actors, or teams —
all of that lives in `debate_pkg/pipeline.py`. To change how a question
is processed, edit the pipeline.
"""

from .commands import CommandRegistry, register_builtins
from .pipeline import DebatePipeline
from .ui import print_error, print_info, prompt_line


async def run_chat_loop(ctx):
    registry = CommandRegistry()
    register_builtins(registry)
    pipeline = DebatePipeline(ctx)

    print_info(
        f"Multi-agent debate ready. Persona: '{ctx.config.persona_name}'."
    )
    print_info("Type !help for commands. Empty input exits.\n")

    while True:
        try:
            line = prompt_line(">>> ")
        except (EOFError, KeyboardInterrupt):
            print()
            break

        if line.strip() == "":
            break

        result = await registry.dispatch(ctx, line.strip())
        if result is not None:
            if result.exit:
                break
            continue

        try:
            await pipeline.run(line)
        except Exception as e:
            print_error(f"during debate: {e!r}")
