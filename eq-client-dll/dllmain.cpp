// dllmain.cpp : EQ Client DLL - dinput8.dll Proxy with DB String Proxy & Chat Commands
//
// This DLL acts as a dinput8.dll proxy that loads automatically when eqgame.exe
// starts (no injection needed). It forwards all 5 real dinput8 exports to the
// system DLL in SysWOW64 while installing our hooks.
//
// Hooks:
//   - CDBStr::GetString - redirects string lookups to server-side DbStrProxy via TCP
//   - CEverQuest::InterpretCmd - intercepts chat commands like #helloworld and #reload
//
// Offsets from MacroQuest for RoF2 (May 10 2013 build):
//   CEverQuest__InterpretCmd_x = 0x51FCE0
//   CEverQuest__dsp_chat_x     = 0x51F1A0
//   CDBStr__GetString_x        = 0x4866C0
//   pinstCDBStr_x              = 0xD1F380

// winsock2.h MUST be included before windows.h
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <wininet.h>
#include <psapi.h>
#include <cstdio>
#include <string>
#include <vector>
#include <unordered_map>
#include <atomic>
#include <cstdint>

#pragma comment(lib, "ws2_32.lib")
#pragma comment(lib, "wininet.lib")
#pragma comment(lib, "psapi.lib")

// ============================================================================
// dinput8.dll Proxy Forwarding
// ============================================================================
//
// eqgame.exe loads dinput8.dll at startup (DirectInput). Windows DLL search
// order checks the app directory before system directories, so by placing our
// dinput8.dll in the EQ directory, Windows loads ours first.
//
// We load the real C:\Windows\SysWOW64\dinput8.dll and forward all 5 exports.

// Handle to the real dinput8.dll from SysWOW64
HMODULE g_real_dinput8 = NULL;

// Forwarding wrapper functions
extern "C" __declspec(dllexport) HRESULT WINAPI ForwardDirectInput8Create(
    HINSTANCE hinst, DWORD dwVersion, REFIID riidltf, LPVOID *ppvOut, LPUNKNOWN punkOuter)
{
    if (!g_real_dinput8) {
        return E_FAIL;
    }
    typedef HRESULT (WINAPI *RealFunc)(HINSTANCE, DWORD, REFIID, LPVOID*, LPUNKNOWN);
    RealFunc real = (RealFunc)GetProcAddress(g_real_dinput8, "DirectInput8Create");
    if (!real) return E_FAIL;
    return real(hinst, dwVersion, riidltf, ppvOut, punkOuter);
}

extern "C" __declspec(dllexport) HRESULT WINAPI ForwardDllCanUnloadNow()
{
    if (!g_real_dinput8) {
        return S_FALSE;
    }
    typedef HRESULT (WINAPI *RealFunc)();
    RealFunc real = (RealFunc)GetProcAddress(g_real_dinput8, "DllCanUnloadNow");
    if (!real) return S_FALSE;
    return real();
}

extern "C" __declspec(dllexport) HRESULT WINAPI ForwardDllGetClassObject(
    REFCLSID rclsid, REFIID riid, LPVOID *ppv)
{
    if (!g_real_dinput8) {
        return CLASS_E_CLASSNOTAVAILABLE;
    }
    typedef HRESULT (WINAPI *RealFunc)(REFCLSID, REFIID, LPVOID*);
    RealFunc real = (RealFunc)GetProcAddress(g_real_dinput8, "DllGetClassObject");
    if (!real) return CLASS_E_CLASSNOTAVAILABLE;
    return real(rclsid, riid, ppv);
}

extern "C" __declspec(dllexport) HRESULT WINAPI ForwardDllRegisterServer()
{
    if (!g_real_dinput8) {
        return SELFREG_E_CLASS;
    }
    typedef HRESULT (WINAPI *RealFunc)();
    RealFunc real = (RealFunc)GetProcAddress(g_real_dinput8, "DllRegisterServer");
    if (!real) return SELFREG_E_CLASS;
    return real();
}

extern "C" __declspec(dllexport) HRESULT WINAPI ForwardDllUnregisterServer()
{
    if (!g_real_dinput8) {
        return SELFREG_E_CLASS;
    }
    typedef HRESULT (WINAPI *RealFunc)();
    RealFunc real = (RealFunc)GetProcAddress(g_real_dinput8, "DllUnregisterServer");
    if (!real) return SELFREG_E_CLASS;
    return real();
}

