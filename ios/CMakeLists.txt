# StackS iOS app. Generated into an Xcode project by scripts/build-ios.sh:
#   cmake -S ios -B build/ios -G Xcode -DCMAKE_SYSTEM_NAME=iOS -DCMAKE_OSX_DEPLOYMENT_TARGET=15.0
cmake_minimum_required(VERSION 3.24)
project(StackS LANGUAGES C OBJC)

set(NATIVEAOT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/nativeaot" CACHE PATH "Folder with stacks_game.a and the NativeAOT runtime libraries")
set(DEV_TEAM "" CACHE STRING "Apple development team id for code signing")

add_executable(StackS MACOSX_BUNDLE ${CMAKE_CURRENT_SOURCE_DIR}/../native/stacks_platform.m)
target_include_directories(StackS PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/../native)
target_compile_options(StackS PRIVATE -fobjc-arc)

file(GLOB AOT_LIBS "${NATIVEAOT_DIR}/*.a" "${NATIVEAOT_DIR}/*.o")
if(NOT AOT_LIBS)
  message(FATAL_ERROR "No NativeAOT libraries in ${NATIVEAOT_DIR}. Run scripts/build-ios.sh first.")
endif()
target_link_libraries(StackS PRIVATE ${AOT_LIBS}
  "-framework Foundation" "-framework UIKit" "-framework Metal" "-framework MetalKit" "-framework QuartzCore"
  "-framework Security" "-lc++" "-lz")

set_target_properties(StackS PROPERTIES
  MACOSX_BUNDLE_INFO_PLIST "${CMAKE_CURRENT_SOURCE_DIR}/Info.plist.in"
  MACOSX_BUNDLE_GUI_IDENTIFIER "dev.stacks.game"
  MACOSX_BUNDLE_BUNDLE_NAME "StackS"
  XCODE_ATTRIBUTE_PRODUCT_BUNDLE_IDENTIFIER "dev.stacks.game"
  XCODE_ATTRIBUTE_TARGETED_DEVICE_FAMILY "1,2"
  XCODE_ATTRIBUTE_DEVELOPMENT_TEAM "${DEV_TEAM}"
  XCODE_ATTRIBUTE_CODE_SIGN_STYLE "Automatic"
  XCODE_ATTRIBUTE_ENABLE_BITCODE "NO"
  XCODE_ATTRIBUTE_DEAD_CODE_STRIPPING "YES")
