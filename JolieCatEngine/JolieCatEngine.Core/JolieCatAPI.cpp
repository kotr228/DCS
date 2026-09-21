#include "pch.h"
#include "JolieCatAPI.h"

JOLIECAT_API void Engine_Initialize()
{
}

JOLIECAT_API void Engine_Shutdown()
{
}

JOLIECAT_API int Engine_GetVersion()
{
    return 100;
}

JOLIECAT_API void Engine_InitializeViewport(void* hwnd, int width, int height)
{
    HWND viewportWindow = static_cast<HWND>(hwnd);
    if (viewportWindow == nullptr)
    {
        return;
    }

    HDC deviceContext = GetDC(viewportWindow);
    if (deviceContext == nullptr)
    {
        return;
    }

    RECT clientRect{ 0, 0, width, height };
    HBRUSH clearBrush = CreateSolidBrush(RGB(30, 28, 26));
    FillRect(deviceContext, &clientRect, clearBrush);
    DeleteObject(clearBrush);
    ReleaseDC(viewportWindow, deviceContext);
}
