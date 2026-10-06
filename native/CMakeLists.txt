# StackS native platform layer.
# Desktop:  cmake -S native -B build/native -DCMAKE_BUILD_TYPE=Release && cmake --build build/native --config Release
# (Android and iOS build this file through android/ and ios/; see docs.)
cmake_minimum_required(VERSION 3.20)
project(stacks_platform C)
set(CMAKE_C_STANDARD 11)

option(SP_DESKTOP "Desktop build: shared library entered from C# via sp_run" ON)
option(SP_ANDROID_APP "Android build: shared library loaded by NativeActivity, entered via sokol_main" OFF)
set(STACKS_GAME_LIB "" CACHE FILEPATH "Android: path to the NativeAOT game library (libstacks_game.so)")

if(APPLE)
  enable_language(OBJC)
  set(SRC stacks_platform.m)
else()
  set(SRC stacks_platform.c)
endif()

if(SP_ANDROID_APP)
  # NativeActivity loads libstacks_platform.so; it calls stacks_configure() in libstacks_game.so.
  add_library(stacks_platform SHARED ${SRC})
  add_library(stacks_game SHARED IMPORTED)
  set_target_properties(stacks_game PROPERTIES IMPORTED_LOCATION ${STACKS_GAME_LIB} IMPORTED_NO_SONAME TRUE)
  target_link_libraries(stacks_platform PRIVATE stacks_game)
elseif(SP_DESKTOP)
  add_library(stacks_platform SHARED ${SRC})
  target_compile_definitions(stacks_platform PRIVATE SP_NO_ENTRY)
else()
  add_library(stacks_platform STATIC ${SRC})
endif()
set_target_properties(stacks_platform PROPERTIES C_VISIBILITY_PRESET hidden OBJC_VISIBILITY_PRESET hidden)
target_include_directories(stacks_platform PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})

if(WIN32)
  target_link_libraries(stacks_platform PRIVATE d3d11 dxgi)
elseif(APPLE)
  target_compile_options(stacks_platform PRIVATE -fobjc-arc)
  if(CMAKE_SYSTEM_NAME STREQUAL "iOS")
    target_link_libraries(stacks_platform PRIVATE "-framework Foundation" "-framework UIKit" "-framework Metal" "-framework MetalKit" "-framework QuartzCore")
  else()
    target_link_libraries(stacks_platform PRIVATE "-framework Cocoa" "-framework QuartzCore" "-framework Metal" "-framework MetalKit")
  endif()
elseif(ANDROID)
  target_link_libraries(stacks_platform PRIVATE GLESv3 EGL android log)
else()
  find_package(X11 REQUIRED)
  find_package(OpenGL REQUIRED)
  target_link_libraries(stacks_platform PRIVATE X11::X11 X11::Xi X11::Xcursor OpenGL::GL dl pthread m)
endif()

# Put the desktop library where StackS.Desktop.csproj looks for it.
set_target_properties(stacks_platform PROPERTIES
  LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib
  ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)
