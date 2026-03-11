"""
FastAPI アプリの組み立てのみを担当する。
グラフとルートを登録した app を返す。
"""
from fastapi import FastAPI
from langserve import add_routes

from app.graph import create_graph
from app.simple_chat import get_reply
from app.schemas import ChatInput, SimpleChatInput, SimpleChatResponse


def create_app() -> FastAPI:
    app = FastAPI(
        title="DesktopAgent",
        description="あなたは愛嬌のあるデスクトップコンパニオンです。",
        version="1.0",
    )
    graph = create_graph().with_config({"configurable": {"thread_id": "default"}})
    add_routes(
        app,
        graph,
        path="/agent",
        input_type=ChatInput,
        playground_type="default",
    )
    
    @app.get("/health")
    def health_check():
        return {"status": "ok"}

    @app.post("/chat", response_model=SimpleChatResponse)
    def chat_simple(payload: SimpleChatInput):
        return SimpleChatResponse(message=get_reply(payload.message))

    return app
