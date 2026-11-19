from functools import lru_cache
from typing import Literal

from pydantic import Field, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Runtime configuration. All values come from environment variables prefixed HEALTHOS_AI_."""

    model_config = SettingsConfigDict(env_prefix="HEALTHOS_AI_", env_file=None, extra="ignore")

    # Shared key used by core-api to sign request contexts. Must be at least 32 bytes.
    context_signing_key: SecretStr = Field(min_length=32)
    context_issuer: str = "healthos-core-api"
    context_audience: str = "healthos-ai-service"
    # Upper bound on accepted token lifetime, independent of the token's own exp claim.
    context_max_lifetime_seconds: int = Field(default=120, ge=10, le=600)

    # Only the deterministic fake provider exists in M1. Real providers are added behind the
    # allow-list policy described in docs/adrs/0005-ai-architecture.md.
    provider: Literal["fake"] = "fake"


@lru_cache
def get_settings() -> Settings:
    # mypy sees pydantic's synthesized __init__ with required fields;
    # the values actually come from the environment.
    return Settings()  # type: ignore[call-arg]
