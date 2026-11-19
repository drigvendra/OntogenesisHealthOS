from fastapi.testclient import TestClient


def test_live(client: TestClient) -> None:
    r = client.get("/health/live")
    assert r.status_code == 200
    assert r.json() == {"status": "ok"}


def test_ready_reports_provider(client: TestClient) -> None:
    r = client.get("/health/ready")
    assert r.status_code == 200
    assert r.json()["provider"] == "fake"


def test_interactive_docs_disabled(client: TestClient) -> None:
    assert client.get("/docs").status_code == 404
