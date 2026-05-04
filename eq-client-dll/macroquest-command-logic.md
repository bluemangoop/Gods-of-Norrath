# MacroQuest Command Logic Analysis

## How MacroQuest Handles Chat Commands

This document analyzes how MacroQuest (MQ) implements its chat command system, based on MQ's actual source code. The goal is to replicate MQ's approach in our `godsofnorrath.dll` to support custom commands like `/helloworld` and `#helloworld`.

---

## 1. MQ's Actual Hook Points (From Source Code)

After reading MQ's actual source code (`MQCommandAPI.cpp`, `MQ2ChatHook.cpp`, `MQCommands.cpp`), here is what MQ actually hooks:

| Hook | Function | Offset (RoF2) | Purpose |
|------|----------|---------------|---------|
| **Primary** | `CEverQuest::InterpretCmd` | `0x51FCE0` | Command interception — MQ checks its own command list FIRST |
| **Secondary** | `CEverQuest::dsp_chat` | `0x51F1A0` | Chat DISPLAY filtering (timestamps, chat events) — NOT command interception |
| **Tertiary** | `CEverQuest::DoTellWindow` | `0x51DA00` | Tell window display — NOT command interception |

### Key Finding: MQ Does NOT Hook WndNotification

Contrary to what was previously assumed, MQ does **NOT** hook `CChatWindow::WndNotification` for command interception. MQ's command interception is entirely through `InterpretCmd`.

---

## 2. How MQ's Command System Works

### MQCommandAPI (MQCommandAPI.cpp)

MQ maintains its **own command list** separate from EQ's CMDLIST:

1. **Constructor** (`MQCommandAPI::MQCommandAPI`, line 78):
   - Hooks `CEverQuest__InterpretCmd` via `EzDetour`
   - Imports ALL EQ commands from `EQADDR_CMDLIST` (offset `0xACD5A8`) into its own list
   - Registers MQ-specific commands (like `/echo`, `/face`, `/target`, etc.)

2. **InterpretCmd_Detour** (line 57-69):
   ```cpp
   void InterpretCmd_Detour(SPAWNINFO* pChar, const char* szFullLine) {
       auto eqHandler = [this](...){ InterpretCmd_Trampoline(pChar_, szFullLine_); };
       if (pCommandAPI->InterpretCmd(szFullLine, eqHandler)) {
           return; // MQ handled it
       }
       InterpretCmd_Trampoline(pChar, szFullLine); // Fall through to EQ
   }
   ```

3. **DispatchCommand** (line 378-427):
   - Searches MQ's command list using **substring matching** (`_strnicmp`)
   - If found: calls the MQ handler
   - If not found: calls the EQ handler (trampoline)

### MQ2ChatHook (MQ2ChatHook.cpp)

This hooks `dsp_chat` for **display filtering only**:
- Chat color filtering
- Chat event detection (`CheckChatForEvent`)
- Timestamp insertion
- Tell window display

---

## 3. The EQ Client's Command Processing Flow

```
User types text in chat box and presses Enter
        │
        ▼
┌─────────────────────────────────────────┐
│  Chat edit box processes Enter key      │
│  (CChatWindow internal handling)        │
└─────────────────────────────────────────┘
        │
        ▼
┌─────────────────────────────────────────┐
│  Does text start with '/'?              │
│  ├─ YES → Call CEverQuest::InterpretCmd │ ← MQ HOOKS HERE
│  │         (parses /command)            │
│  └─ NO  → Send text to server as chat   │
└─────────────────────────────────────────┘
        │
        ▼
┌─────────────────────────────────────────┐
│  InterpretCmd checks CMDLIST array      │
│  ├─ Found → Execute EQ command          │
│  └─ Not found → Send text to server     │
│                  as chat message        │
└─────────────────────────────────────────┘
```

### Why `#helloworld` Fails

The EQ client's `InterpretCmd` is only called when the text starts with `/`. When you type `#helloworld`:
- The chat edit box processes the Enter key
- It checks if the text starts with `/`
- Since it doesn't, **`InterpretCmd` is never called**
- The text is sent directly to the server as a chat message
- Your `InterpretCmd` hook never fires

### Why `/helloworld` Also Fails (Probably)

When you type `/helloworld`:
1. The chat edit box sees `/` prefix
2. It calls `CEverQuest::InterpretCmd`
3. Your hook fires and checks for `"/helloworld"`
4. If the match works, it should handle it

**Possible reasons `/helloworld` fails:**
- The text received by `InterpretCmd` may have trailing whitespace/newlines
- The text may have STML formatting or color codes prepended
- The hook may not be installed correctly (check `eqhook_debug.log`)
- The offset `0x51FCE0` may be wrong for this specific client build

