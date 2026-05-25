"""
ANSI color constants and a small helper for colorized output.

We use 16-color ANSI codes for portability. On Windows, modern terminals
(Windows Terminal, VS Code, PowerShell 7+) handle these natively. On
legacy cmd.exe, colors will appear as literal escape codes — use
Windows Terminal instead, or set the NO_COLOR env var to disable.
"""

import os
import sys

# Disable colors if:
#   - stdout is not a TTY (piped or redirected), or
#   - NO_COLOR is set (https://no-color.org/), or
USE_COLOR = (
    sys.stdout.isatty()
    and "NO_COLOR" not in os.environ
)

RESET = "\033[0m"

# Standard colors
BLACK   = "\033[30m"
RED     = "\033[31m"
GREEN   = "\033[32m"
YELLOW  = "\033[33m"
BLUE    = "\033[34m"   # "dark blue" in our spec
MAGENTA = "\033[35m"   # "dark magenta" / regular magenta
CYAN    = "\033[36m"
WHITE   = "\033[37m"

# Bright variants
BRIGHT_BLACK   = "\033[90m"
BRIGHT_RED     = "\033[91m"
BRIGHT_GREEN   = "\033[92m"
BRIGHT_YELLOW  = "\033[93m"
BRIGHT_BLUE    = "\033[94m"
BRIGHT_MAGENTA = "\033[95m"
BRIGHT_CYAN    = "\033[96m"
BRIGHT_WHITE   = "\033[97m"

BOLD = "\033[1m"

# Semantic aliases (the only constants the rest of the code should use) ----

COLOR_WARNING        = YELLOW
COLOR_ERROR          = RED
COLOR_REPHRASED      = MAGENTA          # "OUR prompt" rewritten by the Judge
COLOR_FINAL_ANSWER   = BRIGHT_WHITE     # Judge's verdict
COLOR_ANSWERER       = BRIGHT_BLUE
COLOR_CRITIC         = BLUE             # "dark blue"
COLOR_JUDGE          = MAGENTA          # "dark magenta" — non-special judge text
COLOR_DEFAULT        = ""               # let terminal pick

def colorize(text, color):
    """Wrap text in a color code if colors are enabled."""
    if not USE_COLOR or not color:
        return text
    return f"{color}{text}{RESET}"

def bold(text):
    """Wrap text in bold if colors are enabled."""
    if not USE_COLOR:
        return text
    return f"{BOLD}{text}{RESET}"