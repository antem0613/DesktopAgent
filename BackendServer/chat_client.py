"""
チャット API クライアント: 指定 URL にメッセージを POST し、返答テキストを返す。
入出力は URL・文字列のみ。HTTP と JSON の詳細を担当する。
"""
import json
import urllib.request


def post_chat(chat_url: str, message: str) -> str:
    """
    POST /chat に message を送り、レスポンスの message フィールドを返す。
    """
    payload = json.dumps({"message": message}).encode("utf-8")
    req = urllib.request.Request(
        chat_url,
        data=payload,
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(req) as resp:
        body = resp.read().decode("utf-8", errors="replace")
    data = json.loads(body or "{}")
    return data.get("message", "")
