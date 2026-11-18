#pragma once

inline float4 UnityObjectToClipPosInstanced(in float3 pos) {
    return mul(UNITY_MATRIX_VP, mul(GetObjectToWorldMatrix(), float4(pos, 1.0)));
}
#define UnityObjectToClipPos UnityObjectToClipPosInstanced // 인스턴스별 변환 행렬을 사용합니다.

inline float3 UnityObjectToWorldNormal(in float3 norm)
{
    #ifdef UNITY_ASSUME_UNIFORM_SCALING
    return UnityObjectToWorldDir(norm);
    #else

    return normalize(mul(norm, (float3x3)GetWorldToObjectMatrix()));
    #endif
}
