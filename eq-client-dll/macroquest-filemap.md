# MacroQuest File Map

This document catalogs every file in the `/tmp/macroquest` directory, documenting the purpose of each file and the information contained within that can serve as examples for our own work.

---

## Top-Level Files

### `.editorconfig`
**Purpose:** Editor configuration file that enforces consistent coding styles across different editors and IDEs. It defines indentation rules per file type.

**Content/Example Value:**
- `root = true` — marks this as the root editorconfig (stops searching parent dirs)
- Default: `indent_style = tab`, `indent_size = 4`
- Python/C#/PowerShell: `indent_style = space`, `indent_size = 4`
- MSBuild props/vcxproj: `indent_style = space`, `indent_size = 2`
- Lua/YAML: `indent_style = space`, `indent_size = 4`

**What we can learn/use:** Standard `.editorconfig` template for a mixed-language C++/C#/Python project. Useful as a reference for our own project's editor configuration.

### `.git-blame-ignore-revs`
**Purpose:** Git blame ignore file — tells `git blame` to skip specific commits that were large-scale reformatting or bulk operations (like file renames/adds), so blame shows the actual author of each line.

**Content/Example Value:**
- Lists 3 commit hashes with a comment: "Ignore eqmule's temper tantrum (all files deleted and re-added)"
- Commits: `d799addf03e440940d85b3935ce58617d55078c1`, `bc7d257a14ef4aac3e6421010eb45e92d105fddf`, `71d894f1d0c1d6cd54942e737afaf1ed08f594e1`

**What we can learn/use:** Useful pattern for our own project — when doing bulk reformatting or restructuring, we can add those commit hashes here so `git blame` remains useful.

### `.gitignore`
**Purpose:** Standard Git ignore file that excludes build artifacts, IDE files, temporary files, and generated code from version control.

**Content/Example Value:**
- Build artifacts: `*.obj`, `*.pdb`, `*.exp`, `*.ilk`, `*.lib`, `*.dll`, `*.exe`
- IDE files: `.vs/`, `.idea/`, `.vscode/`, `.claude/`
- CMake generated: `CMakeCache.txt`, `CMakeFiles/`, `_deps/`, `build/`, `build_*/`
- Generated protobuf: `*.pb.*`
- Template-generated: `/include/config/crashpad.h`
- Custom plugins: `custom_plugins.cmake`, `*.custom`
- Special: `src/tools` (ignores tools repo), `src/MacroQuest*.sln` but NOT `src/MacroQuest.sln`

**What we can learn/use:** Comprehensive `.gitignore` for a C++/CMake project with protobuf, IDE-specific ignores, and template-generated file exclusions. Good reference for our own project.

### `.gitmodules`
**Purpose:** Git submodules configuration — defines external repositories that are pulled in as dependencies.

**Content/Example Value:**
- `src/eqlib` → `https://github.com/macroquest/eqlib.git` — EverQuest library (EQ client interface)
- `contrib/vcpkg` → `https://github.com/microsoft/vcpkg.git` (branch: master) — Microsoft's C++ package manager
- `src/plugins/lua/lua/IntegrationTests` → `https://github.com/macroquest/integration-tests.git` — Lua integration tests

**What we can learn/use:** Shows how to structure submodules for a project: one for the core library (eqlib), one for the package manager (vcpkg), and one for tests. Useful pattern for managing external dependencies.

### `CMakeLists.txt`
**Purpose:** Root CMake build configuration for the entire MacroQuest project. Serves dual purpose: (1) generates Visual Studio solution files, (2) provides a complete build chain via CMake.

**Content/Example Value:**
- Requires CMake 3.30+, Visual Studio generator only (Win32 or x64)
- Sets vcpkg triplet based on architecture: `x64-windows-static` or `x86-windows-static`
- Options: `MQ_BUILD_PLUGINS`, `MQ_BUILD_CUSTOM_PLUGINS`, `MQ_BUILD_LAUNCHER`, `MQ_BUILD_TESTS`, `MQ_STATIC_BUILD`, etc.
- Core subdirs: `src/eqlib`, `src/imgui`, `src/routing`, `src/login`, `src/main`, `contrib/zep`
- Plugin subdirs: `src/plugins/pluginapi`, `src/plugins/autobank`, `src/plugins/lua`, etc.
- C++20 standard, MSVC v143 toolset, Debug/Release configs
- Dependencies: detours, fmt, spdlog
- vcpkg integration with automatic manifest generation per subdirectory
- Custom plugin auto-detection from `plugins/` directory

**What we can learn/use:** Excellent reference for structuring a large C++ CMake project with:
- vcpkg package management integration
- Modular subdirectory organization (core vs plugins vs tests vs loader)
- Custom plugin system with auto-detection
- Dual-purpose build (IDE solution generation + CLI build)
- Architecture-specific configuration

### `CMakePresets.json`
**Purpose:** CMake presets configuration providing pre-configured build profiles for different targets (live servers x64 vs emu servers Win32) and solution regeneration.

**Content/Example Value:**
- Version 9, CMake 3.30 minimum
- Base preset: Visual Studio 17 2022, v143 toolset, binary dir `build/`, VCPKG_ROOT env var
- `live` preset: x64 architecture, `x64-windows-static` vcpkg triplet
- `emu` preset: Win32 architecture, `x86-windows-static` vcpkg triplet
- `live-sln` / `emu-sln` presets: regenerate Visual Studio solution files with `MQ_REGENERATE_SOLUTION=ON`

**What we can learn/use:** Shows how to use CMakePresets.json to manage multiple build configurations (live vs emu) with different architectures and vcpkg triplets. Useful pattern for our project if we need multiple build targets.

### `LICENSE.md`
**Purpose:** GNU General Public License v2 — the license under which MacroQuest is distributed. Standard open-source license text.

**Content/Example Value:**
- Full GPLv2 license text including preamble, terms and conditions, and no warranty clause
- Standard FSF boilerplate for applying the license to new programs

**What we can learn/use:** Standard GPLv2 license file. If we need to choose a license for our project, this is a reference for GPLv2.

### `README.md`
**Purpose:** Project README providing an overview of MacroQuest, build instructions, directory structure, and plugin development guidance.

**Content/Example Value:**
- MacroQuest is "an open source scripting and plugin platform for EverQuest"
- Documentation at docs.macroquest.org
- Build prerequisites: Visual Studio Community, Git for Windows
- Clone + submodule init/update + build via Visual Studio solution
- Directory structure table (build, contrib, data, extras, include, plugins, src, tools)
- Custom plugins go in `plugins/` folder (not `src/plugins/`)
- mkplugin.exe tool for generating new plugins from template

**What we can learn/use:** Good example of a project README structure for a game modding/automation tool. Shows how to document build steps, directory layout, and plugin development workflow.

