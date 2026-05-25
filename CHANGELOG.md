# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-05-18

### Added

- Three-agent debate loop: Answerer, Critic, Judge with structured rounds.
- Judge rephrase (bias-dampening), plain-fact restatement, and Answerer profiling.
- Interactive CLI with `!stats`, `!new`, and session commands.
- Persona system with per-role `.txt` files and named presets.
- Windows _installer_ (`install.ps1`): Ollama setup, model pulls with retry logic, Python venv creation.
