#include "pch.h"
#include "JolieCatAPI.h"

#include <atomic>
#include <chrono>
#include <mutex>
#include <thread>
#include <unordered_map>

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

                auto elapsedMs = duration_cast<milliseconds>(steady_clock::now().time_since_epoch()).count();
                long long cycle = elapsedMs % 2000;
                long long triangle = cycle < 1000 ? cycle : (2000 - cycle);
                BYTE pulse = static_cast<BYTE>((triangle * 255) / 999);

                HDC deviceContext = GetDC(hwnd);
                if (deviceContext != nullptr)
                {
                    RECT clientRect{ 0, 0, width, height };
                    HBRUSH frameBrush = CreateSolidBrush(RGB(pulse, 28, 26));
                    FillRect(deviceContext, &clientRect, frameBrush);
                    DeleteObject(frameBrush);
                    ReleaseDC(hwnd, deviceContext);
                }
            }

            std::this_thread::sleep_for(milliseconds(16));
        }
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