### `batch-convert.ps1`
**Purpose:** PowerShell script to batch-convert all Visual Studio `.vcxproj` project files to CMake using the conversion tool in `tools/conversions/`.

**Content/Example Value:**
- Maps each `.vcxproj` to a solution folder category (core, core/libraries, core/applications, core/plugins, core/applications/tests)
- Iterates over all projects and calls `Convert-VcxprojToCMake.ps1` with `-GenerateAnalysis -Force` flags
- Projects include: MQ2Main, eqlib, imgui, login, routing, zep, MacroQuest loader, and all plugins (autobank, autologin, bzsrch, chat, chatwnd, custombinds, eqbugfix, hud, itemdisplay, labels, lua, map, pluginapi, targetinfo, xtarinfo) plus tests

**What we can learn/use:** Shows how to automate vcxproj-to-CMake conversion at scale. Useful reference if we need to migrate MSBuild projects to CMake.

### `gen_solution.ps1`
**Purpose:** PowerShell script that automates the full CMake configuration process for MacroQuest. Handles submodule initialization, architecture auto-detection, platform change detection, CMake project generation, and solution file cleanup.

**Content/Example Value:**
- Auto-detects architecture from `MQ_EXPANSION_LEVEL` in `src/eqlib/include/eqlib/BuildType.h` (EXPANSION_LEVEL_ROF → Win32, others → x64)
- Parameters: `-Help`, `-Verbose`, `-Clean`, `-SyncSubmodules`, `-SkipVcpkg`, `-SkipLauncher`, `-SkipCustom`, `-SkipPlugins`, `-BuildTest`, `-AddMQ2MainDependency`, `-CustomPluginsFile`, `-WritePluginsFile`, `-OutputDir`, `-SolutionName`
- Detects platform changes (Win32 ↔ x64) and auto-cleans build directory
- Generates Visual Studio 17 2022 solution with `--fresh` flag
- Cleans the generated solution by removing ALL_BUILD, ZERO_CHECK, CMakePredefinedTargets, and VCXPROJ_DUMMY_ projects
- Output structure: `build/solution/` (CMake files), `build/MacroQuest.sln` (cleaned solution)

**What we can learn/use:** Excellent reference for a CMake configuration automation script. Shows:
- Architecture auto-detection from source code defines
- Platform change detection and automatic clean rebuild
- Solution file post-processing (removing CMake internal projects)
- Comprehensive parameter handling with help documentation
- Submodule management integration

---

## `cmake/` Directory

### `cmake/common.cmake`
**Purpose:** CMake module providing shared utility functions used across the project. Includes precompiled header management, source grouping, and target property manipulation.

**Content/Example Value:**
- `use_pch()` — Configures precompiled headers for source files (sets `/Yu` for using, `/Yc` for creating)
- `source_groups()` — Organizes source files into Visual Studio filter folders based on directory structure, with special handling for external paths
- `target_remove_properties()` — Removes specific values from target properties using regex filtering (escapes special chars, handles generator expressions)
- Convenience wrappers: `target_remove_compile_definitions()`, `target_remove_include_directories()`, `target_remove_compile_options()`, `target_remove_link_directories()`, `target_remove_link_libraries()`, `target_remove_link_options()`

**What we can learn/use:** Shows how to create reusable CMake utility functions. The `target_remove_*` pattern is particularly useful for cleaning up inherited properties in complex builds. The `source_groups()` function is useful for organizing files in Visual Studio.

### `cmake/plugins.cmake`
**Purpose:** CMake module for plugin auto-detection, vcxproj-to-CMake conversion, and custom plugin management. Handles the entire lifecycle of discovering and building custom plugins.

**Content/Example Value:**
- `detect_custom_plugins()` — Discovers plugins either from a custom plugins file (parsing `add_subdirectory` calls) or by globbing directories in `plugins/`
- `build_custom_plugin_directories()` — For each plugin directory: checks for existing CMakeLists.txt, finds vcxproj files, filters by platform compatibility (x64/Win32), converts vcxproj to CMake via PowerShell script, creates master CMakeLists.txt with subdirectory references
- `write_custom_plugins_file()` — Generates a CMake file listing all detected plugins using a template
- Skips ZLib.vcxproj specifically
- Handles force reconversion and platform change scenarios

**What we can learn/use:** Excellent reference for implementing a plugin system in CMake. Shows:
- Auto-detection of plugin directories
- vcxproj-to-CMake conversion automation
- Platform compatibility filtering
- Master CMakeLists.txt generation for multi-project plugin directories

### `cmake/custom_plugins.cmake.in`
**Purpose:** Template file for generating custom plugin configuration. Used by `write_custom_plugins_file()` in plugins.cmake to produce a CMake file listing all detected custom plugins.

**Content/Example Value:**
- Header comment: "Auto-generated plugin configuration file"
- Placeholder `@CUSTOM_PLUGIN_COMMANDS@` that gets replaced with actual plugin add_subdirectory commands

**What we can learn/use:** Simple example of CMake `configure_file()` template usage for generating build configuration files dynamically.

### `cmake/portfile.cmake.in`
**Purpose:** Template for vcpkg portfile — used to build MacroQuest as a vcpkg package. Defines how vcpkg should build and install MacroQuest.

**Content/Example Value:**
- Template with `@VCPKG_TARGET_TRIPLET@` placeholder
- Uses vcpkg_configure_cmake, vcpkg_install_cmake, vcpkg_copy_pdbs, vcpkg_copy_tools patterns
- Installs to `${CURRENT_PACKAGES_DIR}/tools/macroquest`

**What we can learn/use:** Reference for creating a vcpkg portfile for packaging our own project as a vcpkg dependency.

### `cmake/test_vcpkg_merger.cmake`
**Purpose:** Test script for the vcpkg manifest merger functionality. Validates that vcpkg.json files from multiple subdirectories can be correctly merged.

**Content/Example Value:**
- Creates temporary test directories with sample vcpkg.json files
- Tests merging of dependencies from multiple manifests
- Cleans up test artifacts after completion

**What we can learn/use:** Shows how to test CMake script functionality with temporary directories and cleanup.

### `cmake/vcpkg_final.json.in`
**Purpose:** Template for the final merged vcpkg.json manifest. After merging all subdirectory vcpkg.json files, this template produces the consolidated manifest.

**Content/Example Value:**
- Template with `@VCPKG_DEPENDENCIES@` placeholder for merged dependency list
- Built-in dependencies: detours, fmt, spdlog (always included)
- Includes `vcpkg-configuration.json` reference

**What we can learn/use:** Shows how to merge multiple vcpkg manifests into one consolidated file for the build.

### `cmake/vcpkg-configuration.json.in`
**Purpose:** Template for vcpkg configuration — defines overlay ports and registry settings for vcpkg package resolution.

