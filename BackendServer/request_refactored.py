"""
request.py の複製。chat_client.post_chat を使う版。
元の request.py は変更していない。
"""
from chat_client import post_chat

endpoint = "http://localhost:8000/chat"
print(post_chat(endpoint, "こんにちは"))
