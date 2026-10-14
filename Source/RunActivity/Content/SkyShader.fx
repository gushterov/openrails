// COPYRIGHT 2009 - 2023 by the Open Rails project.
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
//                     S H A D O W   M A P   S H A D E R                      //
////////////////////////////////////////////////////////////////////////////////

////////////////////    G L O B A L   V A L U E S    ///////////////////////////

float4x4 WorldViewProjection;  // model -> world -> view -> projection
float4   LightVector;  // Direction vector to sun, w = 1/length of vector
float    Time;  // Used for moving textures across the sky
float4   Overcast;  // x = alpha, y = contrast, z = brightness, w = !Overcast.y && !Overcast.z
float4   CloudScalePosition;
float    ProceduralSky;
float3   SkyZenith;
float3   SkyHorizon;
float3   SunTint;
float3   CloudLight;
float3   CloudShade;
float4   WeatherAtmosphere; // cover, daylight, effective visibility, direct sunlight fraction
float3   SkyColor;
float3   FogColor;
float4   Fog;
float2   MoonColor;
float2   MoonTexCoord;
float    CloudColor;
float3   RightVector;
float3   UpVector;
texture  SkyMapTexture;
texture  StarMapTexture;
texture  MoonMapTexture;
texture  MoonMaskTexture;
texture  CloudMapTexture;

sampler SkyMapSampler = sampler_state
{
	Texture = (SkyMapTexture);
	MAGFILTER = LINEAR;
	MINFILTER = LINEAR;
	MIPFILTER = LINEAR;
	MIPLODBIAS = 0.000000;
	AddressU = Wrap;
	AddressV = Wrap;
};

sampler StarMapSampler = sampler_state
{
	Texture = (StarMapTexture);
	MAGFILTER = LINEAR;
	MINFILTER = LINEAR;
	MIPFILTER = LINEAR;
	MIPLODBIAS = 0.000000;
	AddressU = wrap;
	AddressV = wrap;
};

sampler MoonMapSampler = sampler_state
{
	Texture = (MoonMapTexture);
	MAGFILTER = LINEAR;
	MINFILTER = LINEAR;
	MIPFILTER = LINEAR;
	MIPLODBIAS = 0.000000;
	AddressU = wrap;
	AddressV = wrap;
};

sampler MoonMaskSampler = sampler_state
{
	Texture = (MoonMaskTexture);
	MAGFILTER = LINEAR;
	MINFILTER = LINEAR;
	MIPFILTER = LINEAR;
	MIPLODBIAS = 0.000000;
	AddressU = wrap;
	AddressV = wrap;
};

sampler CloudMapSampler = sampler_state
{
	Texture = (CloudMapTexture);
	MAGFILTER = LINEAR;
	MINFILTER = LINEAR;
	MIPFILTER = LINEAR;
	MIPLODBIAS = 0.000000;
	AddressU = wrap;
	AddressV = wrap;
};

////////////////////    V E R T E X   I N P U T S    ///////////////////////////

struct VERTEX_INPUT
{
	float4 Pos      : POSITION;
	float3 Normal   : NORMAL;
	float2 TexCoord : TEXCOORD0;
};

////////////////////    V E R T E X   O U T P U T S    /////////////////////////

struct VERTEX_OUTPUT
{
	float4 Pos      : POSITION;
	float3 Normal   : TEXCOORD0;
	float2 TexCoord : TEXCOORD1;
};

/////////////////////    V E R T E X     S H A D E R S    /////////////////////////////

VERTEX_OUTPUT VSSky(VERTEX_INPUT In)
{
	VERTEX_OUTPUT Out = (VERTEX_OUTPUT)0;

	Out.Pos      = mul(WorldViewProjection, In.Pos);
	Out.Normal   = In.Normal;
	Out.TexCoord = In.TexCoord;	
		
	return Out;
}

VERTEX_OUTPUT VSMoon(VERTEX_INPUT In)
{
	VERTEX_OUTPUT Out = (VERTEX_OUTPUT)0;
	
	float3 position = In.Pos.xyz; 
	position += (In.TexCoord.x - 0.5) * RightVector;
	position += (In.TexCoord.y - 0.5) * UpVector;

	Out.Pos      = mul(WorldViewProjection, float4(position, 1));
	Out.Normal   = In.Normal;
	Out.TexCoord = In.TexCoord;

	return Out;
}

/////////////////////    P I X E L     S H A D E R S    ///////////////////////////////

