#pragma once

#include <cstdint>

#define JOLIECAT_API extern "C" __declspec(dllexport)

JOLIECAT_API void Engine_Initialize();
JOLIECAT_API void Engine_Shutdown();
JOLIECAT_API int Engine_GetVersion();
JOLIECAT_API void Engine_InitializeViewport(void* hwnd, int width, int height);
JOLIECAT_API void Engine_StartRenderLoop();
JOLIECAT_API uint32_t Engine_CreateEntity();
JOLIECAT_API void Engine_SetTransform(
    uint32_t entityId,
    float pX, float pY, float pZ,
    float rX, float rY, float rZ,
    float sX, float sY, float sZ);
JOLIECAT_API void Engine_ResizeViewport(int width, int height);
JOLIECAT_API int Engine_GetAssets(char* outBuffer, int maxLength);
JOLIECAT_API void Engine_SetEditorCamera(float x, float y, float zoom);
