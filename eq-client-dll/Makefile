# Makefile for building the EQ Client dinput8.dll Proxy
# Requires MinGW-w64 cross-compiler on Linux, or MSVC on Windows
#
# This builds a dinput8.dll that acts as a proxy - it forwards all 5 real
# dinput8 exports to the system DLL while installing our hooks for live
# db_str lookups and chat commands. No injection needed - eqgame.exe loads
# this automatically because Windows checks the app directory first.

CXX = x86_64-w64-mingw32-g++
CXXFLAGS = -O2 -Wall -shared -static-libgcc -static-libstdc++ -DNOMINMAX
LDFLAGS = -lws2_32 -lwininet -Wl,dinput8.def
TARGET = dinput8.dll
SRC = dllmain.cpp

all: $(TARGET)

$(TARGET): $(SRC) dinput8.def
	$(CXX) $(CXXFLAGS) -o $(TARGET) $(SRC) $(LDFLAGS)

clean:
	rm -f $(TARGET)

# For building on Windows with MSVC
msvc:
	cl /O2 /LD /Fe:$(TARGET) $(SRC) ws2_32.lib /DEF:dinput8.def

.PHONY: all clean msvc

