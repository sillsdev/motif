# Connect an assistant

Advanced AI mode lets selected assistants use Motif's project tools. The assistant still cannot Apply a Proposal; a person reviews and Applies it in Motif.

Open **Settings** at the bottom of the left navigation rail and choose **Assistant Connections** and select **Let Assistants Use Motif's Tools**. Motif lists the supported assistants it finds (Claude Desktop, Claude Code and Codex); if it finds none, Settings says so. Check the ones to connect, then choose **Connect Selected Assistants**.

Changing Advanced AI mode affects new Motif sessions. Restart an assistant session to apply the setting to a session that is already open.

For Claude Desktop, Motif adds `motif mcp` to Claude's MCP configuration. Choose **Open Plugin Folder**, then **Open Claude Plugins**, upload `motif-plugin.zip` in **Customize → Plugins**, and restart Claude Desktop. Upload the latest ZIP again after a Motif update to refresh the skills saved to your Claude account.

For Claude Code and Codex, Motif installs the plugin from its local marketplace with the assistant's CLI. Start a new assistant session so it loads the plugin and its tools.

If the bundled skills and server have different versions, Motif shows a warning in Settings. Restart Motif after an update so its local marketplace copy is refreshed before connecting again. Claude Desktop keeps uploaded plugins in your account, so Motif cannot check whether that account copy needs an update.

Choose **Claude** in the top bar for [Requests and setup](guide:ask-claude). Configuring an assistant does not prove a live Claude connection, and the panel has no embedded chat. ChatGPT connection status belongs to its own tunnel only.
