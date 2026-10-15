# Linux startup crash fix (1.2.4.b4-fix35.1)

CS2 updates invalidated the Linux CBaseTrigger_StartTouch signature. Installing the native hook failed with `Invalid function pointer`, triggering plugin unload and potentially a process abort from a garbage-collected callback.

This fork replaces native StartTouch/EndTouch detours with BasePlugin-managed OnStartTouch/OnEndTouch entity output subscriptions. Callbacks validate the activator and trigger before dispatching to the existing skill handlers. It also explicitly unhooks user message 207 on unload. No skills are disabled by this patch.

Validation: Release net10.0 build; full CS2 startup; workshop map load; RCON plugin status; plugin unload/reload; round restart. Real-player gameplay and every trigger-based ability have not been exhaustively tested. Entity outputs may differ from native touch hooks for trigger types that do not emit those outputs.

Source changes are in src/player/PlayerEvents.cs, src/player/EntityEvents.cs and src/jRandomSkills.cs. The ready-to-install DLL is included in jRandomSkills - Server Files/plugins/jRandomSkills/. Keep your existing configs and dependencies when installing this update.
