"""Persona file loading and template rendering."""

from pathlib import Path

PERSONA_DIR = Path(__file__).parent.parent / "personas"


def list_persona_names():
    """All persona presets present on disk (anything with an .answerer.txt)."""
    if not PERSONA_DIR.is_dir():
        return []
    names = sorted({
        p.name.split(".")[0]
        for p in PERSONA_DIR.glob("*.answerer.txt")
    })
    return names


def resolve_persona_path(persona_name, role):
    for candidate in (
        PERSONA_DIR / f"{persona_name}.{role}.txt",
        PERSONA_DIR / f"default.{role}.txt",
    ):
        if candidate.is_file():
            return candidate
    return None


def load_persona(persona_name, role):
    path = resolve_persona_path(persona_name, role)
    if path is None:
        raise FileNotFoundError(
            f"No persona file for role '{role}' "
            f"(tried '{persona_name}.{role}.txt' and 'default.{role}.txt')"
        )
    return path.read_text(encoding="utf-8").strip()


def render_prior_rephrased(prior):
    if not prior:
        return "(none yet)"
    return "\n".join(f"{i}. {q}" for i, q in enumerate(prior, 1))


def render_answerer_profile(profile):
    if not profile:
        return "(none yet)"
    return "\n".join(f"- {item}" for item in profile)