// Load the real dinput8.dll from SysWOW64
bool LoadRealDinput8()
{
    if (g_real_dinput8) {
        return true;
    }

    g_real_dinput8 = LoadLibraryA("C:\\Windows\\SysWOW64\\dinput8.dll");
    if (!g_real_dinput8) {
        // Try alternative paths
        g_real_dinput8 = LoadLibraryA("C:\\Windows\\System32\\dinput8.dll");
    }
    return (g_real_dinput8 != NULL);
}


// ============================================================================
// Configuration
// ============================================================================

// Server address for DbStrProxy TCP connection
const char* PROXY_HOST = "108.181.218.166";
const int   PROXY_PORT = 9100;

// URL for downloading dbstr_us.txt from the patcher file server
const char* DBSTR_DOWNLOAD_URL = "http://108.181.218.166/patch/rof/dbstr_us.txt";

// EQ Client offsets (RoF2 client - "May 10 2013" build)
const uintptr_t CEverQuest_InterpretCmd_Offset = 0x51FCE0;
const uintptr_t CEverQuest_dsp_chat_Offset     = 0x51F1A0;
const uintptr_t CDBStr_GetString_Offset        = 0x4866C0;
const uintptr_t pinstCDBStr_Offset             = 0xD1F380;

// ============================================================================
// TCP Client for communicating with the server-side DbStrProxy
// ============================================================================

class DbStrTcpClient {
public:
	DbStrTcpClient() : m_sock(INVALID_SOCKET), m_connected(false) {
		WSADATA wsaData;
		WSAStartup(MAKEWORD(2, 2), &wsaData);
		InitializeCriticalSection(&m_cs);
	}

	~DbStrTcpClient() {
		Disconnect();
		DeleteCriticalSection(&m_cs);
		WSACleanup();
	}

	bool Connect() {
		if (m_connected) return true;

		m_sock = socket(AF_INET, SOCK_STREAM, 0);
		if (m_sock == INVALID_SOCKET) {
			return false;
		}

		// Set timeout
		int timeout = 3000; // 3 seconds
		setsockopt(m_sock, SOL_SOCKET, SO_RCVTIMEO, (const char*)&timeout, sizeof(timeout));
		setsockopt(m_sock, SOL_SOCKET, SO_SNDTIMEO, (const char*)&timeout, sizeof(timeout));

		sockaddr_in server_addr;
		server_addr.sin_family = AF_INET;
		server_addr.sin_port = htons(PROXY_PORT);
		inet_pton(AF_INET, PROXY_HOST, &server_addr.sin_addr);

		if (connect(m_sock, (sockaddr*)&server_addr, sizeof(server_addr)) == SOCKET_ERROR) {
			closesocket(m_sock);
			m_sock = INVALID_SOCKET;
			return false;
		}

		m_connected = true;
		return true;
	}

	void Disconnect() {
		if (m_sock != INVALID_SOCKET) {
			closesocket(m_sock);
			m_sock = INVALID_SOCKET;
		}
		m_connected = false;
	}

	bool SendRequest(const std::string& request) {
		if (!m_connected && !Connect()) {
			return false;
		}

		std::string req = request + "\n";
		int sent = send(m_sock, req.c_str(), (int)req.length(), 0);
		if (sent == SOCKET_ERROR) {
			Disconnect();
			return false;
		}
		return true;
	}

	bool ReceiveResponse(std::string& response) {
		if (!m_connected) return false;

		char buffer[4096];
		int received = recv(m_sock, buffer, sizeof(buffer) - 1, 0);
		if (received <= 0) {
			Disconnect();
			return false;
		}

		buffer[received] = '\0';
		response = buffer;

		// Remove trailing newline if present
		if (!response.empty() && response.back() == '\n') {
			response.pop_back();
		}
		if (!response.empty() && response.back() == '\r') {
			response.pop_back();
		}

		return true;
	}

