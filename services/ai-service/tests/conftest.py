import time
from collections.abc import Iterator
from typing import Any

import jwt
import pytest
from fastapi.testclient import TestClient

from healthos_ai.config import get_settings
from healthos_ai.main import create_app

TEST_KEY = "test-only-signing-key-0123456789abcdef"


@pytest.fixture(autouse=True)
def _env(monkeypatch: pytest.MonkeyPatch) -> Iterator[None]:
    monkeypatch.setenv("HEALTHOS_AI_CONTEXT_SIGNING_KEY", TEST_KEY)
    get_settings.cache_clear()
    yield
    get_settings.cache_clear()


@pytest.fixture
def client() -> TestClient:
    return TestClient(create_app())


def make_token(
    *,
    key: str = TEST_KEY,
    scp: list[str] | None = None,
    lifetime: int = 60,
    **overrides: Any,
) -> str:
    now = int(time.time())
    claims: dict[str, Any] = {
        "iss": "healthos-core-api",
        "aud": "healthos-ai-service",
        "sub": "user-synthetic-1",
        "tid": "tenant-synthetic-a",
        "iat": now,
        "exp": now + lifetime,
        "scp": scp if scp is not None else ["ai.sop-copilot"],
    }
    claims.update(overrides)
    return jwt.encode(claims, key, algorithm="HS256")
