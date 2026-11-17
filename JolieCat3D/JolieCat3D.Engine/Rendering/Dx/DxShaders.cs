// Vortice annotates many D3D parameters as non-nullable, but D3D11 explicitly accepts
// null to unbind a resource/shader/state slot - so null is passed on purpose here.
#pragma warning disable CS8604, CS8625

namespace JolieCat3D.Engine.Rendering.Dx
{
    /// <summary>HLSL source for every pass of the viewport renderer. Compiled once at
    /// device creation (shader model 5.0). Matrices are row-major and multiplied as
    /// <c>mul(vector, matrix)</c>, matching System.Numerics.</summary>
    internal static class DxShaders
    {
        public const int MaxLights = 8;

        public const string Common = @"
#pragma pack_matrix(row_major)

struct Light
{
    float4 ColorType;   // rgb = color * intensity, a = type (0 dir, 1 point, 2 spot)
    float4 PosRange;    // xyz = world position, w = range
    float4 DirCosOuter; // xyz = direction the light shines along, w = cos(outer cone)
    float4 CosInner;    // x = cos(inner cone)
};

cbuffer FrameConstants : register(b0)
{
    float4x4 View;
    float4x4 Proj;
    float4x4 ViewProj;
    float4x4 InvViewProj;
    float4x4 LightViewProj;
    float3 CameraPos;      float ShadowsEnabled;
    float2 ViewportSize;   float AoEnabled;     float ShadingMode; // 0 wire, 1 solid, 2 material, 3 rendered
    float3 AmbientColor;   float LightCount;
    float3 KeyLightDir;    float IsOrtho;
    float3 KeyLightColor;  float GridSize;
    float3 BackgroundTop;  float HasSkybox;
    float3 BackgroundBottom; float GridMajorEvery;
    float3 EnvTint;        float HasEnvTint;
    float3 GridOrigin;     float GridPlane;     // 0 = XZ ground, 1 = XY, 2 = YZ
    float4 EnvSh[9];       // world irradiance, 9 SH coefficients (cosine-convolved)
    float EnvMaxMip;       float EnvIntensity;  float2 EnvPad;
    Light Lights[8];
};

cbuffer ObjectConstants : register(b1)
{
    float4x4 World;
    float4x4 WorldNormal;
    float4 BaseColor;        // rgb (sRGB) + opacity
    float Roughness;  float Metallic;  float UseVertexColor;  float Heatmap;
    float HasDiffuseTex; float HasNormalTex; float HasMRTex; float Unlit;
    float2 UvOffset; float2 UvScale;
    float4 Tint;             // rgb added on top (e.g. hover), a = amount
    float HasAoTex; float HasEmissiveTex; float AlphaCutoff; float ObjectPad;
    float4 Emissive;         // rgb (sRGB) + strength
};

Texture2D DiffuseTex : register(t0);
Texture2D NormalTex : register(t1);
Texture2D MRTex : register(t2);
Texture2D<float> ShadowMap : register(t3);
Texture2D<float> AoTex : register(t4);
TextureCube SkyTex : register(t5);
Texture2D OcclusionTex : register(t12);
Texture2D EmissiveTex : register(t13);

SamplerState LinearWrap : register(s0);
SamplerState LinearClamp : register(s1);
SamplerComparisonState ShadowSampler : register(s2);
SamplerState PointClamp : register(s3);

static const float PI = 3.14159265;

float3 SrgbToLinear(float3 c) { return pow(max(c, 0), 2.2); }
float3 LinearToSrgb(float3 c) { return pow(saturate(c), 1.0 / 2.2); }
";

