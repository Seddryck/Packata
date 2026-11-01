# Python 3.10+. Annotations describe values; they do not validate them.
from datetime import date, time, datetime
from decimal import Decimal
from enum import Enum

class Unset(Enum):
    TOKEN = 0

UNSET = Unset.TOKEN

class Order:
    id: int
    label: str | None
    note: str | None | Unset

    def __init__(self, *, id: int, label: str | None, note: str | None | Unset = UNSET):
        self.id = id
        self.label = label
        self.note = note
