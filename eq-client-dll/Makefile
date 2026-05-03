# Makefile for building the EQ Client Hook DLLs
# Requires MinGW-w64 cross-compiler on Linux, or MSVC on Windows
#
# Targets:
#   dinput8.dll       - Proxy DLL (loads real dinput8 + installs hooks)
#   godsofnorrath.dll - Injection DLL (injected by patcher into eqgame.exe)
#
# IMPORTANT: eqgame.exe is a 32-bit executable, so we MUST use the 32-bit
# (i686) MinGW compiler. A 64-bit DLL cannot be loaded by a 32-bit process.

CXX = i686-w64-mingw32-g++
CXXFLAGS = -O2 -Wall -shared -static-libgcc -static-libstdc++ -DNOMINMAX
LDFLAGS = -lws2_32 -lwininet -Wl,--enable-stdcall-fixup

# dinput8.dll - proxy DLL with .def file for export forwarding
DINPUT8_TARGET = dinput8.dll
DINPUT8_SRC = dllmain.cpp
DINPUT8_LDFLAGS = $(LDFLAGS) -Wl,dinput8.def

# godsofnorrath.dll - injection DLL (no .def file needed, no proxy)
GODSOFNORRATH_TARGET = godsofnorrath.dll
GODSOFNORRATH_SRC = godsofnorrath.cpp

all: $(DINPUT8_TARGET) $(GODSOFNORRATH_TARGET)

$(DINPUT8_TARGET): $(DINPUT8_SRC) dinput8.def
	$(CXX) $(CXXFLAGS) -o $(DINPUT8_TARGET) $(DINPUT8_SRC) $(DINPUT8_LDFLAGS)

$(GODSOFNORRATH_TARGET): $(GODSOFNORRATH_SRC)
	$(CXX) $(CXXFLAGS) -o $(GODSOFNORRATH_TARGET) $(GODSOFNORRATH_SRC) $(LDFLAGS)

clean:
	rm -f $(DINPUT8_TARGET) $(GODSOFNORRATH_TARGET)

# For building on Windows with MSVC
msvc:
	cl /O2 /LD /Fe:$(DINPUT8_TARGET) $(DINPUT8_SRC) ws2_32.lib /DEF:dinput8.def
	cl /O2 /LD /Fe:$(GODSOFNORRATH_TARGET) $(GODSOFNORRATH_SRC) ws2_32.lib

.PHONY: all clean msvc
