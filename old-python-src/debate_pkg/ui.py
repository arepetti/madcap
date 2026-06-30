"""
All user-facing output and input goes through here.

Centralizing this:
  - Keeps actor code free of print()/input() and color logic.
  - Makes it easy to redirect output (file, log, GUI) later.
  - Ensures consistent formatting for warnings, errors, prompts.
"""

from .colors import (
    bold,
    colorize,
    COLOR_WARNING,
    COLOR_ERROR,
    COLOR_REPHRASED,
    COLOR_FINAL_ANSWER,
    COLOR_ANSWERER,
    COLOR_CRITIC,
    COLOR_JUDGE,
)

def print_info(message):
    """Default-color informational message."""
    print(message)

def print_warning(message):
    print(colorize(f"[warning] {message}", COLOR_WARNING))

def print_error(message):
    print(colorize(f"[error] {message}", COLOR_ERROR))


def print_answerer(content):
    print(bold(colorize("--- Answerer ---", COLOR_ANSWERER)))
    print(colorize(content, COLOR_ANSWERER))
    print()

def print_critic(content):
    print(bold(colorize("--- Critic ---", COLOR_CRITIC)))
    print(colorize(content, COLOR_CRITIC))
    print()

def print_judge_intermediate(content):
    """Judge output that is neither the rephrased question nor the final verdict."""
    print(colorize("--- Judge ---", COLOR_JUDGE))
    print(colorize(content, COLOR_JUDGE))
    print()

def print_rephrased(rephrased_text):
    """The Judge's neutral rewriting of the user's question."""
    print()
    print(bold(colorize("REPHRASED QUESTION:", COLOR_REPHRASED)))
    print(colorize(rephrased_text, COLOR_REPHRASED))
    print()

def print_restatement(content):
    """
    The Judge's neutral restatement of an Answerer turn — the channel-
    constraint text the Critic sees. Same colour family as the initial rephrase
    since it is the same conceptual operation (Judge neutralising text).
    """
    print(bold(colorize("--- Judge restates ---", COLOR_REPHRASED)))
    print(colorize(content, COLOR_REPHRASED))
    print()

def print_final_answer(content):
    """The Judge's verdict (Phase 2 output)."""
    print(bold(colorize("--- Final Answer ---", COLOR_FINAL_ANSWER)))
    print(colorize(content, COLOR_FINAL_ANSWER))
    print()


def print_judge_message(content):
    """
    Classify a Judge message produced during Phase 1 and colour it
    appropriately. If it contains REPHRASED:, the rephrased portion is
    shown in magenta. Otherwise it's intermediate (dark magenta) — this
    covers nudge replies and any chatty CLARIFY: framing.
    """
    if "REPHRASED:" in content:
        rephrased = content.split("REPHRASED:", 1)[1].strip()
        print_rephrased(rephrased)
        return

    print_judge_intermediate(content)


def prompt_line(prompt_text):
    """Read one line of input. Caller handles EOFError/KeyboardInterrupt."""
    return input(prompt_text)

def prompt_temperature(role, default):
    while True:
        raw = prompt_line(f"  {role:<9} temperature [default {default}]: ").strip()
        if raw == "":
            return default
        try:
            val = float(raw)
        except ValueError:
            print_warning("not a number, try again")
            continue
        if not (0.0 <= val <= 2.0):
            print_warning("out of range (0.0-2.0), try again")
            continue
        return val

def prompt_yes_no(question, default):
    suffix = "[Y/n]" if default else "[y/N]"
    while True:
        raw = prompt_line(f"{question} {suffix}: ").strip().lower()
        if raw == "":
            return default
        if raw in ("y", "yes"):
            return True
        if raw in ("n", "no"):
            return False
        print_warning("please answer y or n")

def prompt_string(question, default):
    raw = prompt_line(f"{question} [default '{default}']: ").strip()
    return raw if raw else default