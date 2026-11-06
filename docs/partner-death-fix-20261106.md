# Partner death cleanup — 1.2.4.b4-fix35.3

A chosen ally could die and respawn with the old healing bond still attached. PlayerDeath now uses the same bidirectional cleanup as disconnect: remove the deceased player's own bond and every bond targeting that connection handle. Clear affected owners' skill HUD as the bond is removed. The existing one-selection-per-round behavior remains unchanged; respawn alone cannot reconnect the old bond.

Release net10.0 build and git diff --check pass. Two existing nullable warnings in C4Camouflage and TrueArmor remain unrelated. Production bot regression passed on both #33 and #35 after installation.

## Verified production rollout

Source c3d71ac; both public #33 (vds9950x) and #35 (panel) load 1.2.4.b4-fix35.3. DLL SHA256 1be3ea891ddc3b1af1c78113b8d5b18a5381b979571e2a5948929828f98a82f9. Only jRandomSkills.dll replaced. Existing plugin configs, languages, player preferences, gamedata and Advertisement were checked for preservation. One scoped Wings stop/start per game; no other game or host restart.

On each server, real test bot pairs healed40->50 before death. Native death of the chosen partner cleared the bond immediately. After native respawn and repositioning nearby, both bots remained40HP with no bond and no new selection. Owner death and actual round restart also left no bonds. Healing respected100HP and70HP caps. Adrenaline reset after owner/target death, skill removal and round restart. GlassCannon maxHP reset correctly. Damage/hit checks used controlled diagnostic inputs; death and round events were native server events.

Test33 ran empty. A player joined35 after deployment; bot tests first stopped without changing settings, then proceeded only after the owner explicitly authorized testing with a round restart while occupied. All test bots removed, temporary diagnostic module unloaded and moved outside autoload; all9 temporary cvars re-queried equal to originals. Final external A2S identity, Steam secure status and no related new errors verified on both games.

Backups: /root/jrs-partner-fix-20261106/backup-33-1793948739 on vds9950x and /root/jrs-partner-fix-20261106/backup-35-1793948830 on panel. Private evidence: C:/Users/koiie/.codex/tasks/jrs-partner-fix-20261106 (test snapshots, assertions, cleanup and postcheck).
