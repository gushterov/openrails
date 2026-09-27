// COPYRIGHT 2010, 2011, 2013, 2014 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

// This file is the responsibility of the 3D & Environment Team. 

////////////////////////////////////////////////////////////////////////////////

// Positions and animation stay on the GPU; the CPU only replenishes particles.
float4x4 worldViewProjection;
float4x4 invView;
float3 LightVector;
float2 cameraTileXZ;
float currentTime;
float visibility;
float2 fadeDistance;

static float2 texCoords[4] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 1) };
static float2 offsets[4] = { float2(-0.5, 0.5), float2(0.5, 0.5), float2(0.5, -0.5), float2(-0.5, -0.5) };

texture rainTexture;
texture snowTexture;
sampler RainSampler = sampler_state
{
    Texture = (rainTexture);
    MagFilter = Linear;
    MinFilter = Linear;
    MipFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};
sampler SnowSampler = sampler_state
{
    Texture = (snowTexture);
    MagFilter = Linear;
    MinFilter = Linear;
    MipFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

struct VERTEX_INPUT
{
    float4 StartPosition_StartTime : POSITION0;
    float4 EndPosition_EndTime : POSITION1;
    float4 TileXZ_Vertex : POSITION2;
    // Liquid fraction, ground/roof height, independent variation, reserved.
    float4 Appearance : TEXCOORD0;
};

struct VERTEX_OUTPUT
{
    float4 Position : POSITION;
    float2 TexCoord : TEXCOORD0;
    float2 Opacity_Liquidity : TEXCOORD1;
};

VERTEX_OUTPUT VSPrecipitation(in VERTEX_INPUT In)
{
    VERTEX_OUTPUT Out = (VERTEX_OUTPUT)0;
    float duration = max(0.001, In.EndPosition_EndTime.w - In.StartPosition_StartTime.w);
    float age = (currentTime - In.StartPosition_StartTime.w) / duration;
    float seed = In.TileXZ_Vertex.w;
    float variation = In.Appearance.z;
    float liquid = In.Appearance.x;
    float snow = 1 - liquid;
    float phase = seed * 6.2831853;
    float time = currentTime - In.StartPosition_StartTime.w;
    int vertex = (int)In.TileXZ_Vertex.z;
    float3 position = lerp(In.StartPosition_StartTime.xyz, In.EndPosition_EndTime.xyz, age);
    position.xz += (cameraTileXZ - In.TileXZ_Vertex.xy) * float2(-2048, 2048);

    // Each flake has its own flutter and turn rate; rain follows the wind directly.
    position.x += snow * 0.45 * sin(time * (1.1 + variation) + phase);
    position.z += snow * 0.35 * cos(time * (0.8 + seed) + phase);
    float angle = phase + time * (variation - 0.5) * 1.8;

    // Project the fall direction onto the camera plane to keep streaks broadside.
    float3 fall = normalize(In.StartPosition_StartTime.xyz - In.EndPosition_EndTime.xyz);
    float2 projectedFall = float2(dot(fall, invView[0].xyz), dot(fall, invView[1].xyz));
    projectedFall.y += 0.001;
    projectedFall /= max(length(projectedFall), 0.001);
    float3 rainUp = invView[0].xyz * projectedFall.x + invView[1].xyz * projectedFall.y;
    float3 rainRight = invView[0].xyz * projectedFall.y - invView[1].xyz * projectedFall.x;
    float size = lerp(0.65, 1.45, seed);
    float width = lerp(0.22, 0.13, liquid) * size;
    float height = lerp(0.22, 0.6, liquid) * size;
    float turn = angle * snow;
    float3 right = rainRight * cos(turn) + rainUp * sin(turn);
    float3 up = -rainRight * sin(turn) + rainUp * cos(turn);

    float distanceToCamera = length(position - invView[3].xyz);
    // Fade births, deaths, nearby quads, distant particles and terrain intersections.
    float opacity = saturate(age * 12) * saturate((1 - age) * 8);
    opacity *= saturate((position.y - In.Appearance.y) * 3);
    opacity *= saturate((distanceToCamera - 0.7) / 1.8);
    opacity *= 1 - smoothstep(fadeDistance.x, fadeDistance.y, distanceToCamera);
    opacity *= exp(-3 * distanceToCamera / max(visibility, 1));
    opacity *= lerp(0.55, 0.95, variation) * lerp(1, 0.65, liquid);

    position += right * offsets[vertex].x * width + up * offsets[vertex].y * height;
    Out.Position = mul(float4(position, 1), worldViewProjection);
    Out.TexCoord = texCoords[vertex];
    Out.Opacity_Liquidity = float2(opacity, liquid);
    return Out;
}

float4 PSPrecipitation(in VERTEX_OUTPUT In) : COLOR0
{
    // The original snow texture has generous transparent margins. Sample the flake
    // itself so its apparent size is independent of that padding.
    float2 snowUV = (In.TexCoord - 0.5) * 0.22 + float2(0.52, 0.46);
    float4 snow = tex2D(SnowSampler, snowUV);
    float2 rainUV = float2((In.TexCoord.x - 0.5) * 0.3 + 0.5, In.TexCoord.y);
    float4 rain = tex2D(RainSampler, rainUV);
    float4 color = lerp(snow, rain, In.Opacity_Liquidity.y);
    color.a *= In.Opacity_Liquidity.x;
    clip(color.a - 0.002);
    float daylight = lerp(0.15, 0.9, smoothstep(-0.1, 0.1, LightVector.y));
    color.rgb *= daylight;
    return color;
}

technique Precipitation
{
    pass Pass_0
    {
        VertexShader = compile vs_4_0_level_9_1 VSPrecipitation();
        PixelShader = compile ps_4_0_level_9_1 PSPrecipitation();
    }
}
