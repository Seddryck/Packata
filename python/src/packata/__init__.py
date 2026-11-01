"""Incremental Packata clients. Use stream context managers when stopping early."""
from .client import PackataError, connect, open

__all__ = ["PackataError", "connect", "open"]