        public const string Mesh = Common + @"
struct VSIn
{
    float3 Pos : POSITION;
    float3 Normal : NORMAL;
    float2 Uv : TEXCOORD0;
    float4 Color : COLOR0;
};

struct PSIn
{
    float4 Pos : SV_Position;
    float3 WorldPos : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float2 Uv : TEXCOORD2;
    float4 Color : COLOR0;
};

// Material alpha (opacity x texture alpha) at a mesh UV - for alpha-clip tests.
float ClipAlpha(float2 rawUv)
{
    float a = BaseColor.a;
    if (HasDiffuseTex > 0.5) a *= DiffuseTex.Sample(LinearWrap, rawUv * UvScale + UvOffset).a;
    return a;
}

PSIn VSMain(VSIn i)
{
    PSIn o;
    float4 wp = mul(float4(i.Pos, 1), World);
    o.WorldPos = wp.xyz;
    o.Pos = mul(wp, ViewProj);
    o.Normal = mul(float4(i.Normal, 0), WorldNormal).xyz;
    o.Uv = i.Uv;
    o.Color = i.Color;
    return o;
}

float ShadowFactor(float3 worldPos, float3 n, float3 l)
{
    if (ShadowsEnabled < 0.5) return 1;
    float bias = 0.0015 + 0.004 * (1 - saturate(dot(n, l)));
    float4 lp = mul(float4(worldPos + n * 0.01, 1), LightViewProj);
    float3 p = lp.xyz / lp.w;
    float2 uv = float2(p.x * 0.5 + 0.5, 0.5 - p.y * 0.5);
    if (any(uv < 0) || any(uv > 1) || p.z > 1) return 1;
    float w, h; ShadowMap.GetDimensions(w, h);
    float2 texel = 1.0 / float2(w, h);
    float sum = 0;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
        sum += ShadowMap.SampleCmpLevelZero(ShadowSampler, uv + float2(x, y) * texel * 1.5, p.z - bias);
    return sum / 9.0;
}

float DistributionGGX(float ndh, float a)
{
    float a2 = a * a;
    float d = ndh * ndh * (a2 - 1) + 1;
    return a2 / (PI * d * d + 1e-5);
}

float GeometrySmith(float ndv, float ndl, float r)
{
    float k = (r + 1) * (r + 1) / 8;
    return (ndv / (ndv * (1 - k) + k)) * (ndl / (ndl * (1 - k) + k));
}

float3 Brdf(float3 n, float3 v, float3 l, float3 albedo, float rough, float metal, float3 radiance)
{
    float3 h = normalize(v + l);
    float ndl = saturate(dot(n, l));
    float ndv = saturate(dot(n, v)) + 1e-4;
    float ndh = saturate(dot(n, h));
    float a = max(rough * rough, 0.02);
    float3 f0 = lerp(0.04.xxx, albedo, metal);
    float3 F = f0 + (1 - f0) * pow(1 - saturate(dot(h, v)), 5);
    float3 spec = DistributionGGX(ndh, a) * GeometrySmith(ndv, ndl, rough) * F / (4 * ndv * ndl + 1e-4);
    float3 kd = (1 - F) * (1 - metal);
    return (kd * albedo / PI + spec) * radiance * ndl;
}

// ---- image-based lighting from the world texture (Material Preview / Rendered) ----
float3 EnvIrradiance(float3 n)
{
    float3 e = EnvSh[0].rgb * 0.282095
             + EnvSh[1].rgb * (0.488603 * n.y) + EnvSh[2].rgb * (0.488603 * n.z) + EnvSh[3].rgb * (0.488603 * n.x)
             + EnvSh[4].rgb * (1.092548 * n.x * n.y) + EnvSh[5].rgb * (1.092548 * n.y * n.z)
             + EnvSh[6].rgb * (0.315392 * (3 * n.z * n.z - 1)) + EnvSh[7].rgb * (1.092548 * n.x * n.z)
             + EnvSh[8].rgb * (0.546274 * (n.x * n.x - n.y * n.y));
    return max(e, 0);
}

// Karis' analytic fit of the split-sum environment BRDF.
float3 EnvBrdfApprox(float3 f0, float rough, float ndv)
{
    const float4 c0 = float4(-1, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1, 0.0425, 1.04, -0.04);
    float4 r = rough * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * ndv)) * r.x + r.y;
    float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
    return f0 * ab.x + ab.y;
}

float3 EnvLighting(float3 n, float3 v, float3 albedo, float rough, float metal, float ao)
{
    float ndv = saturate(dot(n, v));
    float3 f0 = lerp(0.04.xxx, albedo, metal);
    float3 spec = EnvBrdfApprox(f0, rough, ndv);
    float3 r = reflect(-v, n);
    float3 prefiltered = SkyTex.SampleLevel(LinearClamp, r, sqrt(saturate(rough)) * EnvMaxMip).rgb;
    float3 diffuse = albedo * (1 - metal) * (1 - spec) * EnvIrradiance(n) / PI;
    return (diffuse + prefiltered * spec) * ao * EnvIntensity;
}

