// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
// Glass-space beads and running droplets; deliberately no full-scene render target.
texture ImageTexture;
texture WindowTexture;
texture WipeTexture;
sampler ImageSampler : register(s0) = sampler_state { Texture = (ImageTexture); };
sampler WindowSampler : register(s1) = sampler_state
{
    Texture = (WindowTexture);
    MinFilter = Linear; MagFilter = Linear; MipFilter = Linear;
    AddressU = Clamp; AddressV = Clamp;
};
sampler WipeSampler : register(s2) = sampler_state
{
    Texture = (WipeTexture);
    MinFilter = Point; MagFilter = Point; MipFilter = Point;
    AddressU = Clamp; AddressV = Clamp;
};
float UseWipeMask;
float WipeRecoveryTime;
float RainTime;
float Wetness;
float PaneAspect;
float Daylight;

float Hash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

// Returns coverage and the rim's highlight. Cells have jittered positions and sizes.
float2 Drops(float2 uv, float density, float speed, float seed)
{
    float2 p = uv * float2(PaneAspect, 1) * density;
    p.y -= RainTime * speed;
    float2 cell = floor(p);
    float random = Hash(cell + seed);
    float2 center = float2(0.2 + random * 0.6, 0.2 + Hash(cell + seed + 13) * 0.6);
    float2 d = frac(p) - center;
    float radius = lerp(0.055, 0.13, Hash(cell + seed + 29));
    float2 shape = d / float2(radius, radius * (speed > 0 ? 1.6 : 1));
    float distance = length(shape);
    float edge = 1 - smoothstep(0.85, 1.15, distance);
    float rim = smoothstep(0.4, 0.9, distance);
    float highlight = saturate(0.5 + (shape.y - shape.x) * 0.4) * rim;
    float present = 1 - smoothstep(Wetness * 0.85, Wetness * 0.85 + 0.08, random);
    // Beads vary at independent times instead of pulsing together.
    float life = 0.65 + 0.35 * sin(RainTime * 0.7 + random * 40);
    float trail = (1 - smoothstep(radius * 0.12, radius * 0.35, abs(d.x)))
        * smoothstep(-0.42, -0.1, d.y) * (1 - smoothstep(-0.1, 0, d.y));
    return float2(max(edge * (0.35 + rim * 0.65), trail * (speed > 0 ? 0.16 : 0)), highlight)
        * present * life;
}

float4 RainPS(float4 position : SV_POSITION, float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    // Alpha is opacity: transparent/grey areas are glass, white-alpha areas stay dry.
    // Intersect the dedicated mask with the current day/night/lit cab texture.
    float glass = min(1 - tex2D(ImageSampler, uv).a, 1 - tex2D(WindowSampler, uv).a);
    clip(glass - 0.01);
    float2 beads = Drops(uv, 62, 0, 2);
    float2 runners = Drops(uv, 32, 0.65, 19);
    float coverage = max(beads.x, runners.x);
    // Timestamp mask is in cab-image UV space, so panning and letterboxing also
    // move the swept area. Briefly hold it clear, then recover with new rain/snow.
    float recovery = saturate(WipeRecoveryTime - tex2D(WipeSampler, uv).r - 0.15);
    coverage *= lerp(1, recovery, UseWipeMask);
    float highlight = max(beads.y, runners.y);
    float brightness = lerp(0.075, 0.42, Daylight) + highlight * lerp(0.16, 0.42, Daylight);
    return float4(brightness * float3(0.91, 0.96, 1), coverage * glass * saturate(Wetness * 4) * 0.55);
}

technique CabRain
{
    pass P0 { PixelShader = compile ps_4_0 RainPS(); }
}
