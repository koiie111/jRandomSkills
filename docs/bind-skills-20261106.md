# 1.2.4.b4-fix35.2

Adds public !bind/css_bind help and one manual-skill hint per connection. Players enter bind i css_useskill themselves.

Ports Adrenaline, GlassCannon and Partner from Juzlus/jRandomSkills (ByDexterTR, PR #67; upstream d9536283), adapted to this fork without Panorama. Existing enum IDs, gamedata and settings retained. Adrenaline cleans up transfers by connection handle; Partner supports reopening the teammate menu with the skill key.

Validation: Release net10.0 build; existing translations preserved; enum prefix preserved; JSON parsed for all eight languages. Two pre-existing nullable warnings in C4Camouflage/TrueArmor. Real-client key activation, colors and gameplay need in-game acceptance.

## Deployment

Source commit: cfcedc57738c214e6a1e8d761124639beef1c7e7. Both public servers #33 (vds9950x) and #35 (panel) run 1.2.4.b4-fix35.2. DLL SHA256: d9908bc260e51f6d79d479155c37c7a86911f36c583aaa3d41e404cb827622c9.

One scoped Wings stop/start per game; original #33 hosting and other games untouched. Both servers passed external A2S identity, secure Steam registration, RCON status and loaded-module checks. All 155 pre-existing skill records retained exactly; three new active records added. Config, gamedata and player language preferences have identical hashes. Existing translations retained with selected additions; Russian text also added to the production English files which already contain Russian.

Advertisement now has a separate 300-second hint group; original groups retained. Advertisement reload command completed. No related jRandomSkills/Advertisement errors found in current-day logs after deployment. Backups outside autoload: vds9950x /root/jrs-bind-20261106/backup-33-1793947801; panel /root/jrs-bind-20261106/backup-35-1793947814.

Client-only scenarios (colors, real key activation, once-per-connection delivery, teammate selection and actual combat balance) were not exercised by an actual player; server startup and static checks do not establish those results. Private deployment evidence is under C:/Users/koiie/.codex/tasks/jrs-bind-20261106/.