**Content/Example Value:**
- Default-registry pointing to Microsoft's vcpkg registry (nuget)
- Overlay ports path: `${CMAKE_SOURCE_DIR}/contrib/overlay-ports`

**What we can learn/use:** Reference for vcpkg configuration with custom overlay ports.

### `cmake/vcpkg.json.in`
**Purpose:** Template for individual subdirectory vcpkg.json manifests. Each subdirectory that needs dependencies uses this template.

**Content/Example Value:**
- Template with `@VCPKG_DEPENDENCIES@` placeholder
- References `vcpkg-configuration.json` in parent directory

**What we can learn/use:** Shows how to create per-subdirectory vcpkg manifests that get merged into a final consolidated manifest.

### `cmake/vcpkg_manifest.baak.cmake`
**Purpose:** Backup/alternative version of the vcpkg manifest merger. Likely a previous version kept for reference.

**Content/Example Value:**
- Alternative implementation of the vcpkg manifest merging logic
- Similar structure to vcpkg_manifest.cmake

**What we can learn/use:** Reference for versioning CMake scripts and keeping backup implementations.

### `cmake/vcpkg_manifest.cmake`
**Purpose:** Core CMake module for vcpkg manifest management. Handles the merging of vcpkg.json files from multiple subdirectories into a single consolidated manifest.

**Content/Example Value:**
- Scans all subdirectories for vcpkg.json files
- Merges dependencies from all manifests into one
- Generates the final vcpkg.json using `vcpkg_final.json.in` template
- Handles deduplication of dependencies
- Integrates with CMake's `FetchContent` or `find_package` for dependency resolution

**What we can learn/use:** Important reference for managing vcpkg dependencies across a modular project with many subdirectories. Shows how to avoid dependency duplication.

### `cmake/vcxproj_plugins.cmake`
**Purpose:** CMake module for handling legacy vcxproj-based plugins that haven't been converted to CMake yet. Provides compatibility layer for building old-style plugins.

**Content/Example Value:**
- Functions for adding vcxproj-based projects to the CMake build
- Platform filtering (x64/Win32)
- Integration with the vcxproj-to-CMake conversion pipeline

**What we can learn/use:** Shows how to maintain backward compatibility with legacy build systems during a CMake migration.

### `cmake/vcxproj_sln.cmake`
**Purpose:** CMake module for generating Visual Studio solution files from vcxproj projects. Handles the conversion of .vcxproj files to CMake targets.

**Content/Example Value:**
- Functions for parsing vcxproj files and extracting project metadata
- Generates CMake targets that wrap vcxproj builds
- Handles solution folder organization
- Manages project dependencies and build order

**What we can learn/use:** Reference for creating a compatibility layer that allows gradual migration from MSBuild to CMake.

---

## `contrib/` Directory

The `contrib/` directory contains third-party source code and dependencies used by MacroQuest.

### `contrib/args/`
**Purpose:** Third-party C++ command-line argument parsing library (args.hxx). Provides a header-only argument parser for CLI tools.

**Content/Example Value:**
- `args.hxx` — Single header file implementing argument parsing
- `LICENSE` — License file
- `README.md` — Documentation
- `examples/` — Example usage (bash_completion.sh, completion.cxx, gitlike.cxx)

**What we can learn/use:** Reference for a simple header-only C++ library. Useful if we need command-line argument parsing in our tools.

### `contrib/Blech/`
**Purpose:** Third-party C++ header-only library (Blech.h). Likely provides some utility functionality for MacroQuest.

**Content/Example Value:**
- `Blech.h` — Single header file

**What we can learn/use:** Example of a minimal header-only library inclusion.

### `contrib/ClangSharp/`
**Purpose:** ClangSharp DLL and license — used for C++/CLI interop or code analysis. Provides Clang bindings for .NET.

**Content/Example Value:**
- `ClangSharp.dll` — Pre-built binary
- `LICENSE.txt` — License file

**What we can learn/use:** Shows how MacroQuest integrates Clang-based tooling for code analysis or bindings generation.

### `contrib/DIA SDK/`
**Purpose:** Microsoft Debug Interface Access SDK — provides `msdia140.dll` for programmatic access to debug information (PDB files). Used for symbol resolution and debugging features.

**Content/Example Value:**
- `msdia140.dll` — DIA SDK runtime DLL

**What we can learn/use:** Reference for using DIA SDK to access debug symbols programmatically. Useful if we need to resolve function names, line numbers, or stack traces from PDB files.

### `contrib/imgui/`
**Purpose:** Dear ImGui library — a bloat-free graphical user interface library for C++. Used by MacroQuest for in-game UI rendering.

**Content/Example Value:**
- Core: `imgui.cpp`, `imgui.h`, `imgui_demo.cpp`, `imgui_draw.cpp`, `imgui_widgets.cpp`, `imgui_tables.cpp`
- Internal: `imgui_internal.h`
- Stack layout: `imgui_stacklayout.cpp`, `imgui_stacklayout.h`, `imgui_stacklayout_internal.h`
- User config: `imgui_user.h`
- STB integration: `imstb_rectpack.h`, `imstb_textedit.h`, `imstb_truetype.h`
- Config: `imconfig.h`
- Docs: `docs/` (BACKENDS.md, CHANGELOG.txt, CONTRIBUTING.md, EXAMPLES.md, FAQ.md, FONTS.md, README.md, TODO.txt)
- Misc: `misc/` (cpp bindings, debuggers, fonts, freetype, single_file)
- License: `LICENSE.txt`

**What we can learn/use:** Complete Dear ImGui library distribution. Shows how to bundle a third-party GUI library for in-game overlay rendering. The imgui_user.h pattern is useful for customizing ImGui without modifying core files.

### `contrib/mini-yaml/`
**Purpose:** Lightweight YAML parsing library for C++. Provides YAML serialization/deserialization.

**Content/Example Value:**
- `yaml/Yaml.cpp`, `yaml/Yaml.hpp` — Core implementation
- `examples/` — Example usage (FirstExample.cpp, data1.txt)
- `LICENSE`, `README.md`

**What we can learn/use:** Reference for a minimal YAML parser if we need configuration file parsing.

### `contrib/renderdoc/`
**Purpose:** RenderDoc API header — provides the in-application API for RenderDoc GPU debugging integration.

**Content/Example Value:**
- `renderdoc_app.h` — Single header file defining the RenderDoc API

**What we can learn/use:** Shows how to integrate RenderDoc for graphics debugging. The single-header API pattern is useful.

### `contrib/stb/`
**Purpose:** stb_image.h — single-header image loading library by Sean Barrett. Used for loading textures/images.

**Content/Example Value:**
- `stb_image.h` — Single header file for image loading

