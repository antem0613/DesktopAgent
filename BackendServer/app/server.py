from app.config import get_server_bind
from app.factory import create_app

app = create_app()

if __name__ == "__main__":
    import uvicorn
    host, port = get_server_bind()
    uvicorn.run(app, host=host, port=port)