float3 PerturbNormal(float3 n, float3 worldPos, float2 uv)
{
    // Cotangent frame from screen-space derivatives - no tangent attribute needed.
    float3 dp1 = ddx(worldPos), dp2 = ddy(worldPos);
    float2 duv1 = ddx(uv), duv2 = ddy(uv);
    float3 dp2perp = cross(dp2, n), dp1perp = cross(n, dp1);
    float3 t = dp2perp * duv1.x + dp1perp * duv2.x;
    float3 b = dp2perp * duv1.y + dp1perp * duv2.y;
    float invmax = rsqrt(max(dot(t, t), dot(b, b)) + 1e-12);
    float3x3 tbn = float3x3(t * invmax, b * invmax, n);
    float3 m = NormalTex.Sample(LinearWrap, uv).xyz * 2 - 1;
    return normalize(mul(m, tbn));
}

float4 PSMain(PSIn i, bool frontFace : SV_IsFrontFace) : SV_Target
{
    // View._13/_23/_33 is the camera's backward axis, i.e. towards the viewer.
    float3 v = IsOrtho > 0.5 ? normalize(float3(View._13, View._23, View._33)) : normalize(CameraPos - i.WorldPos);
    float3 n = i.Normal;
    if (dot(n, n) < 1e-8) n = normalize(cross(ddx(i.WorldPos), ddy(i.WorldPos))); // mesh without normals
    n = normalize(n);
    // Two-sided lighting that does not depend on triangle winding.
    if (dot(n, v) < 0) n = -n;

    float2 uv = i.Uv * UvScale + UvOffset;
    float3 albedo;
    float alpha = BaseColor.a;

    if (Heatmap > 0.5)
    {
        // Weight paint: blue (0) -> green -> red (1), like Blender.
        float w = saturate(i.Uv.x);
        float3 c = w < 0.5 ? lerp(float3(0, 0, 1), float3(0, 1, 0), w * 2) : lerp(float3(0, 1, 0), float3(1, 0, 0), w * 2 - 1);
        albedo = SrgbToLinear(c);
        alpha = 1;
    }
    else if (ShadingMode < 1.5)
    {
        albedo = SrgbToLinear(float3(0.69, 0.69, 0.69)); // neutral solid grey
    }
    else
    {
        albedo = SrgbToLinear(BaseColor.rgb);
        if (HasDiffuseTex > 0.5)
        {
            float4 tex = DiffuseTex.Sample(LinearWrap, uv); // sRGB view -> already linear
            albedo *= tex.rgb;
            alpha *= tex.a;
        }
    }

    if (AlphaCutoff > 0 && ShadingMode > 1.5 && Heatmap < 0.5)
    {
        if (alpha < AlphaCutoff) discard;
        alpha = 1;
    }

    if (UseVertexColor > 0.5 && Heatmap < 0.5)
    {
        albedo *= SrgbToLinear(i.Color.rgb);
    }

    if (Unlit > 0.5) return float4(albedo + Tint.rgb * Tint.a, alpha);

    float rough = Roughness, metal = Metallic;
    if (ShadingMode > 1.5)
    {
        if (HasMRTex > 0.5)
        {
            float4 mr = MRTex.Sample(LinearWrap, uv);
            rough *= mr.g; metal *= mr.b;
        }
        if (HasNormalTex > 0.5) n = PerturbNormal(n, i.WorldPos, uv);
    }
    else
    {
        rough = 0.6; metal = 0;
    }

    float ao = 1;
    if (AoEnabled > 0.5)
    {
        float2 suv = i.Pos.xy / ViewportSize;
        ao = AoTex.SampleLevel(LinearClamp, suv, 0);
    }

    if (HasAoTex > 0.5 && ShadingMode > 1.5) ao *= OcclusionTex.Sample(LinearWrap, i.Uv).r;

    float3 color = 0;
    bool useSceneLights = ShadingMode > 2.5 && LightCount > 0.5;
    // A world texture lights the scene by itself (Blender's Material Preview / HDRI world).
    bool envLit = HasSkybox > 0.5 && ShadingMode > 1.5 && Heatmap < 0.5;
    if (useSceneLights)
    {
        int count = (int)LightCount;
        for (int li = 0; li < count; li++)
        {
            Light L = Lights[li];
            float type = L.ColorType.a;
            float3 radiance = L.ColorType.rgb;
            float3 l;
            float shadow = 1;
            if (type < 0.5)
            {
                l = -normalize(L.DirCosOuter.xyz);
                if (li == 0) shadow = ShadowFactor(i.WorldPos, n, l);
            }
            else
            {
                float3 d = L.PosRange.xyz - i.WorldPos;
                float dist = length(d);
                l = d / max(dist, 1e-4);
                float range = max(L.PosRange.w, 1e-3);
                float att = saturate(1 - pow(dist / range, 4));
                float rel = dist / range;
                radiance *= att * att / (1 + 4 * rel * rel);
                if (type > 1.5)
                {
                    float cd = dot(-l, normalize(L.DirCosOuter.xyz));
                    radiance *= smoothstep(L.DirCosOuter.w, L.CosInner.x, cd);
                }
            }
            color += Brdf(n, v, l, albedo, rough, metal, radiance) * shadow;
        }
        if (envLit) color += EnvLighting(n, v, albedo, rough, metal, ao);
        else
        {
            float3 amb = AmbientColor;
            if (HasEnvTint > 0.5) amb = lerp(amb, EnvTint * 0.6, 0.5);
            color += albedo * amb * ao;
        }
    }
    else if (envLit)
    {
        color += EnvLighting(n, v, albedo, rough, metal, ao);
    }
    else
    {
        // Studio lighting: key (casts shadow) + fill + rim + hemisphere ambient.
        float3 keyL = -normalize(KeyLightDir);
        float shadow = ShadowFactor(i.WorldPos, n, keyL);
        color += Brdf(n, v, keyL, albedo, rough, metal, KeyLightColor * 2.6) * shadow;
        float3 fillL = normalize(float3(0.6, 0.35, 0.75));
        color += Brdf(n, v, fillL, albedo, rough, metal, float3(0.55, 0.58, 0.65));
        float3 rimL = normalize(float3(-0.2, 0.5, -1.0));
        color += Brdf(n, v, rimL, albedo, rough, metal, float3(0.35, 0.35, 0.38));
        float hemi = n.y * 0.5 + 0.5;
        float3 amb = lerp(AmbientColor * 0.55, AmbientColor * 1.15, hemi);
        if (HasEnvTint > 0.5) amb = lerp(amb, EnvTint * 0.7, 0.5);
        color += albedo * amb * ao;
    }

    if (ShadingMode > 1.5 && Emissive.a > 0)
    {
        float3 emission = SrgbToLinear(Emissive.rgb) * Emissive.a;
        if (HasEmissiveTex > 0.5) emission *= EmissiveTex.Sample(LinearWrap, uv).rgb;
        color += emission;
    }

    color += Tint.rgb * Tint.a;
    return float4(color, alpha);
}

