"""Token counting helper. Falls back to a heuristic if tiktoken is unavailable."""

try:
    import tiktoken
    _ENC = tiktoken.get_encoding("cl100k_base")

    def count_tokens(text):
        return len(_ENC.encode(text))

    TOKEN_METHOD = "tiktoken (cl100k_base, approximate for Llama)"
except Exception:
    def count_tokens(text):
        return max(1, len(text) // 4)

    TOKEN_METHOD = "char/4 heuristic"