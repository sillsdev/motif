# Connect an assistant

Advanced AI mode lets selected assistants use Motif's project tools. The assistant still cannot Apply a Proposal; a person reviews and Applies it in Motif.

Open **AI assistants** in the window and turn on Advanced AI mode. Motif lists the supported assistants it finds. Check the ones to connect, then choose **Connect selected assistants**.

Changing Advanced AI mode affects new Motif sessions. Restart an assistant session to apply the setting to a session that is already open.

For Claude Desktop, Motif adds `motif mcp` to Claude's MCP configuration. Open the Motif plugin folder, upload `motif-plugin.zip` in **Customize → Plugins**, and restart Claude Desktop. Upload the latest ZIP again after a Motif update to refresh the skills saved to your Claude account.

For Claude Code and Codex, Motif installs the plugin from its local marketplace with the assistant's CLI. Start a new assistant session so it loads the plugin and its tools.

If the bundled skills and server have different versions, Motif shows a warning on this page. Restart Motif after an update so its local marketplace copy is refreshed before connecting again. Claude Desktop keeps uploaded plugins in your account, so Motif cannot check whether that account copy needs an update.
