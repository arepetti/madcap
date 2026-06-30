"""Built-in commands: !help, !new, !personas, !stats, and exit-via-empty."""

import csv
import os

from .base import Command, CommandResult
from ..personas import resolve_persona_path
from ..profile import PROFILE_MIN_COUNT_TO_SURFACE
from ..tokens import TOKEN_METHOD, count_tokens
from ..ui import (
    print_error,
    print_info,
    print_warning,
    prompt_line,
    prompt_string,
)


# Helpers

_warned_missing = set()


def _warn_once(key, message):
    if key not in _warned_missing:
        _warned_missing.add(key)
        from ..ui import print_warning
        print_warning(message)


async def _estimate_tokens(actor):
    if actor is None or actor._agent is None:
        return 0
    agent = actor._agent
    total = 0

    if hasattr(agent, "_system_messages") and agent._system_messages:
        for m in agent._system_messages:
            content = getattr(m, "content", None)
            if isinstance(content, str):
                total += count_tokens(content)
    elif not hasattr(agent, "_system_messages"):
        _warn_once(
            "sysmsg_attr",
            "AssistantAgent no longer exposes '_system_messages' — "
            "system prompts will be missing from !stats.",
        )

    if hasattr(agent, "_model_context"):
        ctx = agent._model_context
        if ctx is not None:
            try:
                messages = await ctx.get_messages()
                for m in messages:
                    content = getattr(m, "content", None)
                    if isinstance(content, str):
                        total += count_tokens(content)
                    elif isinstance(content, list):
                        for part in content:
                            if isinstance(part, str):
                                total += count_tokens(part)
            except Exception as e:
                _warn_once(
                    "ctx_call_error",
                    f"_model_context.get_messages() failed: {e!r}",
                )
    else:
        _warn_once(
            "ctx_attr",
            "AssistantAgent no longer exposes '_model_context'.",
        )

    return total


# Commands

class HelpCommand(Command):
    name = "help"
    help = "show this help"

    def __init__(self, registry):
        self._registry = registry

    async def execute(self, ctx, args):
        print_info("\nCommands:")
        for cmd in self._registry.all():
            print_info(f"  !{cmd.name:<11} {cmd.help}")
        print_info("  <empty>      exit")
        print_info("\nAnything else is a question for the debate.\n")
        return CommandResult()


class NewSessionCommand(Command):
    """Start a fresh session: wipe Answerer memory, history, and profile."""
    name = "new"
    help = "start a new session (clear Answerer memory, history, profile)"

    async def execute(self, ctx, args):
        await ctx.system.clear_session()
        print_info("[context cleared]\n")
        return CommandResult()


class PersonasCommand(Command):
    name = "personas"
    help = "show roles, models, temperatures, and persona files"

    async def execute(self, ctx, args):
        from ..personas import PERSONA_DIR
        cfg = ctx.config
        print_info(f"\nLoaded persona preset: '{cfg.persona_name}'")
        print_info(f"Persona directory:     {PERSONA_DIR}")
        print_info(f"Profile building:      {'on' if cfg.build_profile else 'off'}")
        print_info("")
        print_info(f"{'role':<10} {'model':<22} {'temp':>5}  persona file")
        for actor in ctx.all_actors():
            model = ctx.provider.model_for(actor.role)
            temp = actor.temperature()
            path = resolve_persona_path(cfg.persona_name, actor.role)
            print_info(f"{actor.role:<10} {model:<22} {temp:>5.2f}  {path or '(missing)'}")
        print_info("")
        return CommandResult()


