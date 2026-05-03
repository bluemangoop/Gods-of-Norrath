// godsofnorrath.cpp : EQ Client Hook DLL - Injected by patcher
//
// This DLL is injected into eqgame.exe by the EQEmu Patcher after the game
// launches. It installs hooks for:
//   - CDBStr::GetString - redirects string lookups to server-side DbStrProxy via TCP
//   - CEverQuest::InterpretCmd - intercepts chat commands like #helloworld and #reload
//
// Offsets from MacroQuest for RoF2 (May 10 2013 build):
//   CEverQuest__InterpretCmd_x = 0x51FCE0
//   CEverQuest__dsp_chat_x     = 0x51F1A0
//   CDBStr__GetString_x        = 0x4866C0
//   pinstCDBStr_x              = 0xD1F380
//   pinstLocalPlayer_x         = 0xDD2630
//   pinstCEverQuest_x          = 0xE67CCC

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
#include <fstream>
#include <sstream>
#include <iomanip>
#include <ctime>

// ============================================================================
// Debug Logging
// ============================================================================

void DebugLog(const char* format, ...) {
	char buffer[4096];
	va_list args;
	va_start(args, format);
	vsnprintf(buffer, sizeof(buffer), format, args);
	va_end(args);

	SYSTEMTIME st;
	GetLocalTime(&st);
	char timestamp[64];
	snprintf(timestamp, sizeof(timestamp), "[%02d:%02d:%02d.%03d]",
		st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);

	FILE* f = fopen("eqhook_debug.log", "a");
	if (f) {
		fprintf(f, "%s %s\n", timestamp, buffer);
		fclose(f);
	}
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
			DebugLog("DbStrTcpClient::Connect: socket() failed with error %lu", WSAGetLastError());
			return false;
		}

		int timeout = 3000;
		setsockopt(m_sock, SOL_SOCKET, SO_RCVTIMEO, (const char*)&timeout, sizeof(timeout));
		setsockopt(m_sock, SOL_SOCKET, SO_SNDTIMEO, (const char*)&timeout, sizeof(timeout));

		sockaddr_in server_addr;
		server_addr.sin_family = AF_INET;
		server_addr.sin_port = htons(PROXY_PORT);
		inet_pton(AF_INET, PROXY_HOST, &server_addr.sin_addr);

		if (connect(m_sock, (sockaddr*)&server_addr, sizeof(server_addr)) == SOCKET_ERROR) {
			DebugLog("DbStrTcpClient::Connect: connect() to %s:%d failed with error %lu",
				PROXY_HOST, PROXY_PORT, WSAGetLastError());
			closesocket(m_sock);
			m_sock = INVALID_SOCKET;
			return false;
		}

		m_connected = true;
		DebugLog("DbStrTcpClient::Connect: Connected to %s:%d", PROXY_HOST, PROXY_PORT);
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
			DebugLog("DbStrTcpClient::SendRequest: send() failed with error %lu", WSAGetLastError());
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
			DebugLog("DbStrTcpClient::ReceiveResponse: recv() failed with error %lu", WSAGetLastError());
			Disconnect();
			return false;
		}

		buffer[received] = '\0';
		response = buffer;

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

		for (int attempt = 0; attempt < 2; attempt++) {
			if (!SendRequest(request)) {
				continue;
			}

			if (!ReceiveResponse(response)) {
				continue;
			}

			if (response.substr(0, 3) == "OK|") {
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

BYTE g_original_bytes[5] = {0};
BYTE g_cmd_original_bytes[5] = {0};
uintptr_t g_target_func_addr = 0;
uintptr_t g_cmd_func_addr = 0;

void* g_get_db_str_trampoline = nullptr;
void* g_interpret_cmd_trampoline = nullptr;

// ============================================================================
// Trampoline Helper
// ============================================================================

void* CreateTrampoline(BYTE* original_bytes, uintptr_t original_addr) {
	void* trampoline = VirtualAlloc(NULL, 32, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
	if (!trampoline) {
		DebugLog("CreateTrampoline: VirtualAlloc failed with error %lu", GetLastError());
		return nullptr;
	}

	BYTE* code = (BYTE*)trampoline;
	size_t offset = 0;

	memcpy(code + offset, original_bytes, 5);
	offset += 5;

	uintptr_t return_addr = original_addr + 5;
	code[offset++] = 0xFF;
	code[offset++] = 0x25;
	code[offset++] = 0x00;
	code[offset++] = 0x00;
	code[offset++] = 0x00;
	code[offset++] = 0x00;
	memcpy(code + offset, &return_addr, sizeof(return_addr));
	offset += 4;

	DebugLog("CreateTrampoline: Created at 0x%p, %zu bytes", trampoline, offset);
	return trampoline;
}

// ============================================================================
// CDBStr::GetString Hook
// ============================================================================

typedef const char* (__thiscall* GetDbStrFunc)(void* this_ptr, int id, int type, bool* found);
GetDbStrFunc g_original_get_db_str = nullptr;

#ifdef __MINGW32__
__thread char g_thread_local_buffer[4096];
#else
__declspec(thread) char g_thread_local_buffer[4096];
#endif

const char* __fastcall HookedGetDbStr(void* this_ptr, int /*unused_edx*/, int id, int type, bool* found) {
	std::string cached = g_cache.Get(id, type);
	if (!cached.empty()) {
		if (found) *found = true;
		strncpy_s(g_thread_local_buffer, sizeof(g_thread_local_buffer), cached.c_str(), _TRUNCATE);
		return g_thread_local_buffer;
	}

	std::string server_result = g_tcp_client.LookupString(id, type);
	if (!server_result.empty()) {
		g_cache.Set(id, type, server_result);
		if (found) *found = true;
		strncpy_s(g_thread_local_buffer, sizeof(g_thread_local_buffer), server_result.c_str(), _TRUNCATE);
		return g_thread_local_buffer;
	}

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
// CEverQuest::InterpretCmd Hook
// ============================================================================

typedef void (__thiscall* DspChatFunc)(void* this_ptr, const char* text, int color);
DspChatFunc g_dsp_chat = nullptr;

typedef void (__thiscall* InterpretCmdFunc)(void* this_ptr, void* pChar, const char* szFullLine);
InterpretCmdFunc g_original_interpret_cmd = nullptr;

const uintptr_t pinstLocalPlayer_Offset = 0xDD2630;

std::string GetPlayerName() {
	HMODULE eqgame = GetModuleHandleA("eqgame.exe");
	if (!eqgame) return "Unknown";

	uintptr_t base = (uintptr_t)eqgame;
	uintptr_t pLocalPlayerPtr = base + pinstLocalPlayer_Offset;
	void** pLocalPlayer = (void**)pLocalPlayerPtr;
	if (!pLocalPlayer || !*pLocalPlayer) return "Unknown";

	char* name = (char*)(*pLocalPlayer) + 0x14;
	return std::string(name);
}

void DisplayChatMessage(const char* message) {
	if (!g_dsp_chat) {
		DebugLog("DisplayChatMessage: g_dsp_chat is NULL, can't display: %s", message);
		return;
	}

	HMODULE eqgame = GetModuleHandleA("eqgame.exe");
	if (!eqgame) {
		DebugLog("DisplayChatMessage: eqgame.exe module not found");
		return;
	}

	uintptr_t base = (uintptr_t)eqgame;
	uintptr_t pEverQuestPtr = base + 0xE67CCC;
	void** pEverQuest = (void**)pEverQuestPtr;
	if (!pEverQuest || !*pEverQuest) {
		DebugLog("DisplayChatMessage: pEverQuest is NULL (offset 0xE67CCC)");
		return;
	}

	DebugLog("DisplayChatMessage: Calling dsp_chat with: %s", message);
	g_dsp_chat(*pEverQuest, message, 15);
}

bool DownloadFileToDisk(const char* url, const char* localPath) {
	bool success = false;
	HINTERNET hInternet = InternetOpenA(
		"DbStrProxy/1.0",
		INTERNET_OPEN_TYPE_PRECONFIG,
		NULL, NULL, 0
	);

	if (!hInternet) {
		DebugLog("DownloadFileToDisk: InternetOpenA failed with error %lu", GetLastError());
		return false;
	}

	HINTERNET hUrl = InternetOpenUrlA(
		hInternet,
		url,
		NULL, 0,
		INTERNET_FLAG_RELOAD | INTERNET_FLAG_NO_CACHE_WRITE,
		0
	);

	if (!hUrl) {
		DebugLog("DownloadFileToDisk: InternetOpenUrlA failed for %s with error %lu", url, GetLastError());
		InternetCloseHandle(hInternet);
		return false;
	}

	HANDLE hFile = CreateFileA(
		localPath,
		GENERIC_WRITE,
		0,
		NULL,
		CREATE_ALWAYS,
		FILE_ATTRIBUTE_NORMAL,
		NULL
	);

	if (hFile == INVALID_HANDLE_VALUE) {
		DebugLog("DownloadFileToDisk: CreateFileA failed for %s with error %lu", localPath, GetLastError());
		InternetCloseHandle(hUrl);
		InternetCloseHandle(hInternet);
		return false;
	}

	char buffer[8192];
	DWORD bytesRead;
	DWORD totalBytes = 0;

	while (InternetReadFile(hUrl, buffer, sizeof(buffer), &bytesRead) && bytesRead > 0) {
		DWORD bytesWritten;
		WriteFile(hFile, buffer, bytesRead, &bytesWritten, NULL);
		totalBytes += bytesWritten;
	}

	CloseHandle(hFile);
	InternetCloseHandle(hUrl);
	InternetCloseHandle(hInternet);

	DebugLog("DownloadFileToDisk: Downloaded %lu bytes to %s", totalBytes, localPath);

	if (totalBytes > 0) {
		success = true;
	}

	return success;
}

void __fastcall HookedInterpretCmd(void* this_ptr, int /*unused_edx*/, void* pChar, const char* szFullLine) {
	if (szFullLine) {
		DebugLog("HookedInterpretCmd: Received command: '%s'", szFullLine);

		if (strcmp(szFullLine, "#helloworld") == 0) {
			std::string playerName = GetPlayerName();
			std::string msg = "Hello, " + playerName + ".";
			DebugLog("HookedInterpretCmd: #helloworld triggered for player '%s'", playerName.c_str());
			DisplayChatMessage(msg.c_str());
			return;
		}

		if (strcmp(szFullLine, "#reload aa_data global") == 0) {
			DebugLog("HookedInterpretCmd: #reload aa_data global triggered");
			DisplayChatMessage("Reloading dbstr_us.txt from server...");

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

			if (DownloadFileToDisk(DBSTR_DOWNLOAD_URL, dbstrPath.c_str())) {
				g_cache.Clear();
				g_tcp_client.SendRequest("RELOAD");
				DisplayChatMessage("dbstr_us.txt reloaded successfully from server.");
			} else {
				DisplayChatMessage("ERROR: Failed to download dbstr_us.txt from server.");
			}

			return;
		}
	}

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
		DebugLog("InstallGetDbStrHook: Already installed");
		return true;
	}

	HMODULE eqgame = GetModuleHandleA("eqgame.exe");
	if (!eqgame) {
		eqgame = GetModuleHandleA(NULL);
	}
	if (!eqgame) {
		DebugLog("InstallGetDbStrHook: FAILED - cannot get eqgame.exe module handle");
		g_hook_installed = false;
		return false;
	}

	uintptr_t base_addr = (uintptr_t)eqgame;
	g_target_func_addr = base_addr + CDBStr_GetString_Offset;

	DebugLog("InstallGetDbStrHook: eqgame base = 0x%p, target = 0x%p (offset 0x%X)",
		(void*)base_addr, (void*)g_target_func_addr, CDBStr_GetString_Offset);

	BYTE first_bytes[10];
	memcpy(first_bytes, (void*)g_target_func_addr, 5);
	DebugLog("InstallGetDbStrHook: First 5 bytes at target: %02X %02X %02X %02X %02X",
		first_bytes[0], first_bytes[1], first_bytes[2], first_bytes[3], first_bytes[4]);

	memcpy(g_original_bytes, (void*)g_target_func_addr, 5);

	g_get_db_str_trampoline = CreateTrampoline(g_original_bytes, g_target_func_addr);
	if (g_get_db_str_trampoline) {
		g_original_get_db_str = (GetDbStrFunc)g_get_db_str_trampoline;
		DebugLog("InstallGetDbStrHook: Trampoline at 0x%p", g_get_db_str_trampoline);
	} else {
		g_original_get_db_str = (GetDbStrFunc)g_target_func_addr;
		DebugLog("InstallGetDbStrHook: WARNING - no trampoline, using direct address");
	}

	uintptr_t hook_func_addr = (uintptr_t)&HookedGetDbStr;
	DebugLog("InstallGetDbStrHook: Hook function at 0x%p", (void*)hook_func_addr);

	BYTE jmp_code[10];
	jmp_code[0] = 0xFF;
	jmp_code[1] = 0x25;
	jmp_code[2] = 0x00;
	jmp_code[3] = 0x00;
	jmp_code[4] = 0x00;
	jmp_code[5] = 0x00;
	memcpy(&jmp_code[6], &hook_func_addr, sizeof(hook_func_addr));

	DWORD old_protect;
	if (!VirtualProtect((LPVOID)g_target_func_addr, 10, PAGE_EXECUTE_READWRITE, &old_protect)) {
		DWORD err = GetLastError();
		DebugLog("InstallGetDbStrHook: VirtualProtect failed with error %lu", err);
		g_hook_installed = false;
		return false;
	}

	memcpy((void*)g_target_func_addr, jmp_code, 10);
	VirtualProtect((LPVOID)g_target_func_addr, 10, old_protect, &old_protect);

	memcpy(first_bytes, (void*)g_target_func_addr, 10);
	DebugLog("InstallGetDbStrHook: SUCCESS - bytes after hook: %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X",
		first_bytes[0], first_bytes[1], first_bytes[2], first_bytes[3], first_bytes[4],
		first_bytes[5], first_bytes[6], first_bytes[7], first_bytes[8], first_bytes[9]);

	return true;
}

void UninstallGetDbStrHook() {
	if (!g_hook_installed) return;

	if (g_target_func_addr != 0) {
		DWORD old_protect;
		VirtualProtect((LPVOID)g_target_func_addr, 10, PAGE_EXECUTE_READWRITE, &old_protect);
		memcpy((void*)g_target_func_addr, g_original_bytes, 5);
		VirtualProtect((LPVOID)g_target_func_addr, 10, old_protect, &old_protect);
		DebugLog("UninstallGetDbStrHook: Hook removed");
	}

	g_hook_installed = false;
}

bool InstallCmdHook() {
	if (g_cmd_hook_installed.exchange(true)) {
		DebugLog("InstallCmdHook: Already installed");
		return true;
	}

	HMODULE eqgame = GetModuleHandleA("eqgame.exe");
	if (!eqgame) {
		eqgame = GetModuleHandleA(NULL);
	}
	if (!eqgame) {
		DebugLog("InstallCmdHook: FAILED - cannot get eqgame.exe module handle");
		g_cmd_hook_installed = false;
		return false;
	}

	uintptr_t base_addr = (uintptr_t)eqgame;

	g_dsp_chat = (DspChatFunc)(base_addr + CEverQuest_dsp_chat_Offset);
	DebugLog("InstallCmdHook: dsp_chat at 0x%p", (void*)g_dsp_chat);

	g_cmd_func_addr = base_addr + CEverQuest_InterpretCmd_Offset;
	DebugLog("InstallCmdHook: InterpretCmd at 0x%p (offset 0x%X)",
		(void*)g_cmd_func_addr, CEverQuest_InterpretCmd_Offset);

	BYTE first_bytes[10];
	memcpy(first_bytes, (void*)g_cmd_func_addr, 5);
	DebugLog("InstallCmdHook: First 5 bytes at InterpretCmd: %02X %02X %02X %02X %02X",
		first_bytes[0], first_bytes[1], first_bytes[2], first_bytes[3], first_bytes[4]);

	memcpy(g_cmd_original_bytes, (void*)g_cmd_func_addr, 5);

	g_interpret_cmd_trampoline = CreateTrampoline(g_cmd_original_bytes, g_cmd_func_addr);
	if (g_interpret_cmd_trampoline) {
		g_original_interpret_cmd = (InterpretCmdFunc)g_interpret_cmd_trampoline;
		DebugLog("InstallCmdHook: Trampoline at 0x%p", g_interpret_cmd_trampoline);
	} else {
		g_original_interpret_cmd = (InterpretCmdFunc)g_cmd_func_addr;
		DebugLog("InstallCmdHook: WARNING - no trampoline, using direct address");
	}

	uintptr_t hook_func_addr = (uintptr_t)&HookedInterpretCmd;
	DebugLog("InstallCmdHook: Hook function at 0x%p", (void*)hook_func_addr);

	BYTE jmp_code[10];
	jmp_code[0] = 0xFF;
	jmp_code[1] = 0x25;
	jmp_code[2] = 0x00;
	jmp_code[3] = 0x00;
	jmp_code[4] = 0x00;
	jmp_code[5] = 0x00;
	memcpy(&jmp_code[6], &hook_func_addr, sizeof(hook_func_addr));

	DWORD old_protect;
	if (!VirtualProtect((LPVOID)g_cmd_func_addr, 10, PAGE_EXECUTE_READWRITE, &old_protect)) {
		DWORD err = GetLastError();
		DebugLog("InstallCmdHook: VirtualProtect failed with error %lu", err);
		g_cmd_hook_installed = false;
		return false;
	}

	memcpy((void*)g_cmd_func_addr, jmp_code, 10);
	VirtualProtect((LPVOID)g_cmd_func_addr, 10, old_protect, &old_protect);

	memcpy(first_bytes, (void*)g_cmd_func_addr, 10);
	DebugLog("InstallCmdHook: SUCCESS - bytes after hook: %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X",
		first_bytes[0], first_bytes[1], first_bytes[2], first_bytes[3], first_bytes[4],
		first_bytes[5], first_bytes[6], first_bytes[7], first_bytes[8], first_bytes[9]);

	return true;
}

void UninstallCmdHook() {
	if (!g_cmd_hook_installed) return;

	if (g_cmd_func_addr != 0) {
		DWORD old_protect;
		VirtualProtect((LPVOID)g_cmd_func_addr, 10, PAGE_EXECUTE_READWRITE, &old_protect);
		memcpy((void*)g_cmd_func_addr, g_cmd_original_bytes, 5);
		VirtualProtect((LPVOID)g_cmd_func_addr, 10, old_protect, &old_protect);
		DebugLog("UninstallCmdHook: Hook removed");
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

	DebugLog("InitializeHooks: Starting initialization...");

	DebugLog("InitializeHooks: Connecting to TCP proxy...");
	g_tcp_client.Connect();

	DebugLog("InitializeHooks: Installing CDBStr::GetString hook...");
	if (!InstallGetDbStrHook()) {
		DebugLog("InitializeHooks: FAILED to install CDBStr::GetString hook!");
		g_initialized = false;
		return;
	}
	DebugLog("InitializeHooks: CDBStr::GetString hook installed successfully");

	DebugLog("InitializeHooks: Installing InterpretCmd hook...");
	if (!InstallCmdHook()) {
		DebugLog("InitializeHooks: FAILED to install InterpretCmd hook (non-fatal)");
	} else {
		DebugLog("InitializeHooks: InterpretCmd hook installed successfully");
	}

	DebugLog("InitializeHooks: Initialization complete!");
}

DWORD WINAPI InitThreadProc(LPVOID lpParam) {
	DebugLog("InitThreadProc: Started, waiting 2 seconds...");
	Sleep(2000);
	DebugLog("InitThreadProc: Delay complete, initializing hooks...");
	InitializeHooks();
	return 0;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
	switch (ul_reason_for_call) {
		case DLL_PROCESS_ATTACH:
		{
			// Show a message box to visually confirm the DLL is loaded
			MessageBoxA(NULL, "GodsOfNorrath Hook DLL Loaded!", "Debug - GodsOfNorrath", MB_OK);

			// Clear log file on first load
			FILE* f = fopen("eqhook_debug.log", "w");
			if (f) {
				fprintf(f, "=== GodsOfNorrath Hook DLL Debug Log ===\n");
				fprintf(f, "DLL loaded at %p\n", hModule);
				fclose(f);
			}

			DebugLog("DLL_PROCESS_ATTACH: hModule=0x%p, lpReserved=0x%p", hModule, lpReserved);

			DisableThreadLibraryCalls(hModule);

			// Initialize our hooks in a separate thread to avoid deadlocks
			DebugLog("DLL_PROCESS_ATTACH: Creating init thread...");
			HANDLE hThread = CreateThread(
				NULL, 0, InitThreadProc, NULL, 0, NULL
			);
			if (hThread) {
				DebugLog("DLL_PROCESS_ATTACH: Init thread created successfully");
				CloseHandle(hThread);
			} else {
				DebugLog("DLL_PROCESS_ATTACH: CreateThread failed with error %lu", GetLastError());
			}
			break;
		}
		case DLL_PROCESS_DETACH:
		{
			DebugLog("DLL_PROCESS_DETACH: Cleaning up...");
			UninstallGetDbStrHook();
			UninstallCmdHook();
			g_tcp_client.Disconnect();
			DebugLog("DLL_PROCESS_DETACH: Cleanup complete");
			break;
		}
		case DLL_THREAD_ATTACH:
		case DLL_THREAD_DETACH:
			break;
	}
	return TRUE;
}
