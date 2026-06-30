"""Command pattern: each user `!command` is a class."""

from .base import Command, CommandRegistry, CommandResult
from .builtins import register_builtins

__all__ = ["Command", "CommandRegistry", "CommandResult", "register_builtins"]