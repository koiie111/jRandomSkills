# 1.2.4.b4-fix35.2

Adds public !bind/css_bind help and one manual-skill hint per connection. Players enter bind i css_useskill themselves.

Ports Adrenaline, GlassCannon and Partner from Juzlus/jRandomSkills (ByDexterTR, PR #67; upstream d9536283), adapted to this fork without Panorama. Existing enum IDs, gamedata and settings retained. Adrenaline cleans up transfers by connection handle; Partner supports reopening the teammate menu with the skill key.

Validation: Release net10.0 build; existing translations preserved; enum prefix preserved; JSON parsed for all eight languages. Two pre-existing nullable warnings in C4Camouflage/TrueArmor. Real-client key activation, colors and gameplay need in-game acceptance.
