"""
LLM backend providers.

Every other module in this codebase addresses LLM concerns through the
LLMProvider interface defined here. To add a new backend (OpenAI, Anthropic,
Azure, vLLM, ...), drop a new subclass in this package and wire it up in
`debate.py`. No other file should need to change.
"""

from .base import LLMProvider
from .ollama import OllamaProvider

__all__ = ["LLMProvider", "OllamaProvider"]
