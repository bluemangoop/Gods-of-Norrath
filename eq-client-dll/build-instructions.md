# Build Instructions - GodsOfNorrath Hook DLL

## Versioning

- **Current version:** `v0.0.2` (defined as `DLL_VERSION` in `godsofnorrath.cpp`)
- **Bump rule:** Increment the **patch** version by 0.0.1 on every build unless explicitly told otherwise.
  - Example: `v0.0.1` → `v0.0.2` → `v0.0.3`
  - Major/minor bumps (`v1.0.0`, `v0.1.0`) only when explicitly ordered.

## Build Steps

### 1. Bump the version

Edit `godsofnorrath.cpp` and update the `DLL_VERSION` define:

```cpp
#define DLL_VERSION "v0.0.2"  // bump from v0.0.1
```

### 2. Build the DLL

```bash
cd /home/EQemupatcher-test/eq-client-dll
make godsofnorrath.dll
```

### 3. Deploy to web directory

```bash
cp godsofnorrath.dll /var/www/html/patch/rof/godsofnorrath.dll
```

### 4. Verify

Check the file was copied:

```bash
ls -la /var/www/html/patch/rof/godsofnorrath.dll
```

## Quick One-Liner (build + deploy)

```bash
cd /home/EQemupatcher-test/eq-client-dll && make godsofnorrath.dll && cp godsofnorrath.dll /var/www/html/patch/rof/godsofnorrath.dll
```

## Notes

- The DLL is a **32-bit** build (targeting `i686-w64-mingw32`) because `eqgame.exe` is a 32-bit process.
- The version string appears in a message box when the DLL loads, so players can confirm they have the right version.
- The debug log (`eqhook_debug.log`) is written to the EQ client's working directory.
