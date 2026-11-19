import time

import pytest
from fastapi.testclient import TestClient

from tests.conftest import make_token

BODY = {
    "capability": "ai.sop-copilot",
    "prompt_id": "sop.draft",
    "prompt_version": "1",
    "input_text": "Draft an SOP for cleaning a synthetic test device.",
}


def post(client: TestClient, token: str | None) -> int:
    headers = {"X-HealthOS-Context": token} if token else {}
    return client.post("/v1/generate", json=BODY, headers=headers).status_code


def test_valid_context_returns_draft(client: TestClient) -> None:
    r = client.post("/v1/generate", json=BODY, headers={"X-HealthOS-Context": make_token()})
    assert r.status_code == 200
    data = r.json()
    assert data["status"] == "draft"
    assert data["provider"] == "fake"
    assert data["text"].startswith("[FAKE PROVIDER OUTPUT")


def test_fake_provider_is_deterministic(client: TestClient) -> None:
    h = {"X-HealthOS-Context": make_token()}
    a = client.post("/v1/generate", json=BODY, headers=h).json()["text"]
    b = client.post("/v1/generate", json=BODY, headers=h).json()["text"]
    assert a == b


def test_missing_context_rejected(client: TestClient) -> None:
    assert post(client, None) == 401


@pytest.mark.parametrize(
    "token",
    [
        make_token(key="wrong-key-wrong-key-wrong-key-wrong"),
        make_token(aud="someone-else"),
        make_token(iss="attacker"),
        make_token(iat=int(time.time()) - 600, exp=int(time.time()) - 300),  # expired
        make_token(lifetime=3600),  # lifetime longer than allowed maximum
        "not-a-jwt",
    ],
    ids=["bad-signature", "bad-audience", "bad-issuer", "expired", "too-long-lived", "garbage"],
)
def test_invalid_context_rejected(client: TestClient, token: str) -> None:
    assert post(client, token) == 401


def test_missing_tenant_claim_rejected(client: TestClient) -> None:
    import jwt as pyjwt

    from tests.conftest import TEST_KEY

    now = int(time.time())
    token = pyjwt.encode(
        {"iss": "healthos-core-api", "aud": "healthos-ai-service", "sub": "u", "iat": now, "exp": now + 60},
        TEST_KEY,
        algorithm="HS256",
    )
    assert post(client, token) == 401


def test_capability_not_in_context_forbidden(client: TestClient) -> None:
    assert post(client, make_token(scp=["ai.knowledge-copilot"])) == 403


def test_unsigned_alg_none_rejected(client: TestClient) -> None:
    import base64
    import json

    def b64(d: dict[str, object]) -> str:
        return base64.urlsafe_b64encode(json.dumps(d).encode()).rstrip(b"=").decode()

    now = int(time.time())
    payload = {
        "iss": "healthos-core-api", "aud": "healthos-ai-service", "sub": "u", "tid": "t",
        "iat": now, "exp": now + 60, "scp": ["ai.sop-copilot"],
    }
    token = f"{b64({'alg': 'none', 'typ': 'JWT'})}.{b64(payload)}."
    assert post(client, token) == 401