	std::string LookupString(int id, int type) {
		EnterCriticalSection(&m_cs);

		std::string request = "GET_STR|" + std::to_string(id) + "|" + std::to_string(type);
		std::string result;
		std::string response;

		// Try up to 2 times
		for (int attempt = 0; attempt < 2; attempt++) {
			if (!SendRequest(request)) {
				continue;
			}

			if (!ReceiveResponse(response)) {
				continue;
			}

			// Parse response: "OK|<value>" or "NOT_FOUND|<id>|<type>"
			if (response.substr(0, 3) == "OK|") {
				// Unescape the value
				std::string value = response.substr(3);
				std::string unescaped;
				unescaped.reserve(value.size());
				for (size_t i = 0; i < value.size(); i++) {
					if (value[i] == '\\' && i + 1 < value.size()) {
						switch (value[i + 1]) {
							case '|': unescaped += '|'; i++; break;
							case 'n': unescaped += '\n'; i++; break;
							case 'r': unescaped += '\r'; i++; break;
							default: unescaped += value[i]; break;
						}
					} else {
						unescaped += value[i];
					}
				}
				result = unescaped;
				break;
			}

			break;
		}

		LeaveCriticalSection(&m_cs);
		return result;
	}

private:
	SOCKET m_sock;
	bool m_connected;
	CRITICAL_SECTION m_cs;
};

// ============================================================================
// In-Memory Cache
// ============================================================================

class DbStrCache {
public:
	DbStrCache() {
		InitializeCriticalSection(&m_cs);
	}

	~DbStrCache() {
		DeleteCriticalSection(&m_cs);
	}

	std::string Get(int id, int type) {
		EnterCriticalSection(&m_cs);
		std::string key = Key(id, type);
		auto it = m_cache.find(key);
		std::string result;
		if (it != m_cache.end()) {
			result = it->second;
		}
		LeaveCriticalSection(&m_cs);
		return result;
	}

	void Set(int id, int type, const std::string& value) {
		EnterCriticalSection(&m_cs);
		m_cache[Key(id, type)] = value;
		LeaveCriticalSection(&m_cs);
	}

	void Clear() {
		EnterCriticalSection(&m_cs);
		m_cache.clear();
		LeaveCriticalSection(&m_cs);
	}

private:
	static std::string Key(int id, int type) {
		return std::to_string(id) + ":" + std::to_string(type);
	}

	std::unordered_map<std::string, std::string> m_cache;
	CRITICAL_SECTION m_cs;
};

// ============================================================================
// Global instances
// ============================================================================

DbStrTcpClient g_tcp_client;
DbStrCache     g_cache;
std::atomic<bool> g_initialized(false);
std::atomic<bool> g_hook_installed(false);
std::atomic<bool> g_cmd_hook_installed(false);

// Saved original bytes for unhooking
BYTE g_original_bytes[5] = {0};
BYTE g_cmd_original_bytes[5] = {0};
uintptr_t g_target_func_addr = 0;
uintptr_t g_cmd_func_addr = 0;

// ============================================================================
// CDBStr::GetString Hook
// ============================================================================

// Original function type (__thiscall)
typedef const char* (__thiscall* GetDbStrFunc)(void* this_ptr, int id, int type, bool* found);

// Saved original function pointer
GetDbStrFunc g_original_get_db_str = nullptr;

// Buffer for our cached string results (must persist across calls)
__declspec(thread) char g_thread_local_buffer[4096];

// Our hook function
const char* __fastcall HookedGetDbStr(void* this_ptr, int /*unused_edx*/, int id, int type, bool* found) {
	// First, try the cache
	std::string cached = g_cache.Get(id, type);
	if (!cached.empty()) {
		if (found) *found = true;
		strncpy_s(g_thread_local_buffer, sizeof(g_thread_local_buffer), cached.c_str(), _TRUNCATE);
		return g_thread_local_buffer;
	}

	// Try the server proxy
	std::string server_result = g_tcp_client.LookupString(id, type);
	if (!server_result.empty()) {
		g_cache.Set(id, type, server_result);
		if (found) *found = true;
		strncpy_s(g_thread_local_buffer, sizeof(g_thread_local_buffer), server_result.c_str(), _TRUNCATE);
		return g_thread_local_buffer;
	}

	// Fall back to original function
	if (g_original_get_db_str) {
		const char* result = g_original_get_db_str(this_ptr, id, type, found);
		if (result && result[0] != '\0') {
			g_cache.Set(id, type, result);
		}
		return result;
	}

	if (found) *found = false;
	return "";
}

