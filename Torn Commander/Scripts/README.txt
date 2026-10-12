TORN COMMANDER LOCAL SCRIPTS

Put plain JavaScript .js files in:
%LOCALAPPDATA%\TornCommander\Scripts

Then:
Settings -> Refresh list
check the scripts you want
Save enabled
open a NEW browser tab

Scripts are injected with WebView2 AddScriptToExecuteOnDocumentCreatedAsync.

IMPORTANT
=========
This is NOT a full Greasemonkey/Tampermonkey API implementation.
GM_* APIs are not emulated.

Keep scripts compliant with Torn's scripting rules:
- only operate on pages the user actually loads
- no hidden background crawling
- no automatic game actions
- no CAPTCHA bypass

If you have an existing userscript that requires GM_* APIs, use your normal Edge userscript manager via Torn Commander's "Edge" button, or try loading an unpacked compatible Chromium extension through Settings.
