# Weather presets and procedural sky

Explore and timetable runs offer Cloudy, Overcast, Foggy day, Rain/snowing day, Heavy rain/snow, Storm, and Dynamic in the existing Weather selector. Clear, Rain, and Snow retain their original MSTS weather values. Clear free runs also use the editable Clear profile, unless the existing experimental activity-weather randomization option is enabled. Activities use their authored weather; timetable weather files also take precedence over menu presets.

The standard sky on HiDef-capable graphics hardware is generated procedurally from the existing solar position. Clouds use two differently scaled, wind-driven samples of the existing cloud texture, rather than clouds baked into a sky photograph. Their opacity and illumination respond to the same weather snapshot as the scenery and fog. The route's MSTS environment option and Reach graphics profile retain compatibility rendering.

Fixed presets define cloud cover, visibility, precipitation, wind speed, and the height of an exponential fog layer. Seasonal defaults define temperature, visibility scaling, and the chance of wet conditions in Dynamic weather. Winter's default temperature produces snow; editing the temperature can produce rain or mixed precipitation in another season. Storm supplies strong wind and precipitation; it does not add a lightning system.

Edit the installed `Content/WeatherPresets.json` to tune these values. The file is copied from `Source/RunActivity/Content/WeatherPresets.json` when building. Unknown entries and invalid profiles are reported in the log and retain safe built-in defaults. A missing or malformed file also uses defaults.

| Value | Meaning and accepted range |
|---|---|
| `CloudCover` | 0–1 coverage control |
| `VisibilityM` | 10–100000 metres before seasonal scaling and precipitation attenuation |
| `PrecipitationPPSPM2` | 0–0.015, using the current precipitation renderer's intensity scale |
| `WindSpeedMpS` | 0–40 metres per second |
| `FogHeightScaleM` | 0–5000 metres; 0 means uniform distance fog |
| `TemperatureC` | −60–60 degrees Celsius; default liquid fraction transitions from snow at −1°C to rain at 2°C |
| `VisibilityMultiplier` | 0.1–3 seasonal multiplier |
| `WetWeatherProbability` | 0–1 chance of choosing a wet target in Dynamic weather |

Fog starts around the run's initial locomotive altitude and decreases exponentially above that level. The shader integrates density along the camera-to-surface ray, including rays crossing the bottom of the layer. Existing authored weather uses uniform fog unless a preset supplies a layer. The sky and fog share their horizon color. This is an inexpensive procedural atmosphere with artistic color controls, rather than a full atmospheric scattering or volumetric-cloud simulation.

Dynamic weather changes targets on simulation time. Cloud cover and extinction change smoothly; incoming rain waits for clouds to thicken, and outgoing rain clears before the sky does. Manual weather controls and authored weather events take ownership from the preset controller. Rendering reads an immutable per-frame atmosphere snapshot and does not change simulation weather.

New fixed-weather save blocks use discriminator 2 and store atmospheric values, cloud displacement and its camera anchor, wind-noise state, and the controller's random state and transition endpoints. The loader also accepts the original discriminator 0 without consuming any additional bytes. Authored timetable weather keeps its existing discriminator 1. New save blocks require this updated program to resume.

Multiplayer hosts remain authoritative. WEATHER messages retain the original four leading fields, with optional atmospheric fields appended for updated clients; the new reader accepts old four-field packets. Updated hosts send the running preset state periodically and include a current snapshot when a client joins. Older clients retain their existing rendering and ignore the appended fields.

Sky, scenery, and particle effects are compiled together with the renderer by the runtime shader build target. Deploy those `.mgfx` files with the updated executable; mismatched effects and executables are not supported.