// ============================================================================
// CEverQuest::InterpretCmd Hook (for chat commands)
// ============================================================================
//
// CEverQuest::InterpretCmd is the function that processes all /commands
// and chat input. By hooking it, we can intercept #commands before they
// are sent to the server.
//
// Function signature (__thiscall):
//   void InterpretCmd(PlayerClient* pChar, const char* szFullLine)
//
// Offset: 0x51FCE0 (from MacroQuest)

// CEverQuest::dsp_chat function for displaying messages in the EQ chat window
// void __thiscall dsp_chat(const char* Text, int Color)
// Offset: 0x51F1A0
typedef void (__thiscall* DspChatFunc)(void* this_ptr, const char* text, int color);
DspChatFunc g_dsp_chat = nullptr;

// Original InterpretCmd function type
typedef void (__thiscall* InterpretCmdFunc)(void* this_ptr, void* pChar, const char* szFullLine);
InterpretCmdFunc g_original_interpret_cmd = nullptr;

// Get the EQ client's player name from the local player structure
// pinstLocalPlayer = 0xDD2630 (pointer to SPAWNINFO)
const uintptr_t pinstLocalPlayer_Offset = 0xDD2630;

// Helper: Get the player name from the EQ client
std::string GetPlayerName() {
	HMODULE eqgame = GetModuleHandleA("eqgame.exe");
	if (!eqgame) return "Unknown";

	uintptr_t base = (uintptr_t)eqgame;
	uintptr_t pLocalPlayerPtr = base + pinstLocalPlayer_Offset;
	void** pLocalPlayer = (void**)pLocalPlayerPtr;
	if (!pLocalPlayer || !*pLocalPlayer) return "Unknown";

	// SPAWNINFO structure: at offset 0x14 is the name (char[64])
	char* name = (char*)(*pLocalPlayer) + 0x14;
	return std::string(name);
}

// Helper: Display a message in the EQ chat window using dsp_chat
void DisplayChatMessage(const char* message) {
	if (!g_dsp_chat) return;

	// Get the CEverQuest instance pointer
	// pinstCEverQuest = 0xE67CCC
	HMODULE eqgame = GetModuleHandleA("eqgame.exe");
	if (!eqgame) return;

	uintptr_t base = (uintptr_t)eqgame;
	uintptr_t pEverQuestPtr = base + 0xE67CCC;
	void** pEverQuest = (void**)pEverQuestPtr;
	if (!pEverQuest || !*pEverQuest) return;

	// Color 15 = yellow (chat color), 0 = white, 230 = red, 231 = green
	g_dsp_chat(*pEverQuest, message, 15);
}

// Download a file from a URL and save it to a local path
// Returns true on success
bool DownloadFileToDisk(const char* url, const char* localPath) {
	bool success = false;
	HINTERNET hInternet = InternetOpenA(
		"DbStrProxy/1.0",
		INTERNET_OPEN_TYPE_PRECONFIG,
		NULL, NULL, 0
	);

	if (!hInternet) return false;

	HINTERNET hUrl = InternetOpenUrlA(
		hInternet,
		url,
		NULL, 0,
		INTERNET_FLAG_RELOAD | INTERNET_FLAG_NO_CACHE_WRITE,
		0
	);

	if (hUrl) {
		HANDLE hFile = CreateFileA(
			localPath,
			GENERIC_WRITE,
			0,
			NULL,
			CREATE_ALWAYS,
			FILE_ATTRIBUTE_NORMAL,
			NULL
		);

		if (hFile != INVALID_HANDLE_VALUE) {
			char buffer[8192];
			DWORD bytesRead;
			DWORD totalBytes = 0;

			while (InternetReadFile(hUrl, buffer, sizeof(buffer), &bytesRead) && bytesRead > 0) {
				DWORD bytesWritten;
				WriteFile(hFile, buffer, bytesRead, &bytesWritten, NULL);
				totalBytes += bytesWritten;
			}

			CloseHandle(hFile);

			if (totalBytes > 0) {
				success = true;
			}
		}

		InternetCloseHandle(hUrl);
	}

	InternetCloseHandle(hInternet);
	return success;
}

