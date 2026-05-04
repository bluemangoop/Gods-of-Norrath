# GodsOfNorrath DLL - Chat Command Architecture Notes

## Overview of EQ Client Chat Command Processing

The EQ client processes chat input through a well-defined pipeline. Understanding this is critical to making `#` commands work.

## Key Structures

### CMDLIST (EQData.h line 322-331)
This is the client-side command registration table. Every slash command (`/who`, `/camp`, `/mapfilter`, etc.) is registered here:

```cpp
struct CMDLIST {
    DWORD LocalizedStringID;  // String table ID for the command name
    char* szName;             // Command name (e.g., "who", "camp", "mapfilter")
    char* szLocalized;        // Localized version of the name
    void  (*fAddress)(PlayerClient*, const char*);  // Function pointer to handler
    DWORD Restriction;        // Access restriction flags
    DWORD Category;           // Command category
    DWORD Flags;              // Misc flags
};
```

### Command List Pointer
- `EQADDR_CMDLIST` (Globals.h line 757) - points to the global command list array
- `__CommandList_x` at offset `0xACD5A8` (eqgame.h line 83) - the static address in eqgame.exe

### Key Functions

#### CEverQuest::InterpretCmd (offset 0x51FCE0)
- **Signature:** `void InterpretCmd(PlayerClient*, const char* cmdLine)`
- This is the function that parses and dispatches slash commands
- It's called when the user presses Enter in chat mode
- It iterates through the CMDLIST to find a matching command name
- **This is what your DLL currently hooks** (InstallCmdHook at 0x0091fce0)

#### CEverQuest::dsp_chat (offset 0x51F1A0)
- **Signature:** `void dsp_chat(const char* message, int color, bool allowLog, bool doPercentConversion)`
- Displays text in the chat window
- Used for outputting messages to the player

#### CEverQuest::clr_chat_input
- Clears the chat input buffer after a command is processed

#### EQExecuteCmd (offset 0x4D7230)
- **Signature:** `bool EQExecuteCmd(unsigned int command, bool keyDown, void* data, const KeyCombo* combo)`
- Handles keybindings and executes mapped commands
- Not directly relevant to chat input

## How Slash Commands Work (The `/` Path)

1. User types `/command args` in chat and presses Enter
2. The chat window's WndNotification handler detects the Enter key
3. It calls `CEverQuest::InterpretCmd(pLocalPlayer, "/command args")`
4. `InterpretCmd` parses the string, extracts the command name (everything after `/` up to first space)
5. It walks the CMDLIST array looking for a matching `szName`
6. If found, it calls `cmdlists[i].fAddress(pLocalPlayer, args)`
7. If not found, it sends the text to the server as a chat message

## How `#` Commands Work (The Problem)

### In Live EQ / MacroQuest
- The `#` prefix is NOT a standard EQ client feature
- MacroQuest intercepts chat input BEFORE it reaches InterpretCmd
- MQ hooks the chat input processing at a lower level (the edit box / WndNotification level)
- When MQ sees a `#` command, it handles it internally and prevents it from reaching InterpretCmd
- MQ registers its own commands in a separate command table

### In Your Current DLL
- You hook `InterpretCmd` (offset 0x51FCE0)
- This means you only see commands AFTER the EQ client has already parsed them
- The EQ client's `InterpretCmd` function expects commands to start with `/`
- When you type `#helloworld`, the EQ client doesn't recognize `#` as a command prefix
- It treats `#helloworld` as regular chat text and sends it to the server
- Your hook never sees it because InterpretCmd is never called for `#` prefixed text

## The Two Approaches

### Option A: Hook at the Chat Input Level (Before InterpretCmd)
Hook the chat window's WndNotification or the edit box's Enter key handler. This intercepts ALL chat input before the EQ client processes it. You can then:
1. Check if the text starts with `#`
2. If yes, handle it yourself and prevent it from reaching InterpretCmd
3. If no, let it pass through normally

**Challenge:** Requires finding the right UI hook point. The chat input is handled through `CChatWindow::WndNotification` or the edit box's key handler.

### Option B: Hook InterpretCmd and Handle `#` There
Hook `InterpretCmd` as you do now, but also intercept the text BEFORE it reaches the original function. When you see text starting with `#`, handle it yourself and DON'T call the original InterpretCmd.

**Challenge:** The EQ client's chat input processing may not call InterpretCmd at all for `#` prefixed text - it may send it directly to the server as chat. You need to verify this.

### Option C: Hook the Chat Send Function (Recommended)
Hook the function that actually sends chat text to the server. This is the most reliable approach because:
1. ALL chat text (whether `/` command or regular speech) goes through this path
2. You can intercept `#` commands before they're sent to the server
3. You can display the result locally using `dsp_chat`

**The chat send function** is likely `CEverQuest::ChatServerMessage` or a similar function that sends text to the server. When the EQ client doesn't recognize a command, it sends the text as a chat message.

## How MacroQuest Does It

MacroQuest hooks at multiple levels:
1. **Chat Window WndNotification** - intercepts Enter key presses in chat
2. **InterpretCmd** - intercepts slash command processing
3. **dsp_chat** - intercepts chat display for filtering

MQ's chat command system:
- MQ maintains its own command list separate from EQ's CMDLIST
- MQ commands use `/` prefix (same as EQ) but MQ intercepts first
- MQ also supports `/bct`, `/bc`, etc. which are MQ-specific
- MQ does NOT use `#` prefix - that's a custom convention

## Summary of Findings

| Aspect | EQ Client | MacroQuest | Your DLL |
|--------|-----------|------------|----------|
| Command prefix | `/` only | `/` (intercepted) | `/` only (hooked) |
| `#` prefix support | None | None natively | Not working |
| Hook point | N/A | Multiple levels | InterpretCmd only |
| Command table | CMDLIST array | MQ's own list | None yet |

## Recommended Approach

To support both `/` and `#` prefixed commands:

1. **Keep your InterpretCmd hook** for `/` commands (already working)
2. **Add a hook on the chat send function** to intercept `#` commands before they go to the server
3. In the chat send hook, check if the text starts with `#`
4. If yes, parse the command, handle it, call `dsp_chat` to show output, and BLOCK the send
5. If no, let it pass through to the original function

The chat send function to hook would be the function that sends text to the server when InterpretCmd doesn't find a matching command. This is likely in the `CEverQuest` class or the chat window's notification handler.

## Key Offsets (RoF May 10 2013 client)

| Symbol | Offset | Description |
|--------|--------|-------------|
| `CEverQuest__InterpretCmd` | 0x51FCE0 | Command dispatcher (currently hooked) |
| `CEverQuest__dsp_chat` | 0x51F1A0 | Chat display function |
| `CEverQuest__clr_chat_input` | (needs offset) | Clear chat input |
| `__CommandList` | 0xACD5A8 | Static address of CMDLIST pointer |
| `__ExecuteCmd` | 0x4D7230 | Keybinding executor |
| `CEverQuest__ChatServerMessage` | (needs offset) | Sends chat to server |
| `CDBStr__GetString` | 0x4866C0 | DB string lookup (already hooked) |
