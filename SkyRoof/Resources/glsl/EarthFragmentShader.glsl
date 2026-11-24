#version 330 core

#define Pi 3.1415926538
#define TwoPi (2 * Pi)

uniform float in_homeLat;
uniform float in_homeLon;
uniform float in_satLat;
uniform float in_satLon;
uniform float in_footprint;
uniform float in_brightness;

uniform sampler2D worldMapTexture;

in vec2 pass_RoPhi;
out vec4 out_Color;

vec3 geoVector(float lat, float lon)
{
  return vec3(cos(lat) * cos(lon), cos(lat) * sin(lon), sin(lat));
}

void main(void)
{
  float ro = pass_RoPhi[0] * Pi;
  float phi = pass_RoPhi[1];

  // Inverse azimuthal-equidistant projection in an Earth-fixed basis.
  // Unlike a division by cos(latitude), this remains valid at either pole.
  vec3 center = geoVector(in_homeLat, in_homeLon);
  vec3 east = vec3(-sin(in_homeLon), cos(in_homeLon), 0.0);
  vec3 north = vec3(-sin(in_homeLat) * cos(in_homeLon),
                    -sin(in_homeLat) * sin(in_homeLon), cos(in_homeLat));
  vec3 point = center * cos(ro) +
    (east * cos(phi) + north * sin(phi)) * sin(ro);
  float lat = asin(clamp(point.z, -1.0, 1.0));
  float lon = atan(point.y, point.x);

  out_Color = texture(worldMapTexture, vec2(0.5 + lon / TwoPi, 0.5 - lat / Pi));
  out_Color.rgb *= in_brightness;

  // Footprint belongs to the satellite, not to the movable view centre.
  if (in_footprint < Pi &&
      dot(point, geoVector(in_satLat, in_satLon)) < cos(in_footprint))
    out_Color *= 0.8;
}