// Our hooked InterpretCmd function
void __fastcall HookedInterpretCmd(void* this_ptr, int /*unused_edx*/, void* pChar, const char* szFullLine) {
	if (szFullLine) {
		// Check for #helloworld command
		if (strcmp(szFullLine, "#helloworld") == 0) {
			std::string playerName = GetPlayerName();
			std::string msg = "Hello, " + playerName + ".";
			DisplayChatMessage(msg.c_str());
			return; // Command consumed, don't pass to original
		}

		// Check for #reload aa_data global command
		if (strcmp(szFullLine, "#reload aa_data global") == 0) {
			DisplayChatMessage("Reloading dbstr_us.txt from server...");

			// Get the EQ directory path from the module path
			char eqPath[MAX_PATH];
			HMODULE eqgame = GetModuleHandleA("eqgame.exe");
			if (eqgame) {
				GetModuleFileNameA(eqgame, eqPath, MAX_PATH);
				char* lastSlash = strrchr(eqPath, '\\');
				if (lastSlash) {
					*(lastSlash + 1) = '\0';
				}
			} else {
				GetCurrentDirectoryA(MAX_PATH, eqPath);
			}

			std::string dbstrPath = std::string(eqPath) + "dbstr_us.txt";

			// Download the latest dbstr_us.txt from the server
			if (DownloadFileToDisk(DBSTR_DOWNLOAD_URL, dbstrPath.c_str())) {
				// Clear our local cache so the next lookup goes to the server
				g_cache.Clear();

				// Also send RELOAD to the server proxy to refresh its cache
				g_tcp_client.SendRequest("RELOAD");

				DisplayChatMessage("dbstr_us.txt reloaded successfully from server.");
			} else {
				DisplayChatMessage("ERROR: Failed to download dbstr_us.txt from server.");
			}

			return; // Command consumed
		}
	}

	// Pass through to original handler for all other input
	if (g_original_interpret_cmd) {
		g_original_interpret_cmd(this_ptr, pChar, szFullLine);
	}
}

// ============================================================================
// Memory protection helpers
// ============================================================================

bool UnprotectMemory(uintptr_t addr, size_t size) {
	DWORD old_protect;
	return VirtualProtect((LPVOID)addr, size, PAGE_EXECUTE_READWRITE, &old_protect) != FALSE;
}

// ============================================================================
// Hook Installation
// ============================================================================

bool InstallGetDbStrHook() {
	if (g_hook_installed.exchange(true)) {
		return true;
	}

	// Get the base address of eqgame.exe
	HMODULE eqgame = GetModuleHandleA("eqgame.exe");
	if (!eqgame) {
		eqgame = GetModuleHandleA(NULL);
	}
	if (!eqgame) {
		return false;
	}

	// Calculate the target function address
	uintptr_t base_addr = (uintptr_t)eqgame;
	g_target_func_addr = base_addr + CDBStr_GetString_Offset;

	// Save the original function pointer
	g_original_get_db_str = (GetDbStrFunc)g_target_func_addr;

	// Save original bytes for unhooking
	memcpy(g_original_bytes, (void*)g_target_func_addr, 5);

	// Calculate the relative jump offset
	uintptr_t hook_func_addr = (uintptr_t)&HookedGetDbStr;
	int32_t rel_offset = (int32_t)(hook_func_addr - (g_target_func_addr + 5));

	// Build the JMP instruction
	BYTE jmp_code[5];
	jmp_code[0] = 0xE9; // JMP rel32
	memcpy(&jmp_code[1], &rel_offset, sizeof(rel_offset));

	// Make the target memory writable
	DWORD old_protect;
	if (!VirtualProtect((LPVOID)g_target_func_addr, 5, PAGE_EXECUTE_READWRITE, &old_protect)) {
		g_hook_installed = false;
		return false;
	}

	// Write the JMP instruction
	memcpy((void*)g_target_func_addr, jmp_code, 5);

	// Restore protection
	VirtualProtect((LPVOID)g_target_func_addr, 5, old_protect, &old_protect);

	return true;
}

void UninstallGetDbStrHook() {
	if (!g_hook_installed) {
		return;
	}

	if (g_target_func_addr != 0) {
		DWORD old_protect;
		VirtualProtect((LPVOID)g_target_func_addr, 5, PAGE_EXECUTE_READWRITE, &old_protect);
		memcpy((void*)g_target_func_addr, g_original_bytes, 5);
		VirtualProtect((LPVOID)g_target_func_addr, 5, old_protect, &old_protect);
	}

	g_hook_installed = false;
}

