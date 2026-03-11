"""
起動設定の取得のみを担当するモジュール。
環境変数 (HOST, PORT) とコマンドライン引数 (--host, --port) から host/port を返す。
"""
import argparse
import os


def get_server_bind():
    """
    サーバー bind 用の (host, port) を返す。
    優先: コマンドライン > 環境変数 > 既定値。
    """
    parser = argparse.ArgumentParser(description="Run DesktopAgent server")
    parser.add_argument(
        "--port",
        type=int,
        default=int(os.getenv("PORT", "8000")),
        help="Port to bind (default: $PORT or 8000)",
    )
    parser.add_argument(
        "--host",
        type=str,
        default=os.getenv("HOST", "127.0.0.1"),
        help="Host to bind (default: $HOST or 127.0.0.1)",
    )
    args = parser.parse_args()
    return args.host, args.port