**What we can learn/use:** Classic single-header C library pattern. Useful if we need image loading capabilities.

### `contrib/tinyfsm/`
**Purpose:** TinyFSM — a minimal finite state machine library for C++. Used for state machine implementations in MacroQuest.

**Content/Example Value:**
- `include/tinyfsm.hpp` — Single header implementation
- `examples/` — API examples and elevator state machine example
- `doc/` — Full documentation (Introduction, Installation, Concepts, Usage, API, Development, License)
- `LICENSE` (COPYING), `README.md`, `ChangeLog`

**What we can learn/use:** Reference for a minimal C++ state machine library. The single-header pattern and documentation structure are good examples.

### `contrib/vcpkg/`
**Purpose:** Microsoft vcpkg — C++ package manager (git submodule). Used to manage external dependencies like detours, fmt, spdlog.

**Content/Example Value:**
- Full vcpkg installation (git submodule)
- Used via CMake toolchain file for dependency management

**What we can learn/use:** Shows how to bundle vcpkg as a submodule for reproducible builds.

### `contrib/vcpkg-overlays/`
**Purpose:** Custom vcpkg overlay ports — provides custom package definitions for dependencies that aren't in the main vcpkg registry or need custom build configurations.

**Content/Example Value:**
- `cmake/detours/` — Custom port for Microsoft Detours library
- `cmake/luajit/` — Custom port for LuaJIT
- `msbuild/` — MSBuild-based ports (mio, protobuf, sleepy-discord, spdlog, yaml-cpp, zeromq)
- `triplets/` — Custom triplet definitions:
  - `x64-windows-static.cmake` — Static linking for x64
  - `x86-windows-static.cmake` — Static linking for x86

**What we can learn/use:** Important reference for creating custom vcpkg ports. Shows how to:
- Create overlay ports for libraries not in main vcpkg
- Define custom triplets for static linking
- Support both CMake and MSBuild-based ports

### `contrib/zep/`
**Purpose:** Zep — a modern C++ code editor widget. Provides an embeddable code editor with syntax highlighting, vim mode, and split panes. Used by MacroQuest for in-game scripting editor.

**Content/Example Value:**
- Core source: `src/` (buffer.cpp, commands.cpp, display.cpp, editor.cpp, filesystem.cpp, etc.)
- Headers: `include/zep.h`, `include/zep/`
- Extensions: `extensions/` (orca, repl)
- Demos: `demos/` (imgui, qt)
- Third-party: `m3rdparty/` (googletest, nod)
- Build: `CMakeLists.txt`, `cmake/`, config scripts
- Tests: `tests/`
- Screenshots: `screenshots/`

**What we can learn/use:** Complex third-party library integration. Shows how to bundle a full-featured code editor widget with:
- Multiple backend support (imgui, Qt)
- Vim mode implementation
- Syntax highlighting system
- Split pane management
- CMake integration with extensions and demos

---

## `data/` Directory

### `data/BinCopy.txt`
**Purpose:** Configuration file listing which binary files should be copied to the build output directory. Used by the build system to deploy runtime dependencies.

**Content/Example Value:**
- List of DLLs, EXEs, and other runtime files to copy alongside MacroQuest

**What we can learn/use:** Shows how to manage runtime file deployment in a build system.

### `data/luarocks.exe` / `data/luarocks32.exe`
**Purpose:** LuaRocks package managers for Lua module installation (64-bit and 32-bit versions). Used to manage Lua dependencies for the Lua scripting plugin.

**Content/Example Value:**
- Pre-built LuaRocks executables bundled with MacroQuest

**What we can learn/use:** Shows how to bundle package management tools for a scripting subsystem.

### `data/config/MacroQuest_default.ini`
**Purpose:** Default configuration file for MacroQuest. Defines default settings that get deployed when no user config exists.

**Content/Example Value:**
- Default INI configuration with sections for various MacroQuest subsystems
- Used as a template that gets copied to the user's config directory

**What we can learn/use:** Reference for creating default configuration files that ship with an application.

### `data/macros/`
**Purpose:** Example MacroQuest macros (scripts) that demonstrate the macro scripting language. Provides ready-to-use automation scripts and learning examples.

**Content/Example Value:**
- `ArrayTest.mac` — Array manipulation example
- `Arrows.mac` — Archery automation
- `Cams.mac` — Camera control
- `DoorTests.mac` — Door interaction
- `EventTest.mac` — Event handling
- `Fish.mac` — Fishing automation
- `Follow.mac` — Follow target
- `Inventory.mac` — Inventory management
- `ShowBonuses.mac` — Display character bonuses
- `uservarstest.mac` — User variable testing

**What we can learn/use:** Example macro scripts showing the MacroQuest scripting language capabilities. Useful for understanding the macro API and automation patterns.

### `data/resources/CHANGELOG.md`
**Purpose:** Changelog for MacroQuest releases. Documents version history and changes.

**Content/Example Value:**
- Version history with dates and change descriptions

**What we can learn/use:** Standard changelog format for project documentation.

### `data/resources/ItemDB.txt`
**Purpose:** Item database file — contains item IDs, names, and properties for EverQuest items. Used for item lookup and display.

**Content/Example Value:**
- Tabular data with item IDs, names, and attributes
- Used by MacroQuest to display item information

**What we can learn/use:** Shows how to bundle a game data file for offline item lookup.

### `data/resources/Zones.ini`
**Purpose:** Zone database — maps zone IDs to zone names, short names, and properties. Used for zone identification and navigation.

**Content/Example Value:**
- INI format with zone ID as section key
- Fields: Zone name, short name, expansion, etc.

**What we can learn/use:** Reference for bundling game zone data in a parseable format.

---

## `extras/` Directory

### `extras/mkplugin_old/`
**Purpose:** Older version of the mkplugin code generation tool. Provides a reference for the evolution of the plugin template system.

**Content/Example Value:**
- `mkplugin.cpp` — Source code for the old plugin generator
- `mkplugin.vcxproj / .filters` — MSBuild project files
- `plugin_template/MQ2Template.cpp` — Old plugin template source
- `plugin_template/MQ2Template.vcxproj / .filters` — Old template project
- `plugin_template/ReadMe.txt` — Old template documentation

**What we can learn/use:** Shows how plugin templates evolved over time. The old template uses the `MQ2` prefix convention vs the newer `MQ` prefix in `tools/mkplugin/`. Good reference for understanding how to create a VS project template generator.

### `extras/plugins/MQ2EQIM/`
**Purpose:** Unmaintained Instant Messaging plugin for EverQuest. Provides an example of integrating external communication (IM) into the game client.

**Content/Example Value:**
- `MQ2EQIM.cpp` — Plugin source code (IM client integration)
- `MQ2EQIM.vcxproj / .filters` — MSBuild project files

