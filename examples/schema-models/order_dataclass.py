# Python 3.10+. Annotations describe values; they do not validate them.
from dataclasses import dataclass
from datetime import date, time, datetime
from decimal import Decimal
from enum import Enum

class Unset(Enum):
    TOKEN = 0

UNSET = Unset.TOKEN

@dataclass(kw_only=True)
class Order:
    id: int
    label: str | None
    note: str | None | Unset = UNSET