// This function adjusts brightness, saturation and contrast
// By Romain Dura aka Romz
// Colors edit by DR_Aeronautics
float3 ContrastSaturationBrightness(float3 color, float brt, float sat, float con)
{
	// Increase or decrease theese values to adjust r, g and b color channels separately
	const float AvgLumR = 0.5;
	const float AvgLumG = 0.5;
	const float AvgLumB = 0.5;
	
	const float3 LumCoeff = float3(0.2125, 0.7154, 0.0721);
	
	float3 AvgLumin = float3(AvgLumR, AvgLumG, AvgLumB);
	float3 brtColor = color * brt;
	float intensityf = dot(brtColor, LumCoeff);
	float3 intensity = float3(intensityf, intensityf, intensityf);
	float3 satColor = lerp(intensity, brtColor, sat);
	float3 conColor = lerp(AvgLumin, satColor, con);
	return conColor;
}

float4 PSTexturedSky(VERTEX_OUTPUT In) : COLOR
{
	// Get the color information for the current pixel
	float4 skyColor = tex2D(SkyMapSampler, In.TexCoord);
	float2 TexCoord = float2((1.0 - In.TexCoord.x) + Time, In.TexCoord.y);
	float4 starColor = tex2D(StarMapSampler, TexCoord);
	
	// Adjust sky color brightness for time of day
	skyColor *= SkyColor.y;
	
	// Stars (power function keeps stars hidden until after sunset)
	// if-statement handles astronomical/final stage of twilight
	if (LightVector.y < -0.2)
		{
		skyColor = lerp(starColor, skyColor, LightVector.y*6.6+2.22);
		}
		else 
		{
		skyColor = lerp(starColor, skyColor, pow(abs(SkyColor.y),0.125));
		}
	
	// Fogging
	skyColor.rgb = lerp(skyColor.rgb, FogColor.rgb, saturate((1 - In.Normal.y) * Fog.x));
	
	// Calculate angular difference between LightVector and vertex normal, radians
	float dotproduct = dot(LightVector.xyz, In.Normal);
	float angleRcp = 1 / acos(dotproduct * LightVector.w / length(In.Normal));
	
	// Sun glow
	// Coefficients selected by the author to achieve the desired appearance - fot limits the effect
	skyColor += angleRcp * Fog.y;
	
	// increase orange at sunset and yellow at sunrise - fog limits the effect
	if (LightVector.x < 0)
	{
		// These if-statements prevent the yellow-flash effect
		if (LightVector.y > 0.13)
		{
			skyColor.rg += SkyColor.z*2 * angleRcp * Fog.z;
			skyColor.r += SkyColor.z*2 * angleRcp * Fog.z;
		}
	
		else
		{
			skyColor.rg += angleRcp * 0.075 * SkyColor.y;
			skyColor.r += angleRcp * 0.075 * SkyColor.y;
		}
	}
	else
	{
		if (LightVector.y > 0.15)
		{
			skyColor.rg += SkyColor.z*3 * angleRcp * Fog.z;
			skyColor.r += SkyColor.z * angleRcp * Fog.z;
		}
	
		else
		{
			skyColor.rg += angleRcp * 0.075 * SkyColor.y;
			skyColor.r += pow(angleRcp * 0.075 * SkyColor.y,2);
		}
	}
	
	// Keep alpha opague
	skyColor.a = 1.0;
	return skyColor;
}

float4 PSProceduralSky(VERTEX_OUTPUT In) : COLOR
{
    float3 direction = normalize(In.Normal);
    float height = saturate(direction.y);
    float3 color = lerp(SkyHorizon, SkyZenith, pow(height, 0.45));
    float alignment = saturate(dot(direction, normalize(LightVector.xyz)));
    float sunset = (1 - smoothstep(0.03, 0.22, abs(LightVector.y))) * smoothstep(-0.12, 0.02, LightVector.y);
    color = lerp(color, SunTint * float3(0.7, 0.65, 0.6), sunset * exp(-height * 6) * pow(alignment, 4) * WeatherAtmosphere.w * 0.75);
    float sunVisible = smoothstep(-0.006, 0.006, LightVector.y) * WeatherAtmosphere.w;
    float sunGlow = pow(alignment, 32) * 0.12 + pow(alignment, 256) * 0.2;
    float sunDisc = smoothstep(0.99995, 0.999989, alignment);
    color += SunTint * (sunGlow + sunDisc * 1.4) * sunVisible;
    float2 starCoord = float2(1 - In.TexCoord.x + Time, In.TexCoord.y);
    float stars = (1 - WeatherAtmosphere.y) * (1 - WeatherAtmosphere.y) * WeatherAtmosphere.w;
    color += tex2D(StarMapSampler, starCoord).rgb * stars;
    return float4(color, 1);
}

