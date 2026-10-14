// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
// Integrate density over the viewing ray, rather than assigning the endpoint's density to the entire ray.
float FogDensityAlongRay(float cameraHeight, float surfaceHeight, float baseHeight, float heightScale)
{
    if (heightScale <= 0) return 1;
    float a = cameraHeight - baseHeight;
    float b = surfaceHeight - baseHeight;
    if (a <= 0 && b <= 0) return 1;
    if (a >= 0 && b >= 0)
    {
        float delta = (b - a) / heightScale;
        if (abs(delta) < 0.001) return exp(-(a + b) / (2 * heightScale));
        return (exp(-a / heightScale) - exp(-b / heightScale)) / delta;
    }
    float above = max(a, b);
    float below = -min(a, b);
    return (below + heightScale * (1 - exp(-above / heightScale))) / (above + below);
}