**What we can learn/use:** Example of a plugin that interfaces with an external messaging service from within the game. Shows the pattern for network I/O in a DLL plugin.

### `extras/plugins/MQ2IRC/`
**Purpose:** Unmaintained IRC chat plugin. Integrates IRC (Internet Relay Chat) into the EQ client's chat system.

**Content/Example Value:**
- `MQ2Irc.cpp` — Plugin source code
- `mqirc.h` — IRC protocol and connection header
- `MQ2IRC.vcxproj / .filters` — MSBuild project files

**What we can learn/use:** Shows how to implement a TCP-based chat protocol as a game plugin. Contains IRC protocol handling and game chat integration patterns.

### `extras/plugins/MQ2Telnet/`
**Purpose:** Unmaintained Telnet server plugin. Allows remote control of MacroQuest via telnet protocol.

**Content/Example Value:**
- `MQ2Telnet.cpp` — Plugin entry point and telnet command handling
- `Telnet.h` — Telnet protocol definitions
- `TelnetServer.cpp` / `TelnetServer.h` — Telnet server implementation
- `WinTelnet.cpp` / `WinTelnet.h` — Windows socket abstraction
- `MQ2Telnet.vcxproj / .filters` — MSBuild project files

**What we can learn/use:** Most valuable plugin in extras. Contains a full TCP server implementation within a game DLL, showing:
- Windows socket programming in a plugin context
- Command parsing over network streams
- Threading model for a game-injected server
- Telnet protocol handling

**What we can learn/use (overall):** Shows how to archive deprecated components with their full source and build system intact. The MQ2Telnet plugin is particularly useful as a reference for implementing network services inside a game-injection DLL.

---

## `plugins/` Directory

### `plugins/`
**Purpose:** Top-level directory for custom and third-party plugins. This is separate from `src/plugins/` which contains built-in plugins shipped with MacroQuest. Users place their custom plugins here, and the CMake build system auto-detects them.

**Content/Example Value:**
- `.gitignore` — Ignores generated plugin files
- `mkplugin.exe` — Pre-built binary of the plugin generator tool (source in `tools/mkplugin/`)
- `MQ2Plugin.h` — Plugin development header (the user-facing header for writing plugins, distinct from the internal `include/mq/Plugin.h`)
- `README.md` — Instructions for plugin development

**What we can learn/use:** Shows how to separate built-in plugins (`src/plugins/`) from user/custom plugins (`plugins/`). This is important for our project architecture — we should maintain a similar distinction between our core plugins and user-contributed ones. The `MQ2Plugin.h` header at this level is the user-facing plugin API that would be what third-party developers include when writing plugins for our system. The CMake auto-detection in `cmake/plugins.cmake` demonstrates how to dynamically discover and build plugins placed in this directory.

---

## `include/` Directory

### `include/moveitem.h`
**Purpose:** Header file for item movement functionality. Defines the interface for moving items between inventory slots.

**Content/Example Value:**
- Function declarations and types for inventory item manipulation

**What we can learn/use:** Reference for inventory manipulation API design.

### `include/config/crashpad.h.example`
**Purpose:** Example/template configuration file for Google Crashpad (crash reporting). Users copy this to `crashpad.h` and fill in their own crash reporting credentials.

**Content/Example Value:**
- Template with placeholder values for Crashpad configuration
- Defines crash report upload URL and other settings

**What we can learn/use:** Shows the `.example` file pattern for configuration templates that users customize.

### `include/extras/wil/Constants.h`
**Purpose:** Windows Implementation Library (WIL) constants header. Provides Windows API error constants and utility definitions.

**Content/Example Value:**
- Windows error code definitions
- WIL utility constants

**What we can learn/use:** Reference for Windows error handling constants.

### `include/mq/Plugin.h`
**Purpose:** Core plugin header — defines the base interface and macros for creating MacroQuest plugins. Every plugin includes this header.

**Content/Example Value:**
- Plugin initialization/cleanup macros
- Plugin export declarations
- Event handler registration

**What we can learn/use:** Essential reference for understanding the MacroQuest plugin API. Shows how plugins are declared, exported, and registered.

### `include/mq/api/` (API Headers)
**Purpose:** Public API headers that define the interfaces for interacting with EverQuest game data and MacroQuest features.

**Content/Example Value:**
- `Abilities.h` — Character ability API
- `Achievements.h` — Achievement tracking API
- `ActorAPI.h` — Actor/entity interaction API
- `CommandAPI.h` — Command registration and execution API
- `DetourAPI.h` — Function detouring/hooking API
- `Inventory.h` — Inventory management API
- `Items.h` — Item data API
- `MacroAPI.h` — Macro execution API
- `MacroDataTypes.h` — Data type definitions for macros
- `Main.h` — Main MacroQuest API entry point
- `PluginAPI.h` — Plugin lifecycle API
- `RenderDoc.h` — RenderDoc integration API
- `Spawns.h` — Spawn/entity query API
- `Spells.h` — Spell data API
- `Textures.h` — Texture management API

**What we can learn/use:** Comprehensive API reference for MacroQuest plugin development. Shows how to structure a public API for a game automation platform.

### `include/mq/base/` (Base Headers)
**Purpose:** Base utility headers providing foundational types and utilities used throughout MacroQuest.

**Content/Example Value:**
- `Base.h` — Base type definitions
- `BuildInfo.h` — Build version information
- `Color.h` — Color type definitions
- `Common.h` — Common utility functions
- `Config.h` — Configuration system
- `Deprecation.h` — Deprecation warning macros
- `Enum.h` — Enum utility macros
- `EnumFmt.h` — Enum formatting
- `Format.h` — String formatting utilities
- `GlobalBuffer.h` — Global buffer management
- `Iterator.h` — Iterator utilities
- `Logging.h` — Logging system
- `PluginHandle.h` — Plugin handle type
- `ScopeExit.h` — RAII scope exit utility
- `Signal.h` — Signal/slot system
- `SimpleLexer.h` — Lexer for parsing
- `String.h` — String utilities
- `Threading.h` — Threading utilities
- `Traits.h` — Type traits
- `Vector.h` — Vector math types
- `WString.h` — Wide string utilities

**What we can learn/use:** Shows how to organize a comprehensive utility library with separate headers for each concern.

### `include/mq/contrib/protobuf/ProtobufLibs.h`
**Purpose:** Protobuf library integration header. Includes the necessary protobuf headers and defines compatibility macros.

**Content/Example Value:**
- Includes for Google Protocol Buffers
- Compatibility macros for protobuf version differences

**What we can learn/use:** Reference for integrating Protocol Buffers into a C++ project.

### `include/mq/imgui/` (ImGui Widget Headers)
**Purpose:** Custom ImGui widget headers for MacroQuest-specific UI components.

