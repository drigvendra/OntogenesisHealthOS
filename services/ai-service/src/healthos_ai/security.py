"""Verification of the signed request context issued by core-api.

The browser never calls this service. core-api authenticates the user, checks entitlements,
permissions and budget, then mints a short-lived HS256 token describing who is asking and which
AI capabilities are allowed. Requests without a valid context are rejected.
"""

from dataclasses import dataclass
from typing import Annotated

import jwt
from fastapi import Depends, Header, HTTPException, status

from healthos_ai.config import Settings, get_settings


@dataclass(frozen=True)
class RequestContext:
    tenant_id: str
    user_id: str
    capabilities: frozenset[str]
    correlation_id: str | None


def verify_context_token(token: str, settings: Settings) -> RequestContext:
    try:
        claims = jwt.decode(
            token,
            settings.context_signing_key.get_secret_value(),
            algorithms=["HS256"],
            audience=settings.context_audience,
            issuer=settings.context_issuer,
            options={"require": ["exp", "iat", "iss", "aud", "sub", "tid"]},
        )
    except jwt.PyJWTError as exc:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "invalid request context") from exc

    lifetime = int(claims["exp"]) - int(claims["iat"])
    if lifetime > settings.context_max_lifetime_seconds:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "request context lifetime too long")

    scopes = claims.get("scp", [])
    if not isinstance(scopes, list) or not all(isinstance(s, str) for s in scopes):
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "invalid request context")

    return RequestContext(
        tenant_id=str(claims["tid"]),
        user_id=str(claims["sub"]),
        capabilities=frozenset(scopes),
        correlation_id=claims.get("cid"),
    )


def require_context(
    settings: Annotated[Settings, Depends(get_settings)],
    x_healthos_context: Annotated[str | None, Header()] = None,
) -> RequestContext:
    if not x_healthos_context:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "missing request context")
    return verify_context_token(x_healthos_context, settings)