// ---- depth/normal prepass (for SSAO) ----
struct PrePSIn { float4 Pos : SV_Position; float3 ViewNormal : TEXCOORD0; float ViewDepth : TEXCOORD1; float2 Uv : TEXCOORD2; };

PrePSIn VSPrepass(VSIn i)
{
    PrePSIn o;
    float4 wp = mul(float4(i.Pos, 1), World);
    o.Pos = mul(wp, ViewProj);
    o.ViewNormal = mul(mul(float4(i.Normal, 0), WorldNormal), View).xyz;
    o.ViewDepth = -mul(wp, View).z;
    o.Uv = i.Uv;
    return o;
}

float4 PSPrepass(PrePSIn i, bool frontFace : SV_IsFrontFace) : SV_Target
{
    if (AlphaCutoff > 0 && ClipAlpha(i.Uv) < AlphaCutoff) discard;
    float3 n = i.ViewNormal;
    if (dot(n, n) < 1e-8) n = float3(0, 0, 1);
    n = normalize(n);
    if (n.z < 0) n = -n; // view space: +z points at the camera
    return float4(n, i.ViewDepth);
}

// ---- shadow + selection mask ----
float4 VSShadow(VSIn i) : SV_Position
{
    return mul(mul(float4(i.Pos, 1), World), LightViewProj);
}

struct ShadowClipOut { float4 Pos : SV_Position; float2 Uv : TEXCOORD0; };

ShadowClipOut VSShadowClip(VSIn i)
{
    ShadowClipOut o;
    o.Pos = mul(mul(float4(i.Pos, 1), World), LightViewProj);
    o.Uv = i.Uv;
    return o;
}

