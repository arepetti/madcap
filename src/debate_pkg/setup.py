"""
Interactive setup wizard.

Asks the user for:
  - Persona preset (free text; falls back to 'default' if missing)
  - Per-role temperatures
  - Whether to build an Answerer profile across rounds

Returns a SessionConfig consumed by DebateContext.
"""

from .personas import list_persona_names, resolve_persona_path
from .ui import (
    print_info,
    print_warning,
    prompt_string,
    prompt_temperature,
    prompt_yes_no,
)


# ---- defaults ----
# Cooler Answerer = precise. Hotter Critic = diverse objections. Judge moderate
# so it follows the protocol tokens reliably without being purely greedy.
ANSWERER_TEMP_DEFAULT = 0.3
CRITIC_TEMP_DEFAULT = 0.9
JUDGE_TEMP_DEFAULT = 0.3


class SessionConfig:
    def __init__(self, persona_name, answerer_temp, critic_temp, judge_temp, build_profile):
        self.persona_name = persona_name
        self.answerer_temp = answerer_temp
        self.critic_temp = critic_temp
        self.judge_temp = judge_temp
        self.build_profile = build_profile


def _prompt_persona():
    available = list_persona_names()
    if available:
        print_info("Available personas: " + ", ".join(available))
    else:
        print_warning("No persona files found. Using 'default' (will fail unless created).")

    name = prompt_string("Persona preset", default="default")

    # Sanity check: do *any* of the three role files resolve?
    missing_roles = [
        r for r in ("answerer", "critic", "judge")
        if resolve_persona_path(name, r) is None
    ]
    if missing_roles:
        print_warning(
            f"persona '{name}' is missing files for: {', '.join(missing_roles)} — "
            f"and 'default.<role>.txt' fallbacks are not present either."
        )
    return name


def run_setup_wizard():
    print_info("=== Multi-Agent Debate Setup ===\n")

    persona = _prompt_persona()

    print_info("\nSet per-role temperatures (press Enter for default):")
    a = prompt_temperature("Answerer", ANSWERER_TEMP_DEFAULT)
    c = prompt_temperature("Critic", CRITIC_TEMP_DEFAULT)
    j = prompt_temperature("Judge", JUDGE_TEMP_DEFAULT)
    print_info(f"Using: Answerer={a}, Critic={c}, Judge={j}\n")

    build_profile = prompt_yes_no(
        "Should the Judge build an Answerer profile across rounds?",
        default=True,
    )
    print_info(f"Profile building: {'on' if build_profile else 'off'}\n")

    return SessionConfig(
        persona_name=persona,
        answerer_temp=a,
        critic_temp=c,
        judge_temp=j,
        build_profile=build_profile,
    )