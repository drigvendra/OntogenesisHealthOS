from typing import Annotated, Literal

from fastapi import Depends, FastAPI, HTTPException, status
from pydantic import BaseModel, Field

from healthos_ai import __version__
from healthos_ai.config import Settings, get_settings
from healthos_ai.providers import build_provider
from healthos_ai.security import RequestContext, require_context


class GenerateRequest(BaseModel):
    capability: str = Field(pattern=r"^ai\.[a-z0-9-]+$")
    prompt_id: str = Field(min_length=1, max_length=200)
    prompt_version: str = Field(min_length=1, max_length=50)
    input_text: str = Field(min_length=1, max_length=20_000)
    max_output_tokens: int = Field(default=512, ge=1, le=4096)


class DraftResponse(BaseModel):
    """AI output is always a draft. Only core-api domain modules, after human approval,
    can turn a draft into a final record."""

    status: Literal["draft"] = "draft"
    capability: str
    prompt_id: str
    prompt_version: str
    provider: str
    model_id: str
    text: str
    input_tokens: int
    output_tokens: int


def create_app() -> FastAPI:
    app = FastAPI(
        title="HealthOS AI Service",
        version=__version__,
        docs_url=None,  # internal service; OpenAPI exposed only at /openapi.json for contract tests
        redoc_url=None,
    )

    @app.get("/health/live")
    def live() -> dict[str, str]:
        return {"status": "ok"}

    @app.get("/health/ready")
    def ready(settings: Annotated[Settings, Depends(get_settings)]) -> dict[str, str]:
        try:
            provider = build_provider(settings.provider)
        except ValueError as exc:
            raise HTTPException(status.HTTP_503_SERVICE_UNAVAILABLE, "provider not configured") from exc
        return {"status": "ok", "provider": provider.name}

    @app.post("/v1/generate", response_model=DraftResponse)
    def generate(
        body: GenerateRequest,
        ctx: Annotated[RequestContext, Depends(require_context)],
        settings: Annotated[Settings, Depends(get_settings)],
    ) -> DraftResponse:
        if body.capability not in ctx.capabilities:
            raise HTTPException(status.HTTP_403_FORBIDDEN, "capability not permitted for this context")
        provider = build_provider(settings.provider)
        result = provider.generate(
            system=f"{body.prompt_id}@{body.prompt_version}",
            prompt=body.input_text,
            max_output_tokens=body.max_output_tokens,
        )
        return DraftResponse(
            capability=body.capability,
            prompt_id=body.prompt_id,
            prompt_version=body.prompt_version,
            provider=provider.name,
            model_id=result.model_id,
            text=result.text,
            input_tokens=result.input_tokens,
            output_tokens=result.output_tokens,
        )

    return app


app = create_app()