bool InstallCmdHook() {
	if (g_cmd_hook_installed.exchange(true)) {
		return true;
	}

	// Get the base address of eqgame.exe
	HMODULE eqgame = GetModuleHandleA("eqgame.exe");
	if (!eqgame) {
		eqgame = GetModuleHandleA(NULL);
	}
	if (!eqgame) {
		g_cmd_hook_installed = false;
		return false;
	}

	uintptr_t base_addr = (uintptr_t)eqgame;

	// Set up the dsp_chat function pointer for displaying messages
	g_dsp_chat = (DspChatFunc)(base_addr + CEverQuest_dsp_chat_Offset);

	// Calculate the target function address for InterpretCmd
	g_cmd_func_addr = base_addr + CEverQuest_InterpretCmd_Offset;

	// Save the original function pointer
	g_original_interpret_cmd = (InterpretCmdFunc)g_cmd_func_addr;

	// Save original bytes for unhooking
	memcpy(g_cmd_original_bytes, (void*)g_cmd_func_addr, 5);

	// Calculate the relative jump offset
	uintptr_t hook_func_addr = (uintptr_t)&HookedInterpretCmd;
	int32_t rel_offset = (int32_t)(hook_func_addr - (g_cmd_func_addr + 5));

	// Build the JMP instruction
	BYTE jmp_code[5];
	jmp_code[0] = 0xE9; // JMP rel32
	memcpy(&jmp_code[1], &rel_offset, sizeof(rel_offset));

	// Make the target memory writable
	DWORD old_protect;
	if (!VirtualProtect((LPVOID)g_cmd_func_addr, 5, PAGE_EXECUTE_READWRITE, &old_protect)) {
		g_cmd_hook_installed = false;
		return false;
	}

	// Write the JMP instruction
	memcpy((void*)g_cmd_func_addr, jmp_code, 5);

	// Restore protection
	VirtualProtect((LPVOID)g_cmd_func_addr, 5, old_protect, &old_protect);

	return true;
}

void UninstallCmdHook() {
	if (!g_cmd_hook_installed) {
		return;
	}

	if (g_cmd_func_addr != 0) {
		DWORD old_protect;
		VirtualProtect((LPVOID)g_cmd_func_addr, 5, PAGE_EXECUTE_READWRITE, &old_protect);
		memcpy((void*)g_cmd_func_addr, g_cmd_original_bytes, 5);
		VirtualProtect((LPVOID)g_cmd_func_addr, 5, old_protect, &old_protect);
	}

	g_cmd_hook_installed = false;
}

// ============================================================================
// DLL Initialization
// ============================================================================

void InitializeHooks() {
	if (g_initialized.exchange(true)) {
		return;
	}

	// Connect to the server proxy
	g_tcp_client.Connect();

	// Install the hook on CDBStr::GetString
	if (!InstallGetDbStrHook()) {
		g_initialized = false;
		return;
	}

	// Install the chat command hook on CEverQuest::InterpretCmd
	if (!InstallCmdHook()) {
		// Non-fatal - commands won't work but string lookups will
	}
}

// Thread function for delayed initialization
DWORD WINAPI InitThreadProc(LPVOID lpParam) {
	// Give the process time to fully initialize
	Sleep(5000);
	InitializeHooks();
	return 0;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
	switch (ul_reason_for_call) {
		case DLL_PROCESS_ATTACH:
		{
			DisableThreadLibraryCalls(hModule);

			// Load the real dinput8.dll from SysWOW64 so DirectInput still works
			LoadRealDinput8();

			// Initialize our hooks in a separate thread to avoid deadlocks
			HANDLE hThread = CreateThread(
				NULL, 0, InitThreadProc, NULL, 0, NULL
			);
			if (hThread) {
				CloseHandle(hThread);
			}
			break;
		}
		case DLL_PROCESS_DETACH:
		{
			UninstallGetDbStrHook();
			UninstallCmdHook();
			g_tcp_client.Disconnect();
			if (g_real_dinput8) {
				FreeLibrary(g_real_dinput8);
				g_real_dinput8 = NULL;
			}
			break;
		}
		case DLL_THREAD_ATTACH:
		case DLL_THREAD_DETACH:
			break;
	}
	return TRUE;
}

