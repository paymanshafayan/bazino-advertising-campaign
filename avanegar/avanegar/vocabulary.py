"""Optional Persian spelling hints; never substitute words in recognized text."""
import re
import unicodedata

DEFAULT_VOCABULARY = "آوانگار"
MAX_VOCABULARY_LENGTH = 300


def normalize_vocabulary(value: object) -> str:
    if not isinstance(value, str):
        return DEFAULT_VOCABULARY
    result = []
    for part in re.split(r"[,،;\n\r]+", value[:MAX_VOCABULARY_LENGTH]):
        # Retain Persian half-spaces, but strip directional/control characters.
        word = "".join(char for char in part if char == "\u200c" or not unicodedata.category(char).startswith("C"))
        word = " ".join(word.split())
        if word and word not in result:
            result.append(word)
    return "، ".join(result)[:MAX_VOCABULARY_LENGTH]
