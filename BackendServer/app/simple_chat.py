"""
簡易チャット: ユーザーメッセージ1件を受け取り、助手の返答テキスト1件を返す。
入出力は文字列のみ。グラフの invoke と最終メッセージ抽出を担当する。
"""
from langchain_core.messages import BaseMessage
from langchain_core.messages.utils import convert_to_messages

from app.graph import create_graph
from app.node import chat_model

_graph = create_graph().with_config({"configurable": {"thread_id": "default"}})


def get_reply(message: str) -> str:
    """
    ユーザー発言を受け取り、助手の返答テキストを返す。
    """
    input_state = {"messages": [{"role": "user", "content": message}]}
    result = _graph.invoke(input_state)
    if result is None:
        result = chat_model(input_state)
    messages = convert_to_messages(result.get("messages") or [])
    last_message = next((m for m in reversed(messages) if isinstance(m, BaseMessage)), None)
    return last_message.content if last_message else ""