**Content/Example Value:**
- `AlphaMask.h` — Alpha masking for UI rendering
- `ConsoleWidget.h` — In-game console widget
- `ImGuiUtils.h` — ImGui utility functions
- `MQConsoleDelegate.h` — Console delegate interface
- `Widgets.h` — Custom widget declarations

**What we can learn/use:** Shows how to extend Dear ImGui with custom widgets for game overlay UIs.

### `include/mq/plugin/pluginapi.h`
**Purpose:** Plugin API header — defines the interface that plugins use to interact with the MacroQuest core.

**Content/Example Value:**
- Plugin registration and initialization
- Event subscription
- Command registration

**What we can learn/use:** Core plugin API reference.

### `include/mq/utils/` (Utility Headers)
**Purpose:** Utility headers providing helper functions for common tasks.

**Content/Example Value:**
- `Args.h` — Argument parsing utilities
- `Benchmarks.h` — Performance benchmarking
- `HotKeys.h` — Hotkey management
- `Keybinds.h` — Key binding system
- `Markov.h` — Markov chain text generation
- `Naming.h` — Naming conventions and utilities
- `OS.h` — Operating system utilities

**What we can learn/use:** Shows how to organize utility functions by domain.

### `include/mq/zep/` (Zep Editor Headers)
**Purpose:** Zep code editor integration headers for MacroQuest's in-game scripting editor.

**Content/Example Value:**
- `ImGuiZepConsole.h` — Zep-based console widget
- `ImGuiZepEditor.h` — Zep-based code editor widget

**What we can learn/use:** Shows how to integrate the Zep code editor with ImGui for an in-game scripting environment.

---

## `src/` Directory

The `src/` directory contains the main source code for MacroQuest, organized into subdirectories by component.

### `src/commands.json`
**Purpose:** JSON file defining all MacroQuest slash commands and their metadata. Used for command registration and documentation generation.

**Content/Example Value:**
- JSON array of command objects with name, description, syntax, and category
- Used by the command system to register available commands

**What we can learn/use:** Reference for defining commands in a structured JSON format that can be used for both runtime registration and documentation.

### `src/Common.cmake` / `src/Common.props`
**Purpose:** Shared CMake module and MSBuild property sheet for common build configuration across all src subprojects. Defines standard include paths, defines, and compiler settings.

**Content/Example Value:**
- Common include directories
- Standard preprocessor definitions
- Compiler warning levels and settings

**What we can learn/use:** Shows how to share build configuration across multiple subprojects in both CMake and MSBuild.

### `src/Directory.Build.props`
**Purpose:** MSBuild directory build props — applies common build settings to all projects in the src directory tree. Used for centralized MSBuild configuration.

**Content/Example Value:**
- Common C++ settings for all projects
- Standard include paths and defines

**What we can learn/use:** MSBuild pattern for centralized project configuration.

### `src/MacroQuest.natvis`
**Purpose:** Visual Studio Natvis file — custom debugger visualizations for MacroQuest data types. Makes debugging easier by showing complex types in a readable format.

**Content/Example Value:**
- Custom visualizers for MQ types (spells, items, spawns, etc.)
- XML format defining how types appear in debugger watch windows

**What we can learn/use:** Reference for creating custom debugger visualizations for complex game data types.

### `src/MacroQuest.sln`
**Purpose:** Visual Studio solution file — the main solution that organizes all MacroQuest projects. Generated by CMake but also maintained for direct MSBuild usage.

**Content/Example Value:**
- References to all projects: MQ2Main, eqlib, imgui, login, routing, zep, loader, plugins, tests
- Solution folder organization

**What we can learn/use:** Shows how to organize a large Visual Studio solution with multiple projects and solution folders.

### `src/Plugin.cmake` / `src/Plugin.props`
**Purpose:** CMake module and MSBuild property sheet specifically for plugin projects. Provides standard plugin build configuration.

**Content/Example Value:**
- Plugin-specific include paths and defines
- Plugin export settings
- Standard plugin dependencies

**What we can learn/use:** Shows how to create a standardized build configuration for plugin-type projects.

### `src/VisualStudioAnalysis.ruleset`
**Purpose:** Visual Studio code analysis ruleset — defines which code analysis rules are enabled/disabled for the project.

**Content/Example Value:**
- Custom ruleset with selected Microsoft Native Recommended Rules
- Disabled rules that are too noisy or not applicable

**What we can learn/use:** Reference for creating a custom code analysis ruleset that balances thoroughness with practicality.

### `src/common/`
**Purpose:** Common utility code shared across MacroQuest components. Provides header-only utilities.

**Content/Example Value:**
- `Common.h` — Common type definitions and macros
- `ConfigUtils.h` — Configuration utility functions
- `HotKeys.cpp` — Hotkey handling implementation
- `MiscUtils.h` — Miscellaneous utilities
- `StringUtils.h` — String manipulation utilities

**What we can learn/use:** Shows how to organize shared utility code that's used across multiple components.

### `src/eqlib/`
**Purpose:** EverQuest library — the core interface library that provides access to EQ client memory structures, offsets, and functions. This is the foundation that all of MacroQuest builds upon.

**Content/Example Value:**
- `include/` — Headers defining EQ client data structures (spells, items, spawns, etc.)
- `src/` — Implementation of EQ client interface
- `tools/` — Tools for working with eqlib
- `CMakeLists.txt`, `eqlib.cmake`, `eqlib.props` — Build configuration
- `eqlib.vcxproj` — Visual Studio project
- `vcpkg.json`, `portfile.cmake` — vcpkg integration

**What we can learn/use:** Critical reference for understanding how to interface with the EQ client. Shows how to define memory structures, offsets, and function pointers for game interaction.

### `src/imgui/`
**Purpose:** MacroQuest's custom ImGui integration layer. Extends Dear ImGui with game-specific widgets, file dialogs, text editors, and memory editors.

**Content/Example Value:**
- `ImGuiFileDialog.cpp/.h` — File dialog widget
- `ImGuiTextEditor.cpp/.h` — Text editor widget
- `ImGuiMemoryEditor.h` — Memory editor widget
- `ImGuiTreePanelWindow.h` — Tree panel widget
- `ImGuiUtils.cpp/.h` — ImGui utility functions
- `imanim/` — Animation library for ImGui
- `implot/` — Plotting library for ImGui
- `fonts/` — Font files
- `dirent/` — Directory entry utilities
- `CMakeLists.txt`, `imgui.vcxproj` — Build configuration

**What we can learn/use:** Shows how to build a comprehensive ImGui extension library with custom widgets for game overlays.

### `src/loader/`
**Purpose:** MacroQuest loader — the executable that launches EverQuest and injects MacroQuest into the game process. Handles process management, crash reporting, and auto-login.