float4 PSShadowClip(ShadowClipOut i) : SV_Target
{
    if (ClipAlpha(i.Uv) < AlphaCutoff) discard;
    return 0;
}

float4 VSMask(VSIn i) : SV_Position
{
    return mul(mul(float4(i.Pos, 1), World), ViewProj);
}

float4 PSMask(float4 pos : SV_Position) : SV_Target
{
    return Tint; // r = 1 for selected, g = 1 for active
}
";

        public const string Fullscreen = Common + @"
struct FsOut { float4 Pos : SV_Position; float2 Uv : TEXCOORD0; };

FsOut VSFullscreen(uint id : SV_VertexID)
{
    FsOut o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.Uv = uv;
    o.Pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

// ---- background: gradient or skybox ----
float4 PSBackground(FsOut i) : SV_Target
{
    if (HasSkybox > 0.5)
    {
        float2 ndc = float2(i.Uv.x * 2 - 1, 1 - i.Uv.y * 2);
        float4 a = mul(float4(ndc, 0, 1), InvViewProj);
        float4 b = mul(float4(ndc, 1, 1), InvViewProj);
        float3 dir = normalize(b.xyz / b.w - a.xyz / a.w);
        return float4(SkyTex.SampleLevel(LinearClamp, dir, 0).rgb * EnvIntensity, 1);
    }
    float3 c = lerp(BackgroundTop, BackgroundBottom, i.Uv.y);
    return float4(SrgbToLinear(c), 1);
}

// ---- SSAO ----
Texture2D<float4> NormalDepthTex : register(t6);

float3 ViewPosFromDepth(float2 uv, float viewDepth)
{
    float2 ndc = float2(uv.x * 2 - 1, 1 - uv.y * 2);
    if (IsOrtho > 0.5)
    {
        float hx = 1.0 / Proj._11, hy = 1.0 / Proj._22;
        return float3(ndc.x * hx, ndc.y * hy, -viewDepth);
    }
    return float3(ndc.x / Proj._11 * viewDepth, ndc.y / Proj._22 * viewDepth, -viewDepth);
}

float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

float4 PSSsao(FsOut i) : SV_Target
{
    float4 nd = NormalDepthTex.SampleLevel(PointClamp, i.Uv, 0);
    if (nd.w <= 0) return 1;
    float3 n = normalize(nd.xyz);
    float3 p = ViewPosFromDepth(i.Uv, nd.w);
    float radius = clamp(nd.w * 0.06, 0.05, 1.5);
    float occlusion = 0;
    const int SAMPLES = 12;
    float rot = Hash(i.Pos.xy) * 6.2831;
    [unroll] for (int s = 0; s < SAMPLES; s++)
    {
        float fs = (s + 0.5) / SAMPLES;
        float ang = rot + fs * 6.2831 * 3.0;
        float r = radius * (0.2 + 0.8 * fs * fs);
        float3 offset = float3(cos(ang), sin(ang), 0) * r;
        float3 sp = p + offset + n * r * 0.5;
        float4 clip = mul(float4(sp, 1), Proj);
        float2 suv = float2(clip.x / clip.w * 0.5 + 0.5, 0.5 - clip.y / clip.w * 0.5);
        float sd = NormalDepthTex.SampleLevel(PointClamp, suv, 0).w;
        if (sd <= 0) continue;
        float3 q = ViewPosFromDepth(suv, sd);
        float3 d = q - p;
        float dist = length(d);
        float range = saturate(radius / (abs(p.z - q.z) + 1e-4));
        occlusion += saturate(dot(n, d / max(dist, 1e-4)) - 0.08) * range;
    }
    return saturate(1 - occlusion / SAMPLES * 1.6);
}

Texture2D<float> AoRawTex : register(t7);

float4 PSBlurAo(FsOut i) : SV_Target
{
    float w, h; AoRawTex.GetDimensions(w, h);
    float2 texel = 1.0 / float2(w, h);
    float centerDepth = NormalDepthTex.SampleLevel(PointClamp, i.Uv, 0).w;
    float sum = 0, wsum = 0;
    [unroll] for (int y = -2; y <= 2; y++)
    [unroll] for (int x = -2; x <= 2; x++)
    {
        float2 uv = i.Uv + float2(x, y) * texel;
        float d = NormalDepthTex.SampleLevel(PointClamp, uv, 0).w;
        float wt = 1.0 / (1e-3 + abs(d - centerDepth) * 4);
        sum += AoRawTex.SampleLevel(LinearClamp, uv, 0) * wt;
        wsum += wt;
    }
    return sum / max(wsum, 1e-4);
}

// ---- bloom ----
Texture2D SourceTex : register(t8);
cbuffer PostConstants : register(b2)
{
    float2 TexelDir; float BloomThreshold; float BloomIntensity;
    float4 OutlineColor;
    float4 ActiveOutlineColor;
    float BloomEnabled; float OutlineEnabled; float2 PostPad;
};

float4 PSBrightPass(FsOut i) : SV_Target
{
    float3 c = SourceTex.SampleLevel(LinearClamp, i.Uv, 0).rgb;
    float lum = max(c.r, max(c.g, c.b));
    float k = saturate((lum - BloomThreshold) / max(lum, 1e-4));
    return float4(c * k, 1);
}

float4 PSBlur(FsOut i) : SV_Target
{
    static const float weights[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };
    float3 c = SourceTex.SampleLevel(LinearClamp, i.Uv, 0).rgb * weights[0];
    [unroll] for (int k = 1; k < 5; k++)
    {
        c += SourceTex.SampleLevel(LinearClamp, i.Uv + TexelDir * k, 0).rgb * weights[k];
        c += SourceTex.SampleLevel(LinearClamp, i.Uv - TexelDir * k, 0).rgb * weights[k];
    }
    return float4(c, 1);
}

// ---- final composite: bloom + tonemap + sRGB + selection outline ----
Texture2D SceneTex : register(t9);
Texture2D BloomTex : register(t10);
Texture2D<float2> MaskTex : register(t11);

float3 SoftClip(float3 c)
{
    // Identity below 0.8, smooth shoulder above - keeps editor colours faithful.
    float3 x = max(c - 0.8, 0);
    return min(c, 0.8) + 0.2 * (1 - exp(-x / 0.2));
}

float4 PSComposite(FsOut i) : SV_Target
{
    float3 c = SceneTex.SampleLevel(PointClamp, i.Uv, 0).rgb;
    if (BloomEnabled > 0.5) c += BloomTex.SampleLevel(LinearClamp, i.Uv, 0).rgb * BloomIntensity;
    c = LinearToSrgb(SoftClip(c));

    if (OutlineEnabled > 0.5)
    {
        float w, h; MaskTex.GetDimensions(w, h);
        float2 texel = 1.0 / float2(w, h);
        float2 center = MaskTex.SampleLevel(PointClamp, i.Uv, 0);
        float2 maxN = 0;
        [unroll] for (int y = -2; y <= 2; y++)
        [unroll] for (int x = -2; x <= 2; x++)
        {
            if (abs(x) + abs(y) > 3) continue;
            maxN = max(maxN, MaskTex.SampleLevel(PointClamp, i.Uv + float2(x, y) * texel, 0));
        }
        if (center.g < 0.5 && maxN.g > 0.5) c = ActiveOutlineColor.rgb;
        else if (center.r < 0.5 && maxN.r > 0.5) c = OutlineColor.rgb;
    }
    return float4(c, 1);
}
";

        public const string Grid = Common + @"
struct GridOut { float4 Pos : SV_Position; float3 WorldPos : TEXCOORD0; };

GridOut VSGrid(uint id : SV_VertexID)
{
    // Two triangles covering a large square on the grid plane, centred under the view.
    static const float2 corners[6] = { float2(-1,-1), float2(1,-1), float2(1,1), float2(-1,-1), float2(1,1), float2(-1,1) };
    float extent = max(200.0, length(CameraPos - GridOrigin) * 60.0);
    float2 c = corners[id] * extent;
    float3 wp;
    if (GridPlane < 0.5)      wp = float3(c.x + GridOrigin.x, 0, c.y + GridOrigin.z);
    else if (GridPlane < 1.5) wp = float3(c.x + GridOrigin.x, c.y + GridOrigin.y, 0);
    else                      wp = float3(0, c.x + GridOrigin.y, c.y + GridOrigin.z);
    GridOut o;
    o.WorldPos = wp;
    o.Pos = mul(float4(wp, 1), ViewProj);
    return o;
}

float2 GridCoords(float3 wp)
{
    if (GridPlane < 0.5) return wp.xz;
    if (GridPlane < 1.5) return wp.xy;
    return wp.yz;
}

float GridLine(float2 coord, float width)
{
    float2 d = fwidth(coord);
    float2 g = abs(frac(coord - 0.5) - 0.5) / max(d, 1e-5);
    float lineDist = min(g.x, g.y);
    return 1 - saturate(lineDist - width + 0.5);
}

float ShadowAt(float3 wp)
{
    if (ShadowsEnabled < 0.5) return 1;
    float4 lp = mul(float4(wp, 1), LightViewProj);
    float3 p = lp.xyz / lp.w;
    float2 uv = float2(p.x * 0.5 + 0.5, 0.5 - p.y * 0.5);
    if (any(uv < 0) || any(uv > 1) || p.z > 1) return 1;
    float w, h; ShadowMap.GetDimensions(w, h);
    float2 texel = 1.0 / float2(w, h);
    float sum = 0;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
        sum += ShadowMap.SampleCmpLevelZero(ShadowSampler, uv + float2(x, y) * texel * 1.5, p.z - 0.002);
    return sum / 9.0;
}

float4 PSGrid(GridOut i) : SV_Target
{
    float2 plane = GridCoords(i.WorldPos);
    float2 coord = plane / max(GridSize, 1e-4);
    float minor = GridLine(coord, 1.0);
    float major = GridLine(coord / GridMajorEvery, 1.4);

    float fade = 1;
    if (IsOrtho < 0.5)
    {
        float dist = length(i.WorldPos - CameraPos);
        float fadeRange = max(30.0, abs(CameraPos.y) * 25.0);
        fade = saturate(1 - dist / fadeRange);
    }
    // Minor lines vanish once they get denser than a few pixels.
    float density = length(fwidth(coord));
    float minorFade = saturate(1.5 - density * 2.5);
    float majorFade = saturate(1.5 - density / GridMajorEvery * 2.5);

    float3 color = float3(0.42, 0.42, 0.42);
    float alpha = max(minor * 0.22 * minorFade, major * 0.42 * majorFade) * fade;

    // Axis lines: red X, green Y, blue Z (Blender colours).
    float2 axisW = fwidth(plane) * 1.6;
    float a1 = 1 - saturate(abs(plane.y) / max(axisW.y, 1e-5)); // first in-plane axis (along plane.x)
    float a2 = 1 - saturate(abs(plane.x) / max(axisW.x, 1e-5)); // second in-plane axis (along plane.y)
    float3 c1 = GridPlane < 1.5 ? float3(0.86, 0.28, 0.32) : float3(0.45, 0.78, 0.30);
    float3 c2 = GridPlane < 0.5 ? float3(0.30, 0.50, 0.92) : (GridPlane < 1.5 ? float3(0.45, 0.78, 0.30) : float3(0.30, 0.50, 0.92));
    if (a1 > 0.01) { color = lerp(color, c1, a1); alpha = max(alpha, a1 * 0.9 * max(fade, 0.35)); }
    if (a2 > 0.01) { color = lerp(color, c2, a2); alpha = max(alpha, a2 * 0.9 * max(fade, 0.35)); }

    // The ground also catches shadows so objects feel grounded (darkens only).
    float shadowAlpha = 0;
    if (GridPlane < 0.5)
    {
        float shadow = ShadowAt(i.WorldPos);
        shadowAlpha = (1 - shadow) * 0.5 * saturate(fade * 3);
    }
    float3 outColor = SrgbToLinear(color) * alpha;          // premultiplied
    float outAlpha = saturate(alpha + shadowAlpha * (1 - alpha));
    return float4(outColor, outAlpha);
}
";

        public const string Overlay = Common + @"
// Thick screen-space lines: one instance per segment, 6 vertices per instance.
struct LineInstance
{
    float3 A : POSITION0;
    float3 B : POSITION1;
    float4 Color : COLOR0;
    float Width : TEXCOORD0;
    float DepthBias : TEXCOORD1;
};

struct LineOut { float4 Pos : SV_Position; float4 Color : COLOR0; float Edge : TEXCOORD0; float Width : TEXCOORD1; };

float4 ClipNear(float4 a, float4 b)
{
    const float eps = 1e-4;
    if (a.w >= eps) return a;
    float t = (eps - a.w) / (b.w - a.w);
    return lerp(a, b, t);
}

LineOut VSLine(uint vid : SV_VertexID, LineInstance inst)
{
    float4 ca = mul(float4(inst.A, 1), ViewProj);
    float4 cb = mul(float4(inst.B, 1), ViewProj);
    float4 a = ClipNear(ca, cb);
    float4 b = ClipNear(cb, ca);

    float2 sa = a.xy / a.w * ViewportSize * 0.5;
    float2 sb = b.xy / b.w * ViewportSize * 0.5;
    float2 dir = sb - sa;
    float len = length(dir);
    dir = len > 1e-5 ? dir / len : float2(1, 0);
    float2 nrm = float2(-dir.y, dir.x);

    static const float2 quad[6] = { float2(0,-1), float2(1,-1), float2(1,1), float2(0,-1), float2(1,1), float2(0,1) };
    float2 q = quad[vid];
    float4 p = q.x < 0.5 ? a : b;
    float halfW = inst.Width * 0.5 + 1.0; // +1px for anti-aliasing
    float2 offsetPx = nrm * q.y * halfW + dir * (q.x < 0.5 ? -1 : 1) * min(halfW, 1.5);
    p.xy += offsetPx / (ViewportSize * 0.5) * p.w;
    p.z -= inst.DepthBias * p.w;

    LineOut o;
    o.Pos = p;
    o.Color = inst.Color;
    o.Edge = q.y * halfW;
    o.Width = inst.Width * 0.5;
    return o;
}

float4 PSLine(LineOut i) : SV_Target
{
    float d = abs(i.Edge);
    float a = saturate(i.Width + 0.5 - d);
    return float4(SrgbToLinear(i.Color.rgb), i.Color.a * a);
}

// Round screen-space points.
struct PointInstance { float3 P : POSITION0; float4 Color : COLOR0; float Size : TEXCOORD0; float DepthBias : TEXCOORD1; };
struct PointOut { float4 Pos : SV_Position; float4 Color : COLOR0; float2 Local : TEXCOORD0; float Size : TEXCOORD1; };

PointOut VSPoint(uint vid : SV_VertexID, PointInstance inst)
{
    static const float2 quad[6] = { float2(-1,-1), float2(1,-1), float2(1,1), float2(-1,-1), float2(1,1), float2(-1,1) };
    float4 p = mul(float4(inst.P, 1), ViewProj);
    float halfW = inst.Size * 0.5 + 1;
    float2 q = quad[vid];
    p.xy += q * halfW / (ViewportSize * 0.5) * p.w;
    p.z -= inst.DepthBias * p.w;
    PointOut o;
    o.Pos = p;
    o.Color = inst.Color;
    o.Local = q * halfW;
    o.Size = inst.Size * 0.5;
    if (p.w <= 0) o.Pos = float4(0, 0, -1, 1);
    return o;
}

float4 PSPoint(PointOut i) : SV_Target
{
    float d = length(i.Local);
    float a = saturate(i.Size + 0.5 - d);
    if (a <= 0) discard;
    float3 c = SrgbToLinear(i.Color.rgb);
    c *= lerp(0.55, 1.0, saturate(i.Size - 0.5 - d)); // thin dark rim
    return float4(c, i.Color.a * a);
}

// Flat/lit coloured triangles (gizmo solids, selected faces, ...).
struct TriIn { float3 P : POSITION; float3 N : NORMAL; float4 Color : COLOR0; };
struct TriOut { float4 Pos : SV_Position; float3 N : TEXCOORD0; float4 Color : COLOR0; };

TriOut VSTri(TriIn i)
{
    TriOut o;
    o.Pos = mul(float4(i.P, 1), ViewProj);
    o.N = i.N;
    o.Color = i.Color;
    return o;
}

float4 PSTri(TriOut i) : SV_Target
{
    float3 c = SrgbToLinear(i.Color.rgb);
    if (dot(i.N, i.N) > 0.01)
    {
        float3 n = normalize(i.N);
        float3 v = normalize(-float3(View._13, View._23, View._33));
        float lit = 0.55 + 0.45 * abs(dot(n, normalize(float3(0.4, 0.8, 0.45))));
        c *= lit;
    }
    return float4(c, i.Color.a);
}
";
    }
}
