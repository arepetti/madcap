"""
OllamaProvider — local Ollama backend.

Encapsulates everything Ollama-specific:
  - detecting whether the server is already running and starting it if not
  - resolving num_ctx (from OLLAMA_NUM_CTX env, with a documented fallback)
  - verifying required models are installed locally (and offering to proceed
    anyway, since a pull may be running in another terminal)
  - building OpenAI-compatible chat clients pointed at localhost:11434/v1
"""

import json
import os
import platform
import shutil
import subprocess
import time
from urllib.error import URLError
from urllib.request import urlopen

from autogen_ext.models.openai import OpenAIChatCompletionClient

from .base import LLMProvider
from ..ui import print_error, print_info, print_warning, prompt_yes_no


class OllamaProvider(LLMProvider):
    """Local LLM backend via an `ollama serve` process."""

    HOST = "http://localhost:11434"
    OPENAI_BASE = f"{HOST}/v1"

    # Role -> model tag. The only place this mapping should live.
    MODELS = {
        "answerer": "llama3.1:8b",
        "critic":   "qwen2.5:7b",
        "judge":    "llama3.1:8b",
    }

    # Preferred context size when we control the launch.
    PREFERRED_NUM_CTX = 8192
    # Ollama's built-in default if no env override is present.
    OLLAMA_DEFAULT_NUM_CTX = 2048

    # How long to wait for the server to come up after we spawn it.
    STARTUP_TIMEOUT = 30.0

    def __init__(self):
        # Resolved during bootstrap; read by !stats.
        self._resolved_num_ctx = self.PREFERRED_NUM_CTX

    # LLMProvider interface

    def bootstrap(self):
        if not self._ensure_running():
            return False
        if not self._check_models_available():
            try:
                if not prompt_yes_no("Continue anyway?", default=False):
                    return False
            except (KeyboardInterrupt, EOFError):
                print()
                return False
        return True

    def make_client(self, role, temperature):
        return OpenAIChatCompletionClient(
            model=self.MODELS[role],
            base_url=self.OPENAI_BASE,
            api_key="ollama",
            temperature=temperature,
            model_info={
                "vision": False,
                "function_calling": False,
                "json_output": False,
                "family": "unknown",
            },
        )

    def model_for(self, role):
        return self.MODELS[role]

    def effective_context_size(self):
        return self._resolved_num_ctx

    # Server lifecycle

    def _alive(self, timeout=1.0):
        try:
            with urlopen(f"{self.HOST}/api/tags", timeout=timeout) as r:
                return r.status == 200
        except (URLError, OSError):
            return False

    def _ensure_running(self):
        if self._alive():
            self._resolved_num_ctx = self._resolve_already_running()
            return True

        if shutil.which("ollama") is None:
            print_error(
                "'ollama' not found in PATH. Install from https://ollama.com/download"
            )
            return False

        if "OLLAMA_NUM_CTX" not in os.environ:
            os.environ["OLLAMA_NUM_CTX"] = str(self.PREFERRED_NUM_CTX)

        print_info(
            f"Ollama not running — starting 'ollama serve' with "
            f"OLLAMA_NUM_CTX={os.environ['OLLAMA_NUM_CTX']}"
        )
        try:
            kwargs = {
                "stdout": subprocess.DEVNULL,
                "stderr": subprocess.DEVNULL,
                "stdin": subprocess.DEVNULL,
            }
            if platform.system() == "Windows":
                kwargs["creationflags"] = (
                    subprocess.CREATE_NEW_PROCESS_GROUP
                    | getattr(subprocess, "DETACHED_PROCESS", 0)
                )
            else:
                kwargs["start_new_session"] = True
            subprocess.Popen(["ollama", "serve"], **kwargs)
        except Exception as e:
            print_error(f"failed to launch ollama: {e!r}")
            return False

        deadline = time.time() + self.STARTUP_TIMEOUT
        while time.time() < deadline:
            if self._alive():
                print_info("Ollama is up")
                self._resolved_num_ctx = self._resolve_we_started_it()
                return True
            time.sleep(0.5)

        print_error(
            f"ollama did not become ready within {self.STARTUP_TIMEOUT:.0f}s"
        )
        return False

    def _resolve_we_started_it(self):
        val = os.environ.get("OLLAMA_NUM_CTX")
        if val:
            try:
                return int(val)
            except ValueError:
                pass
        return self.PREFERRED_NUM_CTX

    def _resolve_already_running(self):
        val = os.environ.get("OLLAMA_NUM_CTX")
        if val:
            try:
                n = int(val)
                print_info(
                    f"Ollama already running. Using OLLAMA_NUM_CTX={n} "
                    f"from environment for !stats calculations."
                )
                return n
            except ValueError:
                print_warning(
                    f"OLLAMA_NUM_CTX='{val}' is not a valid integer; "
                    f"falling back to Ollama default ({self.OLLAMA_DEFAULT_NUM_CTX})."
                )
                return self.OLLAMA_DEFAULT_NUM_CTX

        print_warning(
            "Ollama is already running and OLLAMA_NUM_CTX is not set in this "
            "environment. Cannot determine the running server's context size. "
            f"!stats percentages will assume the Ollama default of "
            f"{self.OLLAMA_DEFAULT_NUM_CTX} tokens. To use a larger context: "
            f"stop Ollama, set OLLAMA_NUM_CTX={self.PREFERRED_NUM_CTX}, restart it."
        )
        return self.OLLAMA_DEFAULT_NUM_CTX

    def _check_models_available(self):
        try:
            with urlopen(f"{self.HOST}/api/tags", timeout=3.0) as r:
                data = json.loads(r.read().decode("utf-8"))
            local = {m.get("name", "") for m in data.get("models", [])}
        except Exception as e:
            print_warning(f"could not list local models: {e!r}")
            return True

        required = set(self.MODELS.values())
        missing = sorted(m for m in required if m not in local)
        if missing:
            print_warning(
                "models not found locally: " + ", ".join(missing)
                + " — pull with: " + "; ".join(f"ollama pull {m}" for m in missing)
            )
            return False
        return True
