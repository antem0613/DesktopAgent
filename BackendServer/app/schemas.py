"""
API のリクエスト・レスポンス型定義のみを担当する。
"""
from typing import Any, Dict, List

from pydantic import BaseModel, ConfigDict


class ChatInput(BaseModel):
    model_config = ConfigDict()
    messages: List[Dict[str, Any]]


class SimpleChatInput(BaseModel):
    message: str


class SimpleChatResponse(BaseModel):
    message: str
