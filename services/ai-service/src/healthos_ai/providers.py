"""Model provider abstraction.

M1 ships only a deterministic fake provider so that tests and local development are reproducible,
free, and never send data anywhere. Its output is clearly labelled as fake.
"""

import hashlib
from dataclasses import dataclass
from typing import Protocol


@dataclass(frozen=True)
class Generation:
    text: str
    model_id: str
    input_tokens: int
    output_tokens: int


class ModelProvider(Protocol):
    name: str

    def generate(self, *, system: str, prompt: str, max_output_tokens: int) -> Generation: ...


class FakeProvider:
    """Deterministic provider for dev/CI. Same input -> same output."""

    name = "fake"
    model_id = "fake-deterministic-1"

    def generate(self, *, system: str, prompt: str, max_output_tokens: int) -> Generation:
        digest = hashlib.sha256(f"{system}\x00{prompt}".encode()).hexdigest()[:12]
        text = f"[FAKE PROVIDER OUTPUT {digest}] This is placeholder text for testing only."
        words = text.split()[:max_output_tokens]
        return Generation(
            text=" ".join(words),
            model_id=self.model_id,
            input_tokens=len((system + " " + prompt).split()),
            output_tokens=len(words),
        )


def build_provider(name: str) -> ModelProvider:
    if name == "fake":
        return FakeProvider()
    raise ValueError(f"unknown or unapproved provider: {name}")
