# Implementation Plan: Live DB String Lookup via DLL Injection

## Architecture Overview

Two components working in tandem:

1. **Server-Side (Zone Process Enhancement)**: A lightweight TCP server (`DbStrProxy`) that runs inside the zone process, listening on port 9100. It loads the `db_str` table into an in-memory cache at startup and handles lookup requests from the client DLL.

2. **Client-Side (DLL)**: A Windows DLL (`db_str_proxy.dll`) that is injected into the EQ client process by the eqemupatcher. It connects to the server-side proxy via TCP and hooks `CDBStr::GetString` at offset `0x4866C0` to intercept dbstr lookups.

## Protocol

- Request: `GET_STR|<id>|<type>\n`
- Response: `OK|<value>\n` or `NOT_FOUND|<id>|<type>\n`

## Files Created/Modified

### Server-Side (Zone Process)
- ✅ `akk-stack/akk-stack/code/zone/db_str_proxy.h` - Header for the TCP proxy server
- ✅ `akk-stack/akk-stack/code/zone/db_str_proxy.cpp` - Implementation with in-memory cache and TCP server
- ✅ `akk-stack/akk-stack/code/zone/CMakeLists.txt` - Added db_str_proxy to build
- ✅ `akk-stack/akk-stack/code/zone/main.cpp` - Initializes DbStrProxy on port 9100 after DB connection
- ✅ `akk-stack/docker-compose.yml` - Exposed port 9100
- ✅ `akk-stack/akk-stack/code/common/server_reload_types.h` - Added `DbStr` reload type
- ✅ `akk-stack/akk-stack/code/zone/worldserver.cpp` - Added `DbStr` case to `ProcessReload` to call `db_str_proxy.ReloadCache()`

### Client-Side (DLL)
- ✅ `eq-client-dll/dllmain.cpp` - Windows DLL with:
  - TCP client (`DbStrTcpClient`) connecting to server proxy on port 9100
  - In-memory cache (`DbStrCache`) with thread-safe access
  - **5-byte JMP detour at `CDBStr::GetString` (offset `0x4866C0`)** 
  - Thread-local buffer for returning cached strings
  - Fallback to original function if server proxy is unreachable
  - Proper hook installation/uninstallation with memory protection
- ✅ `eq-client-dll/Makefile` - Build file for MinGW cross-compilation

### Patcher Integration (eqemupatcher)
- ✅ `EQemupatcher-git/EQEmu Patcher/EQEmu Patcher/UtilityLibrary.cs` - Added DLL injection via CreateRemoteThread + LoadLibraryW
- ✅ `EQemupatcher-git/EQEmu Patcher/EQEmu Patcher/MainForm.cs` - Added `db_str_proxy.dll` to SpireExports for auto-download

## How to Build & Deploy

### Server-Side
```bash
# Rebuild the zone binary inside the docker container
docker-compose exec eqemu-server bash
cd /home/eqemu/code/build
cmake .. -DEQEMU_BUILD_LUA=ON -DEQEMU_BUILD_PERL=ON
make -j$(nproc) zone
```

### Client-Side DLL
```bash
# On Linux with MinGW cross-compiler
cd /home/eq-client-dll
make

# Or on Windows with MSVC
cd eq-client-dll
make msvc
```

### Patcher
Build the patcher in Visual Studio as usual. The patcher will:
1. Download `db_str_proxy.dll` from Spire alongside other game files
2. When "Play" is clicked, start `eqgame.exe` and inject the DLL via `CreateRemoteThread` + `LoadLibraryW`
3. The DLL connects to the server's `DbStrProxy` on port 9100 for live lookups

## Usage

### Reloading db_str data without client restart
```bash
# In-game GM command
#reload dbstr
```
This will:
1. Trigger the zone server to reload the `db_str` table from the database
2. The proxy cache is refreshed
3. The client DLL will fetch new strings on next lookup (cache miss)

## EQ Client Offsets (RoF2 - May 10 2013 build)

- `CDBStr::GetString`: `0x4866C0` - The function that looks up db_str entries by id and type
- `pinstCDBStr`: `0xD1F380` - Pointer to the DatabaseStringTable singleton instance

## Next Steps / Future Work
- [ ] Add encryption/authentication to the TCP protocol
- [ ] Add batch lookups to reduce network overhead
- [ ] Extend for dual-classing functionality
