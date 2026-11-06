from typing import Literal, Dict
from pydantic import Field

from open_rigs.core.rig import Rig

from ucl_open_coen_calibrate_valve import __semver__


class UclOpenCoenCalibrateValveRig(Rig):
    version: Literal[__semver__] = __semver__
    ...