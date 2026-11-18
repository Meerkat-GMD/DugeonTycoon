

#pragma warning (disable : 3571)

#pragma warning (disable : 3206)

#ifndef UCTS_LWRP_INCLUDED
#define UCTS_LWRP_INCLUDED

#define UCTS_LWRP 1

#ifndef DIRECTIONAL
# define DIRECTIONAL
#endif

#define FP_BUFFER 1
#if FP_BUFFER
#define SATURATE_IF_SDR(x) (x)
#define SATURATE_BASE_COLOR_IF_SDR(x) (x)
#else
#define SATURATE_IF_SDR(x) saturate(x)
#define SATURATE_BASE_COLOR_IF_SDR(x) saturate(x)
#endif

#if defined(UNITY_PASS_PREPASSBASE) || defined(UNITY_PASS_DEFERRED) || defined(UNITY_PASS_SHADOWCASTER)
#undef FOG_LINEAR
#undef FOG_EXP
#undef FOG_EXP2
#endif

#if 1

inline bool IsGammaSpace() {
#ifdef UNITY_COLORSPACE_GAMMA
    return true;
#else
    return false;
#endif
}

inline float GammaToLinearSpaceExact(float value) {
    if (value <= 0.04045F)
        return value / 12.92F;
    else if (value < 1.0F)
        return pow((value + 0.055F) / 1.055F, 2.4F);
    else
        return pow(value, 2.2F);
}

inline half3 GammaToLinearSpace(half3 sRGB) {

    return sRGB * (sRGB * (sRGB * 0.305306011h + 0.682171111h) + 0.012522878h);

}

inline float LinearToGammaSpaceExact(float value) {
    if (value <= 0.0F)
        return 0.0F;
    else if (value <= 0.0031308F)
        return 12.92F * value;
    else if (value < 1.0F)
        return 1.055F * pow(value, 0.4166667F) - 0.055F;
    else
        return pow(value, 0.45454545F);
}

inline half3 LinearToGammaSpace(half3 linRGB) {
    linRGB = max(linRGB, half3(0.h, 0.h, 0.h));

    return max(1.055h * pow(linRGB, 0.416666667h) - 0.055h, 0.h);

}

#ifdef UNITY_PASS_FORWARDADD
#define UNITY_APPLY_FOG(coord,col) UNITY_APPLY_FOG_COLOR(coord,col,fixed4(0,0,0,0)) // 추가 조명 패스의 안개 색은 검정으로 적용합니다.
#else
#define UNITY_APPLY_FOG(coord,col) UNITY_APPLY_FOG_COLOR(coord,col,unity_FogColor) // 기본 패스에서는 씬의 안개 색을 적용합니다.
#endif

#endif

#ifdef DIRECTIONAL
#define LIGHTING_COORDS(idx1,idx2) SHADOW_COORDS(idx1)
#define TRANSFER_VERTEX_TO_FRAGMENT(a) TRANSFER_SHADOW(a)
#define LIGHT_ATTENUATION(a)    SHADOW_ATTENUATION(a)
#endif

#define UCTS_TEXTURE2D(tex,name)  SAMPLE_TEXTURE2D(tex,sampler##tex,TRANSFORM_TEX(name, tex));

inline float4 UnityObjectToClipPosInstanced(in float3 pos) {

    return mul(UNITY_MATRIX_VP, mul(GetObjectToWorldMatrix(), float4(pos, 1.0)));
}

inline float4 UnityObjectToClipPosInstanced(float4 pos) {
    return UnityObjectToClipPosInstanced(pos.xyz);
}

#define UnityObjectToClipPos UnityObjectToClipPosInstanced // 인스턴스별 변환 행렬을 사용합니다.

inline float3 UnityObjectToWorldNormal(in float3 norm) {
#ifdef UNITY_ASSUME_UNIFORM_SCALING
    return UnityObjectToWorldDir(norm);
#else

    return normalize(mul(norm, (float3x3)GetWorldToObjectMatrix()));
#endif
}

half3 SHEvalLinearL0L1(half4 normal) {
    half3 x;

    x.r = dot(unity_SHAr, normal);
    x.g = dot(unity_SHAg, normal);
    x.b = dot(unity_SHAb, normal);

    return x;
}

half3 SHEvalLinearL2(half4 normal) {
    half3 x1, x2;

    half4 vB = normal.xyzz * normal.yzzx;
    x1.r = dot(unity_SHBr, vB);
    x1.g = dot(unity_SHBg, vB);
    x1.b = dot(unity_SHBb, vB);

    half vC = normal.x * normal.x - normal.y * normal.y;
    x2 = unity_SHC.rgb * vC;

    return x1 + x2;
}

half3 ShadeSH9(half4 normal) {

    half3 res = SHEvalLinearL0L1(normal);

    res += SHEvalLinearL2(normal);

#   ifdef UNITY_COLORSPACE_GAMMA
    res = LinearToGammaSpace(res);
#   endif

    return res;
}

#endif
