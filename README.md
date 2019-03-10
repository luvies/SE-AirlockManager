# Space Engineers - Airlock Manager

A script that will manage all airlocks on a given grid.

## Setup

To set up the airlocks, do the following (replace `{number}` with the number of the airlock each block is for):

- Make sure a PB is running the script
- Tag the inner doors with `[Airlock {number} Inner]`
- Tag the outer doors with `[Airlock {number} Outer]`
- Tag the vents with `[Airlock {number} Vent]`
- Wire up buttons with the following commands
  - `inner:{number}`
    - Cycles the airlock to open the inner door
  - `toggle:{number}`
    - Cycles the airlock to open the other door
  - `outer:{number}`
    - Cycles the airlock to open the outer door
