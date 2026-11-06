# Import core types
from typing import Literal
from pydantic import Field

from swc.aeon.io import reader
from swc.aeon.schema import BaseSchema, data_reader

from ucl_open_coen_calibrate_valve import __semver__

# TODO - should inherit from some TaskParameters base class rather than BaseSchema
class UclOpenCoenCalibrateValveTaskParameters(BaseSchema):
    ...


class UclOpenCoenCalibrateValveTaskLogic(BaseSchema):
    version: Literal[__semver__] = __semver__
    name: Literal["UclOpenCoenCalibrateValve"] = Field(default="UclOpenCoenCalibrateValve", description="Name of the task logic", frozen=True)
    task_parameters: UclOpenCoenCalibrateValveTaskParameters = Field(description="Parameters of the task logic")
    ...