**Content/Example Value:**
- `MacroQuest.cpp/.h` — Main loader entry point
- `Crashpad.cpp/.h` — Crash reporting integration
- `LoaderAutoLogin.cpp/.h` — Auto-login functionality
- `ProcessMonitor.cpp/.h` — Process monitoring
- `ProcessList.cpp` — Process enumeration
- `RemoteOps.cpp` — Remote process operations
- `PostOffice.cpp/.h` — Inter-process communication
- `ImGui.cpp/.h` — Loader UI
- `Utility.cpp` — Utility functions
- `WinToastLib.cpp/.h` — Windows toast notifications
- `imgui_backend/` — ImGui backend for the loader
- `CMakeLists.txt`, `MacroQuest.vcxproj` — Build configuration

**What we can learn/use:** Reference for creating a game loader/injector with:
- Process management and monitoring
- Crash reporting via Crashpad
- Auto-login state machine
- Inter-process communication
- GUI overlay with ImGui

### `src/login/`
**Purpose:** Login library — handles EverQuest authentication and session management. Communicates with login servers.

**Content/Example Value:**
- `Login.cpp/.h` — Login implementation
- `AutoLogin.cpp/.h` — Automated login
- `Companies.h` — Server company definitions
- `Login.proto` — Protocol Buffers definition for login protocol
- `CMakeLists.txt`, `login.vcxproj` — Build configuration

**What we can learn/use:** Shows how to implement game authentication and session management with protobuf-based protocol definitions.

### `src/main/`
**Purpose:** MQ2Main — the core MacroQuest DLL that gets injected into the EQ client. This is the heart of MacroQuest, containing all core functionality.

**Content/Example Value:**
- Core: `MacroQuest.cpp/.h`, `MQ2Main.h`, `MQ2MainBase.h`
- Data: `MQ2Data.cpp`, `MQ2DataVars.cpp`, `MQ2DataContainers.h`
- Commands: `MQCommands.cpp`, `MQ2Commands.h`
- Spells: `MQ2Spells.cpp`, `MQ2SpellSearch.h`
- Spawns: `MQ2Spawns.cpp`
- Items: `MQ2Items.cpp`
- Windows: `MQ2Windows.cpp`, `MQ2WindowInspector.cpp`
- Keybinds: `MQ2KeyBinds.cpp/.h`
- Macros: `MQ2MacroCommands.cpp`
- Chat: `MQ2ChatHook.cpp`
- Globals: `MQ2Globals.cpp/.h`
- Utilities: `MQ2Utilities.cpp/.h`
- String DB: `MQ2StringDB.cpp`
- Ground Spawns: `MQ2GroundSpawns.cpp`
- Achievements: `MQAchievements.cpp`
- Actor API: `MQActorAPI.cpp/.h`
- Command API: `MQCommandAPI.cpp/.h`
- Data API: `MQDataAPI.cpp/.h`
- Detour API: `MQDetourAPI.cpp`
- Inventory: `MQInventory.cpp`
- Plugin Handler: `MQPluginHandler.cpp/.h`
- Post Office: `MQPostOffice.cpp/.h`
- RenderDoc: `MQRenderDoc.cpp/.h`
- Input API: `MQInputAPI.cpp`
- Display Hook: `MQDisplayHook.cpp`
- ImGui Console: `MQImGuiConsole.cpp`
- ImGui Widgets: `MQImguiWidgets.cpp`
- Graphics: `GraphicsEngine.cpp/.h`, `GraphicsEngineDX9.cpp`, `GraphicsEngineDX11.cpp`, `GraphicsResources.cpp/.h`
- ImGui Backend: `ImGuiBackend.h`, `ImGuiBackendDX9.cpp`, `ImGuiBackendDX11.cpp`, `ImGuiBackendWin32.cpp`
- ImGui Manager: `ImGuiManager.cpp/.h`
- Alpha Mask: `ImGuiAlphaMask.cpp`
- Crash Handler: `CrashHandler.cpp/.h`
- Assembly: `AssemblyFunctions64.asm`
- Developer Tools: `MQ2DeveloperTools.cpp/.h`
- Benchmarks: `MQ2Benchmarks.cpp`
- Frame Limiter: `MQ2FrameLimiter.cpp`
- Anonymize: `MQ2Anonymize.cpp`
- Auto Inventory: `MQ2AutoInventory.cpp`
- Cached Buffs: `MQ2CachedBuffs.cpp`
- Login Frontend: `MQ2LoginFrontend.cpp`
- Pulse: `MQ2Pulse.cpp`
- Version Info: `MQ2VersionInfo.h`
- Inlines: `MQ2Inlines.h`
- Internal: `MQ2Internal.h`
- Prototypes: `MQ2Prototypes.h`
- Mercenaries: `MQ2Mercenaries.h`
- `api/` — API subdirectory
- `datatypes/` — Data type definitions
- `emu/` — Emulator-specific code
- `CMakeLists.txt`, `MQ2Main.vcxproj` — Build configuration

**What we can learn/use:** The most important reference in the entire project. Shows how to build a game injection DLL with:
- Memory structure definitions for game objects
- Function detouring and hooking
- Command system
- Macro execution engine
- UI overlay with ImGui
- Graphics engine abstraction (DX9/DX11)
- Plugin system
- Inter-process communication
- Crash handling

### `src/plugins/`
**Purpose:** Built-in MacroQuest plugins that ship with the project. Each plugin provides specific functionality.

**Content/Example Value:**
- `autobank/` — Automated banking plugin
- `autologin/` — Automated login with state machine
- `bzsrch/` — Bazaar search plugin
- `chat/` — Chat enhancements
- `chatwnd/` — Custom chat window
- `custombinds/` — Custom key bindings
- `eqbugfix/` — EverQuest bug fixes
- `hud/` — Heads-up display
- `itemdisplay/` — Enhanced item display
- `labels/` — Label rendering
- `lua/` — Lua scripting engine (most complex plugin)
- `map/` — In-game map with assembly optimizations
- `pluginapi/` — Plugin API library
- `targetinfo/` — Target information display
- `xtarinfo/` — Extended target information

**What we can learn/use:** Shows how to structure a plugin-based architecture with:
- Standardized plugin interface
- Each plugin in its own directory with CMakeLists.txt
- Mix of simple and complex plugins
- Lua scripting engine integration
- Assembly-optimized components (map, itemdisplay)

### `src/private/`
**Purpose:** Private/closed-source components. Contains code that is not publicly distributed.

**Content/Example Value:**
- Proprietary or closed-source modules
- Not available in the public repository

**What we can learn/use:** Shows how to structure a project with both open-source and closed-source components.

### `src/routing/`
**Purpose:** Routing library — handles message routing and inter-component communication within MacroQuest.

