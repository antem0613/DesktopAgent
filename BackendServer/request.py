import json
import urllib.request

endpoint = "http://localhost:8000/chat"
payload = json.dumps({"message": "こんにちは"}).encode("utf-8")

req = urllib.request.Request(
	endpoint,
	data=payload,
	headers={"Content-Type": "application/json"},
)

with urllib.request.urlopen(req) as resp:
	body = resp.read().decode("utf-8", errors="replace")

data = json.loads(body or "{}")
print(data.get("message", ""))