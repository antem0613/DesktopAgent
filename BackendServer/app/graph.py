from langgraph.graph import StateGraph, START, END
from langgraph.checkpoint.memory import MemorySaver

from app.state import State
from app.node import chat_model

def create_graph():
    graph = StateGraph(State)
    graph.add_node("chatbot", chat_model)
    graph.set_entry_point("chatbot")
    graph.add_edge("chatbot", END)
    return graph.compile()