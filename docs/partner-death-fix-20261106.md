# Partner death cleanup — 1.2.4.b4-fix35.3

A chosen ally could die and respawn with the old healing bond still attached. PlayerDeath now uses the same bidirectional cleanup as disconnect: remove the deceased player's own bond and every bond targeting that connection handle. Clear affected owners' skill HUD as the bond is removed. The existing one-selection-per-round behavior remains unchanged; respawn alone cannot reconnect the old bond.

Release net10.0 build and git diff --check pass. Two existing nullable warnings in C4Camouflage and TrueArmor remain unrelated. Production bot regression and rollout results will be recorded after installation on #33 and #35.