float4 PSMoon(VERTEX_OUTPUT In) : COLOR
{
	// Get the color information for the current pixel
	float2 TexCoord = float2(MoonTexCoord.x + In.TexCoord.x * 0.5, MoonTexCoord.y + In.TexCoord.y * 0.25);
	float4 moonColor = tex2D(MoonMapSampler, TexCoord);
	float4 moonMask = tex2D(MoonMaskSampler, In.TexCoord);
	
	// Fade moon during daylight
	moonColor.a *= MoonColor.x;
	
	// Fogging
	moonColor.rgb = lerp(moonColor.rgb, ProceduralSky ? SkyHorizon : FogColor.rgb, saturate((1 - In.Normal.y) * Fog.x));
	
	// Mask stars behind dark side (mask fades in)
	moonColor.a += moonMask.r * MoonColor.y;
		
	return moonColor;
}

float4 PSProceduralClouds(VERTEX_OUTPUT In) : COLOR
{
        float cover = saturate(WeatherAtmosphere.x);
        float2 lowCoord = In.TexCoord * CloudScalePosition.xy - CloudScalePosition.zw;
        float2 highCoord = In.TexCoord * CloudScalePosition.xy * 0.47 - CloudScalePosition.zw * 0.35 + float2(0.37, 0.63);
        // The existing cloud texture carries a useful density mask in its alpha, independent of its baked RGB.
        float lowDensity = saturate((tex2D(CloudMapSampler, lowCoord).a - 0.4) * 2.5);
        float highDensity = saturate((tex2D(CloudMapSampler, highCoord).a - 0.4) * 2.5);
        // Mixing nonmatching scales reduces repetition without loading another large texture.
        lowDensity = lowDensity * 0.7 + highDensity * 0.3;
        float threshold = lerp(0.98, 0.02, cover);
        float lowAlpha = smoothstep(threshold - 0.16, threshold + 0.16, lowDensity) * smoothstep(0, 0.08, cover);
        lowAlpha = max(lowAlpha, smoothstep(0.8, 1, cover) * 0.96);
        float highAlpha = smoothstep(0.65, 0.9, highDensity) * cover * (1 - cover) * 0.55;
        float alpha = 1 - (1 - lowAlpha) * (1 - highAlpha);
        float3 lowerColor = lerp(CloudLight, CloudShade, saturate(lowDensity * (0.4 + cover * 0.55)));
        float3 color = lerp(CloudLight, lowerColor, lowAlpha / max(alpha, 0.001));
        float rim = pow(saturate(dot(normalize(In.Normal), normalize(LightVector.xyz))), 8);
        color += SunTint * rim * 0.1 * WeatherAtmosphere.y * WeatherAtmosphere.w;
        float horizon = 1 - smoothstep(0, 0.25, In.Normal.y);
        color = lerp(color, SkyHorizon, horizon);
        alpha *= 1 - horizon * 0.7;
        return float4(color, saturate(alpha));
}

float4 PSClouds(VERTEX_OUTPUT In) : COLOR
{
	float2 TexCoord = In.TexCoord.xy * CloudScalePosition.xy - CloudScalePosition.zw;
	float4 cloudColor = tex2D(CloudMapSampler, TexCoord);
	float alpha = cloudColor.a;
	
    // Fogging
    cloudColor.rgb = lerp(cloudColor.rgb, FogColor.rgb, saturate((1 - In.Normal.y) * Fog.x));
	
    // Adjust amount of overcast by adjusting alpha
	if (Overcast.w)
	{
		alpha += Overcast.x;
		// Reduce contrast and brightness
		float3 color = ContrastSaturationBrightness(cloudColor.xyz, 1.0, Overcast.z, Overcast.y); // Brightness and saturation are really need to be exchanged?
		cloudColor = float4(color, alpha);
	}
	else
	{
		alpha *= Overcast.x;
	}

	// Adjust cloud color brightness for time of day
	cloudColor *= CloudColor;
	cloudColor.a = alpha;
	return cloudColor;
}

///////////////////////////    T E C H N I Q U E S    ///////////////////////////////

// These techniques are all the same, but we'll keep them separate for now.

technique Sky {
   pass Pass_0 {
	  VertexShader = compile vs_4_0_level_9_1 VSSky();
	  PixelShader = compile ps_4_0_level_9_1 PSTexturedSky();
   }
}

technique Moon {
   pass Pass_0 {
	  VertexShader = compile vs_4_0_level_9_1 VSMoon();
	  PixelShader = compile ps_4_0_level_9_1 PSMoon();
   }
}

technique Clouds {
   pass Pass_0 {
	  VertexShader = compile vs_4_0_level_9_1 VSSky();
	  PixelShader = compile ps_4_0_level_9_1 PSClouds();
   }
}

technique SkyProcedural {
   pass Pass_0 {
      VertexShader = compile vs_4_0 VSSky();
      PixelShader = compile ps_4_0 PSProceduralSky();
   }
}

technique CloudsProcedural {
   pass Pass_0 {
      VertexShader = compile vs_4_0 VSSky();
      PixelShader = compile ps_4_0 PSProceduralClouds();
   }
}
