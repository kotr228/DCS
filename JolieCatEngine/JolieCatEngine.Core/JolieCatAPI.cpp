#include "pch.h"
#include "JolieCatAPI.h"

#include <atomic>
#include <chrono>
#include <cstring>
#include <filesystem>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

namespace
{
    struct Transform
    {
        float PositionX = 0.0f, PositionY = 0.0f, PositionZ = 0.0f;
        float RotationX = 0.0f, RotationY = 0.0f, RotationZ = 0.0f;
        float ScaleX = 1.0f, ScaleY = 1.0f, ScaleZ = 1.0f;
    };

    std::atomic<HWND> g_viewportHwnd{ nullptr };
    std::atomic<int> g_viewportWidth{ 0 };
    std::atomic<int> g_viewportHeight{ 0 };

    std::atomic<bool> g_renderLoopRunning{ false };
    std::thread g_renderThread;

    std::atomic<uint32_t> g_nextEntityId{ 1 };
    std::mutex g_entityMutex;
    std::unordered_map<uint32_t, Transform> g_entityTransforms;

    float s_CameraX = 0.0f;
    float s_CameraY = 0.0f;
    float s_CameraZoom = 1.0f;
    POINT s_LastMousePos = { 0, 0 };
    bool s_IsPanning = false;

    void RenderLoopMain()
    {
        using namespace std::chrono;

        while (g_renderLoopRunning.load(std::memory_order_relaxed))
        {
            HWND hwnd = g_viewportHwnd.load(std::memory_order_relaxed);
            if (hwnd != nullptr)
            {
                int width = g_viewportWidth.load(std::memory_order_relaxed);
                int height = g_viewportHeight.load(std::memory_order_relaxed);

                bool isButtonDown = (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0
                    || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;

                POINT currentPos{};
                GetCursorPos(&currentPos);
                ScreenToClient(hwnd, &currentPos);

                if (isButtonDown)
                {
                    if (!s_IsPanning)
                    {
                        s_IsPanning = true;
                    }

                    float deltaX = static_cast<float>(currentPos.x - s_LastMousePos.x);
                    float deltaY = static_cast<float>(currentPos.y - s_LastMousePos.y);

                    s_CameraX -= deltaX / s_CameraZoom;
                    s_CameraY -= deltaY / s_CameraZoom;
                }
                else
                {
                    s_IsPanning = false;
                }

                s_LastMousePos = currentPos;

                HDC deviceContext = GetDC(hwnd);
                if (deviceContext != nullptr)
                {
                    RECT clientRect{ 0, 0, width, height };
                    HBRUSH frameBrush = CreateSolidBrush(RGB(32, 32, 32));
                    FillRect(deviceContext, &clientRect, frameBrush);
                    DeleteObject(frameBrush);

                    {
                        std::lock_guard<std::mutex> lock(g_entityMutex);

                        HBRUSH entityBrush = CreateSolidBrush(RGB(255, 255, 255));
                        HGDIOBJ previousBrush = SelectObject(deviceContext, entityBrush);

                        constexpr int halfSize = 8;
                        constexpr float pixelsPerUnit = 50.0f;
                        float screenCenterX = static_cast<float>(width) / 2.0f;
                        float screenCenterY = static_cast<float>(height) / 2.0f;
                        float effectivePixelsPerUnit = pixelsPerUnit * s_CameraZoom;

                        for (const auto& entry : g_entityTransforms)
                        {
                            const Transform& transform = entry.second;
                            float relativeX = transform.PositionX - s_CameraX;
                            float relativeY = transform.PositionY - s_CameraY;
                            int centerX = static_cast<int>(screenCenterX + relativeX * effectivePixelsPerUnit);
                            int centerY = static_cast<int>(screenCenterY + relativeY * effectivePixelsPerUnit);
                            Rectangle(deviceContext, centerX - halfSize, centerY - halfSize, centerX + halfSize, centerY + halfSize);
                        }

                        SelectObject(deviceContext, previousBrush);
                        DeleteObject(entityBrush);
                    }

                    ReleaseDC(hwnd, deviceContext);
                }
            }

            std::this_thread::sleep_for(milliseconds(16));
        }
    }

    std::vector<std::string> ScanAssetsFolder()
    {
        std::vector<std::string> assetNames;

        wchar_t exePath[MAX_PATH]{};
        GetModuleFileNameW(nullptr, exePath, MAX_PATH);

        std::filesystem::path assetsDir = std::filesystem::path(exePath).parent_path() / L"Assets";

        std::error_code errorCode;
        if (!std::filesystem::exists(assetsDir, errorCode) || !std::filesystem::is_directory(assetsDir, errorCode))
        {
            return assetNames;
        }

        for (const auto& entry : std::filesystem::directory_iterator(assetsDir, errorCode))
        {
            if (entry.is_regular_file())
            {
                assetNames.push_back(entry.path().filename().string());
            }
        }

        return assetNames;
    }
}

JOLIECAT_API void Engine_Initialize()
{
}

JOLIECAT_API void Engine_Shutdown()
{
    g_renderLoopRunning.store(false, std::memory_order_relaxed);
    if (g_renderThread.joinable())
    {
        g_renderThread.join();
    }
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

    g_viewportHwnd.store(viewportWindow, std::memory_order_relaxed);
    g_viewportWidth.store(width, std::memory_order_relaxed);
    g_viewportHeight.store(height, std::memory_order_relaxed);

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

JOLIECAT_API void Engine_StartRenderLoop()
{
    bool expected = false;
    if (!g_renderLoopRunning.compare_exchange_strong(expected, true))
    {
        return;
    }

    g_renderThread = std::thread(RenderLoopMain);
}

JOLIECAT_API uint32_t Engine_CreateEntity()
{
    uint32_t entityId = g_nextEntityId.fetch_add(1, std::memory_order_relaxed);

    std::lock_guard<std::mutex> lock(g_entityMutex);
    g_entityTransforms[entityId] = Transform{};

    return entityId;
}

JOLIECAT_API void Engine_SetTransform(
    uint32_t entityId,
    float pX, float pY, float pZ,
    float rX, float rY, float rZ,
    float sX, float sY, float sZ)
{
    std::lock_guard<std::mutex> lock(g_entityMutex);
    auto it = g_entityTransforms.find(entityId);
    if (it == g_entityTransforms.end())
    {
        return;
    }

    Transform& transform = it->second;
    transform.PositionX = pX;
    transform.PositionY = pY;
    transform.PositionZ = pZ;
    transform.RotationX = rX;
    transform.RotationY = rY;
    transform.RotationZ = rZ;
    transform.ScaleX = sX;
    transform.ScaleY = sY;
    transform.ScaleZ = sZ;
}

JOLIECAT_API void Engine_ResizeViewport(int width, int height)
{
    g_viewportWidth.store(width, std::memory_order_relaxed);
    g_viewportHeight.store(height, std::memory_order_relaxed);
}

JOLIECAT_API int Engine_GetAssets(char* outBuffer, int maxLength)
{
    if (outBuffer == nullptr || maxLength <= 0)
    {
        return 0;
    }

    std::vector<std::string> assetNames = ScanAssetsFolder();
    if (assetNames.empty())
    {
        assetNames = { "player.gltf", "level.scene", "click.wav" };
    }

    std::string joined;
    for (size_t i = 0; i < assetNames.size(); ++i)
    {
        if (i > 0)
        {
            joined += '|';
        }
        joined += assetNames[i];
    }

    int copyLength = static_cast<int>(joined.size());
    if (copyLength > maxLength - 1)
    {
        copyLength = maxLength - 1;
    }

    if (copyLength > 0)
    {
        std::memcpy(outBuffer, joined.data(), copyLength);
    }
    outBuffer[copyLength] = '\0';

    return copyLength;
}

JOLIECAT_API void Engine_SetEditorCamera(float x, float y, float zoom)
{
    s_CameraX = x;
    s_CameraY = y;
    s_CameraZoom = zoom;
}

JOLIECAT_API void Engine_ZoomCamera(float factor)
{
    float newZoom = s_CameraZoom * factor;

    if (newZoom < 0.1f)
    {
        newZoom = 0.1f;
    }
    else if (newZoom > 10.0f)
    {
        newZoom = 10.0f;
    }

    s_CameraZoom = newZoom;
}