**Content/Example Value:**
- Message routing infrastructure
- Component communication

**What we can learn/use:** Reference for implementing a message routing system in a modular application.

### `src/tests/`
**Purpose:** Test projects for MacroQuest. Contains unit tests and integration tests.

**Content/Example Value:**
- Unit tests for core components
- Integration tests

**What we can learn/use:** Shows how to structure tests for a game automation project.

### `src/zep/`
**Purpose:** Zep code editor integration — wraps the Zep editor for use within MacroQuest's ImGui environment.

**Content/Example Value:**
- ImGui integration for Zep editor
- Console widget using Zep

**What we can learn/use:** Shows how to integrate a third-party code editor widget into an ImGui application.

---

## `tools/` Directory

### `tools/build_scripts/`
**Purpose:** PowerShell build automation scripts used during the MQ2Main compilation process. Handles pre/post-build tasks, versioning, plugin validation, and vcpkg management.

**Content/Example Value:**
- `MQ2Main_PostBuild.ps1` — Post-build actions for the core DLL
- `MQ2Main_PreBuild.ps1` — Pre-build environment setup
- `Plugin_Versioning.ps1` — Auto-incrementing plugin version numbers
- `Validate_Plugin_Projects.ps1` — Validates plugin project configurations
- `vcpkg_mq.ps1` — vcpkg integration script for build pipeline
- `protoc/protobuf.cmake` / `.props` / `.targets` / `.xml` — Protocol Buffers compiler integration for build system

**What we can learn/use:** Shows how to automate a complex build pipeline with PowerShell scripts. The pre/post-build pattern and plugin versioning are directly applicable to our DLL project.

### `tools/comment-update/`
**Purpose:** C# tool for automatically updating comments in source code. Likely used to maintain documentation consistency across the codebase.

**Content/Example Value:**
- `src/comment-update/Program.cs` — Main C# application source
- `src/comment-update/comment-update.csproj` — C# project file
- `src/comment-update/App.config` — Application configuration
- `src/comment-update/Properties/AssemblyInfo.cs` — Assembly metadata
- `comment-update.config` — Tool configuration
- `setup.cmd` — Setup batch file
- `update-debug.bat` / `update-debug-x86.bat` — Debug update scripts

**What we can learn/use:** Reference for a small C# tool integrated into a C++ project's toolchain. Shows how to use a .NET utility for code maintenance tasks.

### `tools/conversions/`
**Purpose:** PowerShell infrastructure for migrating MSBuild (.vcxproj) projects to CMake. Provides the core conversion engine used by `batch-convert.ps1` at the project root.

**Content/Example Value:**
- `Convert-VcxprojToCMake.ps1` — Main conversion script
- `Convert-PropsToCMake.ps1` — Property sheet (.props) conversion
- `compile_commands.ps1` — Compile commands generation
- `Convert-VcxprojToCMake-README.md` — Conversion documentation
- `Modules/AnalysisReporting.psm1` — Reports on conversion results
- `Modules/ConversionCommon.psm1` — Shared conversion utilities
- `Modules/FileOperations.psm1` — File I/O helpers
- `Modules/GeneratorExpressions.psm1` — CMake generator expression handling
- `Modules/ItemDefinitionProcessing.psm1` — MSBuild item definition processing
- `Modules/PathUtilities.psm1` — Path manipulation utilities

**What we can learn/use:** Comprehensive reference for vcxproj-to-CMake migration. The modular PowerShell architecture (separate .psm1 modules per concern) is a good pattern to follow. Detailed understanding of MSBuild → CMake translation is useful if we ever need to port projects.

### `tools/mkplugin/`
**Purpose:** C++ plugin generation tool that creates new MacroQuest plugin projects from a template. Ships as `mkplugin.exe` in the `plugins/` directory.

**Content/Example Value:**
- `mkplugin.cpp` — Plugin generator source code
- `mkplugin.rc` / `resource.h` — Windows resource definitions
- `mkplugin.sln` / `.vcxproj` / `.filters` — Visual Studio solution and project
- `plugin_template/` — Template files for new plugins:
  - `MQPluginTemplate.cpp` — Template source
  - `MQPluginTemplate.rc` — Template resource
  - `MQPluginTemplate.vcxproj` / `.filters` — Template project
  - `MQPluginTemplate.vcxproj.filters` — Template filter
  - `resource.h`, `README.md`, `.gitignore`

**What we can learn/use:** Extremely useful reference for creating a code generation tool. Shows how to:
- Build a C++ tool that generates other C++ projects from templates
- Structure plugin template files for easy customization
- Package a template system with resource files and project configurations
- The template itself shows the minimal structure needed for a MacroQuest plugin

### `tools/python/`
**Purpose:** Bundled Python 3.8 runtime executable and libraries. Used by MacroQuest's scripting system to run Python-based tools and scripts.

**Content/Example Value:**
- `python.exe` / `pythonw.exe` — Python 3.8 interpreter
- `python38.dll` / `python3.dll` — Python runtime DLLs
- Standard library modules: `_asyncio.pyd`, `_bz2.pyd`, `_ctypes.pyd`, `_decimal.pyd`, `_elementtree.pyd`, `_hashlib.pyd`, `_lzma.pyd`, `_msi.pyd`, `_multiprocessing.pyd`, `_overlapped.pyd`, `_queue.pyd`, `_socket.pyd`, `_sqlite3.pyd`, `_ssl.pyd`
- Supporting DLLs: `libcrypto-1_1.dll`, `libffi-7.dll`, `libssl-1_1.dll`, `sqlite3.dll`, `vcruntime140.dll`
- `LICENSE.txt`, `python.cat`, `python38._pth`

**What we can learn/use:** Shows how to bundle a scripting runtime (Python) within a game automation tool. If we need to provide scripting support in our DLL, this demonstrates the embedded Python pattern.

**What we can learn/use (overall tools/):** Shows comprehensive development tooling for a large C++ project with:
- Build automation (PowerShell scripts)
- Code maintenance tools (C#)
- Build system migration tools (PowerShell modules)
- Code generation tools (C++ template system)
- Bundled scripting runtime (Python)

---

## Summary

This file map covers the entire MacroQuest project structure. Key takeaways for our own work:

1. **Build System**: CMake with vcpkg integration, supporting both CMake and legacy MSBuild
2. **Plugin Architecture**: Well-defined plugin API with auto-detection and standardized build config
3. **Game Integration**: Memory structure definitions, function detouring, and graphics engine abstraction
4. **UI Framework**: Dear ImGui with custom widgets, code editor, and game overlay
5. **Scripting**: Lua scripting engine with full API bindings
6. **Tooling**: Development tools for conversion, code generation, and build automation
7. **Third-party**: Well-organized contrib directory with clear separation of dependencies


