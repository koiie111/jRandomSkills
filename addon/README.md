# jRandomSkills Panorama HUD

Build the plugin for .NET 10 and use CounterStrikeSharp 1.0.375 or newer. Stage the Release DLL and its runtime dependencies from the project build. Apply `onboarding.sql` in the shared MySQL schema and set `HudMySqlConnectionString` in the plugin config. Use a database account with SELECT and INSERT/UPDATE on `jrandomskills_hud_intro`.

Example development configuration:

```json
{
  "HudMySqlConnectionString": "Server=127.0.0.1;Port=33485;Database=jrandomskills_qa;User ID=jrs_qa;Password=<local-test-password>"
}
```

In an isolated Windows CS2 install with Workshop Tools, compile the resources with:

```powershell
./addon/build.ps1 -Cs2Root <isolated-cs2-root> -Addon jrandomskills_qa
```

Copy the result's `panorama` directory into a private search path in the LAN client's copied `gameinfo.gi`. A server-only install cannot supply these panels. Verify delivery on a clean retail client before public rollout. The LAN search path tests only clients with the resources already mounted.

Use `!bind` to reopen key setup, `!skills` to browse abilities, `!summ` to see all players’ skills from the last completed round, and `!skillvote` to open the active vote. From the server console, `css_jrandomskills_hudstats` reports card and toast writes and a separate count of modal menu draws.
