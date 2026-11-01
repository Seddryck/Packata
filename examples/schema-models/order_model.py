# Python 3.10+, Pydantic 2. Strict Python-value validation; no coercion.
from datetime import date, time, datetime
from decimal import Decimal
from typing import Annotated, Literal
from pydantic import BaseModel, ConfigDict, Field

class Order(BaseModel):
    model_config = ConfigDict(strict=True, populate_by_name=True)
    id: int = Field(alias="id")
    label: str | None = Field(alias="label")
    note: str | None = Field(default=None, alias="note")
