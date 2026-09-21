#pragma once

#define JOLIECAT_API extern "C" __declspec(dllexport)

JOLIECAT_API void Engine_Initialize();
JOLIECAT_API void Engine_Shutdown();
JOLIECAT_API int Engine_GetVersion();
