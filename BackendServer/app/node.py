from langchain_ollama import ChatOllama
from langchain_core.messages import BaseMessage, SystemMessage
from langchain_core.messages.utils import convert_to_messages
from app.state import State

llm = ChatOllama(model="gemma3:12b", temperature=0.7)

def chat_model(state: State):
    messages = state.get("messages") or []
    messages = convert_to_messages(messages)
    messages = [m for m in messages if isinstance(m, BaseMessage)]
    if not messages:
        messages = [SystemMessage(content="you are a friendly desktoop companion. you must respond in japanese. just output only japanese text. you must not use any emojis.")]
    elif not isinstance(messages[0], SystemMessage):
        messages = [SystemMessage(content="you are a friendly desktoop companion. you must respond in japanese. just output only japanese text. you must not use any emojis.")] + messages

    try:
        response = llm.invoke(messages)
    except ValueError as exc:
        if "No generations found in stream" in str(exc):
            response = llm.invoke(messages)
        else:
            raise
    return {"messages": messages + [response]}