class StatsCommand(Command):
    name = "stats"
    help = "show session/per-actor stats; 'export [path]' appends to CSV"

    # Single source of truth for both the CSV header row and the per-row
    # value order in _export. Edit here to add a column.
    EXPORT_COLUMNS = [
        "role",
        "questions",
        "clarifications",
        "debate_rounds",
        "wall_time_total",
        "last_wall_time_total",
        "wall_time_post_rephrase",
        "last_wall_time_post_rephrase",
        "tokens_total",
        "tokens_rephrase",
        "tokens_answerer",
        "tokens_critic",
        "tokens_verdict",
        "tokens_profile",
        "verdict_confidence_low",
        "verdict_confidence_medium",
        "verdict_confidence_high",
    ]

    async def execute(self, ctx, args):
        args = (args or "").strip()
        if args.startswith("export"):
            rest = args[len("export"):].strip()
            return await self._export(ctx, rest)
        return await self._show(ctx)

    async def _show(self, ctx):
        num_ctx = ctx.provider.effective_context_size()
        print_info(f"\nToken estimation: {TOKEN_METHOD}")
        print_info(f"Effective context size: {num_ctx}")
        print_info(f"Profile building: {'on' if ctx.config.build_profile else 'off'}")
        print_info(f"Prior rephrased questions: {len(ctx.prior_rephrased)}")
        active = ctx.active_profile()
        pending = ctx.pending_profile()
        print_info(f"Profile: {len(active)} active, {len(pending)} pending")

        s = ctx.stats
        print_info("\nSession stats:")
        print_info(f"  Questions:               {s.questions}")
        print_info(f"  Clarifications:          {s.clarifications}")
        print_info(f"  Debate rounds:           {s.debate_rounds}")
        print_info("\n  Wall time (seconds):")
        print_info(
            f"    question  -> answer:   "
            f"total {s.wall_time_total:>7.1f}   "
            f"last {s.last_wall_time_total:>7.1f}"
        )
        print_info(
            f"    rephrased -> answer:   "
            f"total {s.wall_time_post_rephrase:>7.1f}   "
            f"last {s.last_wall_time_post_rephrase:>7.1f}"
        )
        print_info("\n  Tokens:")
        token_rows = [
            ("total:", s.tokens_total),
            ("rephrase question:", s.tokens_rephrase),
            ("answerer turns:", s.tokens_answerer),
            ("critic (restate+critic):", s.tokens_critic),
            ("judge verdict:", s.tokens_verdict),
            ("profile (phase 3 + render):", s.tokens_profile),
        ]
        for label, value in token_rows:
            print_info(f"    {label:<29}{value:>8,}")

        print_info("\n  Verdict confidence (count of verdicts at each label):")
        print_info(
            f"    low {s.verdict_confidence_low:>5}   "
            f"medium {s.verdict_confidence_medium:>5}   "
            f"high {s.verdict_confidence_high:>5}"
        )
        print_info("")

        print_info(f"{'agent':<10} {'model':<22} {'tokens':>8} {'budget':>8} {'fill':>8}")
        for actor in ctx.all_actors():
            name = actor.display_name
            model = ctx.provider.model_for(actor.role)
            if actor._agent is None:
                print_info(f"{name:<10} {model:<22} {'—':>8} {num_ctx:>8} {'(unbuilt)':>8}")
                continue
            tokens = await _estimate_tokens(actor)
            pct = 100.0 * tokens / num_ctx
            warn = "  ⚠" if pct > 80 else ""
            print_info(f"{name:<10} {model:<22} {tokens:>8} {num_ctx:>8} {pct:>7.1f}%{warn}")

        if ctx.prior_rephrased:
            print_info("\nRephrased questions so far:")
            for i, q in enumerate(ctx.prior_rephrased, 1):
                preview = q if len(q) <= 80 else q[:77] + "..."
                print_info(f"  {i}. {preview}")

        if active:
            print_info(
                f"\nActive profile (visible to Critic, "
                f"count >= {PROFILE_MIN_COUNT_TO_SURFACE}):"
            )
            for entry in ctx.profile_entries:
                if entry.count >= PROFILE_MIN_COUNT_TO_SURFACE:
                    print_info(f"  [{entry.count}x] {entry.text}")

        if pending:
            print_info("\nPending profile (hidden from Critic, observed only once):")
            for entry in pending:
                print_info(f"  [{entry.count}x] {entry.text}")

        print_info("")
        return CommandResult()

    async def _export(self, ctx, path_arg):
        path = path_arg
        if not path:
            try:
                path = prompt_line("    csv path >>> ").strip()
            except (EOFError, KeyboardInterrupt):
                print()
                return CommandResult()
            if not path:
                print_warning("export cancelled (no path)")
                return CommandResult()

        path = os.path.expanduser(path)

        try:
            role = prompt_string("    role label", ctx.config.persona_name)
        except (EOFError, KeyboardInterrupt):
            print()
            return CommandResult()

        s = ctx.stats
        row = [
            role,
            s.questions,
            s.clarifications,
            s.debate_rounds,
            f"{s.wall_time_total:.1f}",
            f"{s.last_wall_time_total:.1f}",
            f"{s.wall_time_post_rephrase:.1f}",
            f"{s.last_wall_time_post_rephrase:.1f}",
            s.tokens_total,
            s.tokens_rephrase,
            s.tokens_answerer,
            s.tokens_critic,
            s.tokens_verdict,
            s.tokens_profile,
            s.verdict_confidence_low,
            s.verdict_confidence_medium,
            s.verdict_confidence_high,
        ]

        try:
            needs_header = (not os.path.exists(path)) or os.path.getsize(path) == 0
            with open(path, "a", newline="", encoding="utf-8") as f:
                writer = csv.writer(f)
                if needs_header:
                    writer.writerow(self.EXPORT_COLUMNS)
                writer.writerow(row)
        except OSError as e:
            print_error(f"could not write {path}: {e!r}")
            return CommandResult()

        print_info(f"[stats appended to {path} as role='{role}']")
        return CommandResult()


# Registration

def register_builtins(registry):
    cmds = [
        HelpCommand(registry),
        NewSessionCommand(),
        PersonasCommand(),
        StatsCommand(),
    ]
    for c in cmds:
        registry.register(c)