---

## 4. The Solution: Hook at the Right Level

### For `/` Commands: InterpretCmd Hook (Already Done)

The `InterpretCmd` hook at `0x51FCE0` IS the correct interception point for `/` commands. MQ uses this same approach. The issue is likely:
1. String comparison issues (trailing whitespace, hidden characters)
2. Hook installation issues

### For `#` Commands: Need a Lower-Level Hook

Since `InterpretCmd` is never called for `#` commands, we need to intercept at a lower level. The options are:

**Option A: Hook CChatWindow::WndNotification** (offset `0x656640`)
- Intercepts ALL Windows messages sent to the chat window
- Catches `WM_KEYDOWN` with `VK_RETURN` (Enter key)
- Can read the chat edit box text BEFORE any processing
- This is the cleanest approach

**Option B: Hook the Chat Edit Box's Internal Enter Handler**
- The chat edit box has an internal function that processes Enter
- This is called regardless of whether the text starts with `/`
- More complex to find the right offset

**Option C: Keep the send() Hook as a Fallback**
- The Winsock `send()` hook intercepts ALL outgoing data
- It's a hack but it works for catching `#` commands that get sent to the server
- The problem is it catches ALL network traffic, not just chat

---

## 5. MQ's Command Registration System

MQ registers commands in `MQCommandAPI` constructor (line 134-265):

```cpp
struct { const char* szCommand; MQCommandHandler pFunc; bool Parse; bool InGame; } NewCommands[] = {
    { "/aa",                AltAbility,                 true,  true  },
    { "/alert",             Alert,                      true,  true  },
    { "/alias",             Alias,                      false, false },
    { "/echo",              Echo,                       true,  false },
    { "/face",              Face,                       true,  true  },
    { "/target",            Target,                     true,  true  },
    // ... many more
    { nullptr,              nullptr,                    false, true  },
};
```

Each command has:
- **szCommand**: The command string (e.g., `"/echo"`)
- **pFunc**: The handler function
- **Parse**: Whether to parse macro parameters
- **InGame**: Whether the command requires being in-game

---

## 6. Our Implementation Plan

### Step 1: Fix the InterpretCmd Hook for `/` Commands

The current `HookedInterpretCmd` function checks for exact match `"/helloworld"`. We need to:
1. Strip trailing whitespace/newlines (already done with `TrimRight`)
2. Add better debug logging to see what `InterpretCmd` actually receives
3. Consider that the EQ client may pass the command with or without the `/` prefix

### Step 2: Add CChatWindow::WndNotification Hook for `#` Commands

Hook `CChatWindow::WndNotification` at offset `0x656640` to intercept ALL chat input:
- Catches `WM_KEYDOWN` with `VK_RETURN`
- Reads the chat edit box text
- Checks for `#` commands
- If matched: handles the command and blocks further processing
- If not matched: passes through to original

### Step 3: Keep dsp_chat Hook for Display

Keep the `dsp_chat` function pointer for displaying messages back to the player.

### Step 4: Remove the send() Hook (Optional)

Once WndNotification is working, the `send()` hook becomes redundant for command interception. However, it can be kept as a fallback.

---

## 7. Key Offsets (RoF2 May 10 2013)

| Symbol | Offset | Source |
|--------|--------|--------|
| `CEverQuest__InterpretCmd` | `0x51FCE0` | MQ eqgame.h |
| `CEverQuest__dsp_chat` | `0x51F1A0` | MQ eqgame.h |
| `CEverQuest__DoTellWindow` | `0x51DA00` | MQ eqgame.h |
| `CChatWindow__WndNotification` | `0x656640` | MQ eqgame.h |
| `CDBStr__GetString` | `0x4866C0` | MQ eqgame.h |
| `pinstCEverQuest` | `0xE67CCC` | MQ eqgame.h |
| `pinstLocalPlayer` | `0xDD2630` | MQ eqgame.h |
| `pinstCDBStr` | `0xD1F380` | MQ eqgame.h |
| `__CommandList` | `0xACD5A8` | MQ eqgame.h |

---

## 8. Comparison: Our Current Approach vs MQ's Approach

| Aspect | Our Current DLL | MacroQuest |
|--------|----------------|------------|
| Command interception | InterpretCmd + send() hook | InterpretCmd only |
| Command table | Hardcoded if/else checks | MQ's own command list |
| `#` prefix support | Broken (send hook workaround) | Not used (MQ uses `/`) |
| `/` prefix support | Via InterpretCmd hook | Via InterpretCmd hook |
| Chat display | dsp_chat function pointer | dsp_chat hook |
| Plugin system | None | Full plugin API |
